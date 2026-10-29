using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Roamint.Receipts.Application;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Infrastructure;

/// <summary>
/// Reads a household chat message (Persian free text, 0024) over the same <see cref="IChatClient"/> as receipt OCR.
/// Returns the model's best guess; validation is the handler's job. An unparseable reply means "couldn't read
/// it" and throws, so the item fails (and is retried) instead of silently becoming chatter.
/// </summary>
internal sealed class ChatClientInboxExtractor(IChatClient chat) : IInboxExtractor
{
    private const string Prompt =
        """
        You read one message from a household expense chat in Armenia. Messages are usually Persian (Farsi),
        sometimes English, e.g. "من 15 هزار درام برای شام امشب دادم" (I paid 15 thousand dram for dinner tonight).
        Return ONLY a JSON object — no prose, no markdown, no code fences — with exactly this shape:
        {
          "isExpense": boolean,
          "amount": number|null,
          "currencyCode": "AMD"|"USD"|"USDT"|null,
          "description": string|null,
          "date": "YYYY-MM-DD"|null
        }
        Rules:
        - "isExpense" is false for anything that is not a report of money spent (questions, chatter, plans).
        - "amount" is the full number: "15 هزار" / "۱۵ هزار" = 15000, "2.5 میلیون" = 2500000. Persian and Arabic
          digits are digits.
        - Currency words: درام / dram / AMD → "AMD"; دلار / dollar / $ → "USD"; تتر / tether / USDT → "USDT".
          If no currency is stated, use null; do not guess.
        - "description" is what was bought or paid for, as a short Persian noun phrase without the amount or the
          currency, e.g. "شام" for dinner, "تاکسی" for a taxi.
        - "date" is when it was spent. The message was sent on SENT_ON; resolve words like امروز / امشب / today /
          tonight to that date and دیروز / yesterday to the day before. Use null if no day is implied.
        Message:
        MESSAGE
        """;

    public async Task<InboxExtraction> ExtractAsync(string text, DateOnly sentOn, CancellationToken ct)
    {
        var prompt = Prompt
            .Replace("SENT_ON", sentOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("MESSAGE", text, StringComparison.Ordinal);
        var response = await chat.GetResponseAsync(
            [new ChatMessage(ChatRole.User, prompt)], new ChatOptions { Temperature = 0f }, ct);
        return Parse(response.Text);
    }

    internal static InboxExtraction Parse(string? reply)
    {
        var start = reply?.IndexOf('{', StringComparison.Ordinal) ?? -1;
        var end = reply?.LastIndexOf('}') ?? -1;
        if (start < 0 || end <= start)
        {
            throw new InvalidOperationException("The extractor reply held no JSON object.");
        }

        var dto = JsonSerializer.Deserialize<ExtractionDto>(reply![start..(end + 1)], ParsedDraftConverter.Options)
            ?? throw new InvalidOperationException("The extractor reply was empty JSON.");

        DateOnly? date = DateOnly.TryParseExact(
            dto.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

        return new InboxExtraction(dto.IsExpense, dto.Amount, dto.CurrencyCode, dto.Description, date);
    }

    private sealed record ExtractionDto(
        bool IsExpense, decimal? Amount, string? CurrencyCode, string? Description, string? Date);
}

/// <summary>Extractor fallback when no API key is configured: every item fails (and is retried once a key is
/// set) rather than being guessed at.</summary>
internal sealed class UnconfiguredInboxExtractor : IInboxExtractor
{
    public Task<InboxExtraction> ExtractAsync(string text, DateOnly sentOn, CancellationToken ct) =>
        throw new InvalidOperationException("No extractor configured (Ocr:ApiKey is not set).");
}

/// <summary>
/// Telegram feedback for inbox items (0024): the bot's reaction on the original message is its status light, and
/// an optional reply in its thread. The item's <see cref="InboxItem.ExternalId"/> is <c>"&lt;chat&gt;:&lt;message&gt;"</c>.
/// Bots hold one reaction per message and setting one replaces the relay's 👀; the emoji must be from Telegram's
/// fixed reaction set (hence 👍/✍/🤷, not ✅/📝). Failures throw; callers treat feedback as best-effort.
/// </summary>
internal sealed class TelegramInboxNotifier(string botToken, HttpMessageHandler? handler = null) : IInboxNotifier, IDisposable
{
    private readonly HttpClient _http = handler is null ? new() : new(handler);

    public async Task ReactAsync(InboxItem item, InboxReaction reaction, CancellationToken ct)
    {
        var (chatId, messageId) = Address(item);
        object[] emoji = reaction switch
        {
            InboxReaction.Recorded => [new { type = "emoji", emoji = "👍" }],
            InboxReaction.NeedsReview => [new { type = "emoji", emoji = "✍" }],
            InboxReaction.Failed => [new { type = "emoji", emoji = "🤷" }],
            _ => [],
        };

        await PostAsync("setMessageReaction", new { chat_id = chatId, message_id = messageId, reaction = emoji }, ct);
    }

    public async Task ReplyAsync(InboxItem item, string text, CancellationToken ct)
    {
        var (chatId, messageId) = Address(item);
        await PostAsync(
            "sendMessage",
            new { chat_id = chatId, text, reply_parameters = new { message_id = messageId } },
            ct);
    }

    public void Dispose() => _http.Dispose();

    private async Task PostAsync(string method, object body, CancellationToken ct)
    {
        // An absolute URI: as a relative path, "bot123:ABC/..." parses as an absolute URI with the scheme "bot123".
        using var response = await _http.PostAsJsonAsync(new Uri($"https://api.telegram.org/bot{botToken}/{method}"), body, ct);
        response.EnsureSuccessStatusCode();
    }

    private static (long ChatId, long MessageId) Address(InboxItem item)
    {
        var parts = item.ExternalId.Split(':');
        if (parts.Length != 2
            || !long.TryParse(parts[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var chatId)
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var messageId))
        {
            throw new InvalidOperationException($"'{item.ExternalId}' is not a Telegram chat:message address.");
        }

        return (chatId, messageId);
    }
}

/// <summary>Notifier fallback when no bot token is configured: feedback is silently skipped.</summary>
internal sealed class NoopInboxNotifier : IInboxNotifier
{
    public Task ReactAsync(InboxItem item, InboxReaction reaction, CancellationToken ct) => Task.CompletedTask;

    public Task ReplyAsync(InboxItem item, string text, CancellationToken ct) => Task.CompletedTask;
}
