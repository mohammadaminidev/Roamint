using Roamint.Application.Abstractions;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Application.Inbox;

/// <summary>Inbox items not yet settled — <see cref="InboxItemStatus.Received"/>,
/// <see cref="InboxItemStatus.Parsed"/> (awaiting review) or <see cref="InboxItemStatus.Failed"/> — oldest first.
/// The inbox page lists these (0026); the host's startup sweep re-enqueues them so a crash or restart loses nothing.
/// Read model in Infrastructure.</summary>
public sealed record ListOpenInboxItems : IQuery<IReadOnlyList<InboxItemView>>;
