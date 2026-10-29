using System.Globalization;
using System.Text.Json;
using Roamint.Application.Abstractions;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Application.Inbox;

/// <summary>One relay row as drained by the host (0024): the source tag, the source's message id, and the raw
/// payload the relay stored.</summary>
public sealed record InboxEnvelope(string Source, string ExternalId, string Payload);

/// <summary>
/// Lands drained relay envelopes as <see cref="InboxItem"/>s. Idempotent on <c>(source, externalId)</c>: an
/// envelope already accepted (the host crashed before acking, or the relay redelivered) is skipped. Envelopes
/// this module can't read — an unknown source, or a payload without text — are skipped too; the relay only
/// stores readable Telegram text, so these are noise, and the host acks them all the same. Returns the ids of
/// the newly accepted items, in envelope order.
/// </summary>
public sealed record AcceptInboxEnvelopes(IReadOnlyList<InboxEnvelope> Envelopes)
    : ICommand<IReadOnlyList<InboxItemId>>;

internal sealed class AcceptInboxEnvelopesHandler(
    IInboxItemRepository items,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<AcceptInboxEnvelopes, IReadOnlyList<InboxItemId>>
{
    public const string TelegramSource = "telegram";

    public async Task<IReadOnlyList<InboxItemId>> HandleAsync(AcceptInboxEnvelopes command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var accepted = new List<InboxItemId>();
        var seen = new HashSet<(string, string)>();
        foreach (var envelope in command.Envelopes)
        {
            if (envelope.Source != TelegramSource
                || !seen.Add((envelope.Source, envelope.ExternalId))
                || !TelegramMessage.TryRead(envelope.Payload, out var message)
                || await items.FindAsync(envelope.Source, envelope.ExternalId, ct) is not null)
            {
                continue;
            }

            var item = InboxItem.Receive(
                InboxItemId.New(clock),
                envelope.Source,
                envelope.ExternalId,
                message.SenderId,
                message.Text,
                message.SentOnUtc,
                envelope.Payload,
                clock);
            await items.AddAsync(item, ct);
            accepted.Add(item.Id);
        }

        if (accepted.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(ct);
        }

        return accepted;
    }
}

/// <summary>The fields Roamint needs from a Telegram <c>Update</c>: text, sender id, and send time.</summary>
internal readonly record struct TelegramMessage(string Text, string? SenderId, DateTimeOffset SentOnUtc)
{
    public static bool TryRead(string payload, out TelegramMessage message)
    {
        message = default;
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("message", out var body)
                || !body.TryGetProperty("text", out var text)
                || text.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(text.GetString())
                || !body.TryGetProperty("date", out var date)
                || !date.TryGetInt64(out var unixSeconds))
            {
                return false;
            }

            var senderId = body.TryGetProperty("from", out var from)
                && from.TryGetProperty("id", out var id)
                && id.TryGetInt64(out var userId)
                    ? userId.ToString(CultureInfo.InvariantCulture)
                    : null;

            message = new TelegramMessage(text.GetString()!, senderId, DateTimeOffset.FromUnixTimeSeconds(unixSeconds));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
