using System.Text.Json;
using Microsoft.Extensions.AI;
using Roamint.Receipts.Application;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Infrastructure;

/// <summary>
/// Provider-agnostic OCR over an <see cref="IChatClient"/> (Gemini by default; see 0015). Sends the image
/// plus a structured-extraction prompt and parses the JSON reply into a <see cref="ParsedDraft"/>. A reply
/// that cannot be parsed degrades to <see cref="ParsedDraft.Empty"/> (the user fills the form by hand) rather
/// than throwing — transport failures propagate and are turned into a failed receipt by the handler.
/// </summary>
internal sealed class ChatClientReceiptOcr(IChatClient chat) : IReceiptOcr
{
    private const string Prompt =
        """
        You are a receipt OCR extractor. Read the receipt image and return ONLY a JSON object — no prose,
        no markdown, no code fences — with exactly this shape:
        {
          "merchant": string|null,
          "date": "YYYY-MM-DD"|null,
          "currencyCode": string|null,
          "lines": [ {
            "sourceText": string,
            "description": string,
            "amount": number,
            "quantityValue": number|null,
            "quantityUnit": string|null
          } ]
        }
        For each line: "sourceText" is the item name EXACTLY as printed (keep the original language and
        script, e.g. Armenian); "description" is a concise Farsi (Persian) translation of that item name.
        "amount" is the line total actually paid. Do NOT include subtotal, tax, or grand-total rows in "lines".
        """;

    public async Task<ParsedDraft> ExtractAsync(byte[] image, string contentType, CancellationToken ct)
    {
        var message = new ChatMessage(ChatRole.User,
        [
            new TextContent(Prompt),
            new DataContent(image, contentType),
        ]);

        var response = await chat.GetResponseAsync([message], new ChatOptions { Temperature = 0f }, ct);
        return Parse(response.Text);
    }

    private static ParsedDraft Parse(string? text)
    {
        var json = ExtractJsonObject(text);
        if (json is null)
        {
            return ParsedDraft.Empty;
        }

        DraftDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<DraftDto>(json, ParsedDraftConverter.Options);
        }
        catch (JsonException)
        {
            return ParsedDraft.Empty;
        }

        if (dto is null)
        {
            return ParsedDraft.Empty;
        }

        var lines = (dto.Lines ?? [])
            .Where(line => !string.IsNullOrWhiteSpace(line.Description) || !string.IsNullOrWhiteSpace(line.SourceText))
            .Select(line => new ParsedLine(
                NullIfBlank(line.Description) ?? line.SourceText!,
                line.Amount ?? 0m,
                line.QuantityValue,
                line.QuantityUnit,
                NullIfBlank(line.SourceText)))
            .ToList();

        DateOnly? date = DateOnly.TryParse(dto.Date, out var parsed) ? parsed : null;

        return new ParsedDraft(NullIfBlank(dto.Merchant), date, NullIfBlank(dto.CurrencyCode), lines);
    }

    /// <summary>Pulls the outermost <c>{ … }</c> out of the reply, tolerating stray prose or code fences.</summary>
    private static string? ExtractJsonObject(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var start = text.IndexOf('{', StringComparison.Ordinal);
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : null;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private sealed record DraftDto(string? Merchant, string? Date, string? CurrencyCode, List<LineDto>? Lines);

    private sealed record LineDto(
        string? SourceText, string? Description, decimal? Amount, decimal? QuantityValue, string? QuantityUnit);
}

/// <summary>OCR fallback when no API key/endpoint is configured (e.g. the GPU-less production host). Returns
/// an empty draft so upload still succeeds and the review screen opens for manual entry.</summary>
internal sealed class StubReceiptOcr : IReceiptOcr
{
    public Task<ParsedDraft> ExtractAsync(byte[] image, string contentType, CancellationToken ct) =>
        Task.FromResult(ParsedDraft.Empty);
}
