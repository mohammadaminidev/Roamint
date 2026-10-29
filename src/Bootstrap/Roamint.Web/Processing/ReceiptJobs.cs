using System.Threading.Channels;
using Roamint.Web.Capture;

namespace Roamint.Web.Processing;

/// <summary>The honest, discrete stages a backgrounded receipt moves through (0019). Deliberately not a
/// percentage — Gemini is one opaque call, so the bar animates an indeterminate indicator per stage.</summary>
public enum ReceiptJobStage
{
    Queued,
    Reading,
    Matching,
    Ready,
    Failed,
}

/// <summary>One backgrounded receipt as the notification bar sees it. Immutable — the tracker replaces the
/// record on each transition. A <see cref="ReceiptJobStage.Ready"/> job carries the fully-matched
/// <see cref="Seed"/> (so "Review" is instant) plus a one-line summary for the row.</summary>
public sealed record ReceiptJob
{
    public required Guid ReceiptId { get; init; }

    public required long Sequence { get; init; }

    public ReceiptJobStage Stage { get; init; }

    /// <summary>The matched review seed, set when the job reaches <see cref="ReceiptJobStage.Ready"/>.</summary>
    public CaptureSeed? Seed { get; init; }

    public string? Merchant { get; init; }

    public decimal Total { get; init; }

    public int ItemCount { get; init; }

    public string? CurrencyCode { get; init; }

    /// <summary>The failure reason, set when the job reaches <see cref="ReceiptJobStage.Failed"/>.</summary>
    public string? Error { get; init; }
}

/// <summary>
/// In-process source of truth for in-flight and recently-finished receipt jobs (0019). A singleton so the
/// hosted worker and every live Blazor circuit (phone tab, laptop tab) observe one shared queue — correct for
/// a single user. Wraps a bounded <see cref="Channel{T}"/> the worker drains and a small bounded set of job
/// states the bar renders; mutations raise <see cref="Changed"/> so the bar can push (never poll).
/// </summary>
public sealed class ReceiptJobs
{
    // Generous for one user snapping a batch; Wait back-pressures rather than dropping if somehow exceeded.
    private const int QueueCapacity = 64;

    // Keep recently-finished rows around so the user can still tap Review after a few more shots, but cap the
    // retained set so a long session doesn't grow it without bound.
    private const int MaxRetained = 12;

    private readonly Channel<Guid> _queue = Channel.CreateBounded<Guid>(
        new BoundedChannelOptions(QueueCapacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, ReceiptJob> _jobs = [];
    private long _sequence;

    /// <summary>Raised after every job mutation. The bar subscribes and marshals a re-render onto its circuit.</summary>
    public event Action? Changed;

    /// <summary>Registers a receipt and queues it for the worker. Returns once enqueued (near-instant).</summary>
    public async ValueTask EnqueueAsync(Guid receiptId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_jobs.ContainsKey(receiptId))
            {
                return; // already tracked (e.g. the startup sweep re-running) — don't double-queue.
            }

            _jobs[receiptId] = new ReceiptJob
            {
                ReceiptId = receiptId,
                Sequence = ++_sequence,
                Stage = ReceiptJobStage.Queued,
            };
            Trim();
        }

        Raise();
        await _queue.Writer.WriteAsync(receiptId, ct);
    }

    /// <summary>Adds an already-processed capture as a reviewable row without queueing it — the inbox worker's
    /// "needs review" outcome (0024). The id is the inbox item's; a row already tracked is left as-is.</summary>
    public void AddReady(Guid id, CaptureSeed seed, string title, decimal total)
    {
        ArgumentNullException.ThrowIfNull(seed);

        lock (_gate)
        {
            if (_jobs.ContainsKey(id))
            {
                return;
            }

            _jobs[id] = new ReceiptJob
            {
                ReceiptId = id,
                Sequence = ++_sequence,
                Stage = ReceiptJobStage.Ready,
                Seed = seed,
                Merchant = title,
                Total = total,
                ItemCount = seed.Lines.Count,
                CurrencyCode = seed.CurrencyCode,
            };
            Trim();
        }

        Raise();
    }

    /// <summary>The worker's drain loop — yields queued receipt ids until the host shuts down.</summary>
    public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken ct) => _queue.Reader.ReadAllAsync(ct);

    public void Advance(Guid receiptId, ReceiptJobStage stage) =>
        Mutate(receiptId, job => job with { Stage = stage });

    public void MarkReady(Guid receiptId, CaptureSeed seed, string? merchant, decimal total, int itemCount) =>
        Mutate(receiptId, job => job with
        {
            Stage = ReceiptJobStage.Ready,
            Seed = seed,
            Merchant = merchant,
            Total = total,
            ItemCount = itemCount,
            CurrencyCode = seed.CurrencyCode,
        });

    public void MarkFailed(Guid receiptId, string reason) =>
        Mutate(receiptId, job => job with { Stage = ReceiptJobStage.Failed, Error = reason });

    /// <summary>Removes a finished row the user dismissed (or reviewed).</summary>
    public void Dismiss(Guid receiptId)
    {
        lock (_gate)
        {
            if (!_jobs.Remove(receiptId))
            {
                return;
            }
        }

        Raise();
    }

    /// <summary>Current jobs, newest first.</summary>
    public IReadOnlyList<ReceiptJob> Snapshot()
    {
        lock (_gate)
        {
            return _jobs.Values.OrderByDescending(job => job.Sequence).ToList();
        }
    }

    private void Mutate(Guid receiptId, Func<ReceiptJob, ReceiptJob> update)
    {
        lock (_gate)
        {
            if (!_jobs.TryGetValue(receiptId, out var job))
            {
                return;
            }

            _jobs[receiptId] = update(job);
        }

        Raise();
    }

    // Drop the oldest finished (Ready/Failed) rows past the cap; never evicts an in-flight job. Caller holds _gate.
    private void Trim()
    {
        if (_jobs.Count <= MaxRetained)
        {
            return;
        }

        var finished = _jobs.Values
            .Where(job => job.Stage is ReceiptJobStage.Ready or ReceiptJobStage.Failed)
            .OrderBy(job => job.Sequence)
            .ToList();

        foreach (var job in finished)
        {
            if (_jobs.Count <= MaxRetained)
            {
                break;
            }

            _jobs.Remove(job.ReceiptId);
        }
    }

    private void Raise() => Changed?.Invoke();
}
