using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Roamint.Receipts.Contracts;

namespace Roamint.Web.Processing;

/// <summary>The in-process backlog of inbox item ids awaiting <see cref="InboxProcessor"/> (0024). A singleton
/// between the puller (producer) and the processing worker (single consumer).</summary>
public sealed class InboxBacklog
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public ValueTask EnqueueAsync(Guid itemId, CancellationToken ct) => _channel.Writer.WriteAsync(itemId, ct);

    public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}

/// <summary>
/// Drains the Cloudflare relay into Receipts (0024): on startup it first re-enqueues unsettled items (a crash, a
/// restart, or items still awaiting review), then pulls the relay now and every minute. Each batch is persisted
/// before it is acked, so a crash in between only means a redelivery that the accept step dedups. An unreachable
/// relay is logged and retried on the next tick; a missing <c>InboxRelay</c> config leaves only the sweep running.
/// </summary>
internal sealed partial class InboxPuller(
    InboxBacklog queue,
    InboxRelayClient relay,
    IOptions<InboxRelayOptions> options,
    IServiceScopeFactory scopes,
    ILogger<InboxPuller> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using (var scope = scopes.CreateAsyncScope())
        {
            var inbox = scope.ServiceProvider.GetRequiredService<IInboxApi>();
            foreach (var itemId in await inbox.ListOpenAsync(stoppingToken))
            {
                await queue.EnqueueAsync(itemId, stoppingToken);
            }
        }

        if (!options.Value.IsConfigured)
        {
            LogNotConfigured(logger);
            return;
        }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            await PullAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PullAsync(CancellationToken ct)
    {
        try
        {
            while (true)
            {
                var batch = await relay.PullAsync(ct);
                if (batch.Count == 0)
                {
                    return;
                }

                IReadOnlyList<Guid> accepted;
                await using (var scope = scopes.CreateAsyncScope())
                {
                    accepted = await scope.ServiceProvider.GetRequiredService<IInboxApi>().AcceptAsync(
                        batch.Select(item => new InboxEnvelopeDto(item.Source, item.ExternalId, item.Payload)).ToList(),
                        ct);
                }

                await relay.AckAsync(batch.Select(item => item.Id).ToList(), ct);
                foreach (var itemId in accepted)
                {
                    await queue.EnqueueAsync(itemId, ct);
                }

                LogPulled(logger, batch.Count, accepted.Count);
                if (batch.Count < InboxRelayClient.MaxBatch)
                {
                    return;
                }
            }
        }
        catch (HttpRequestException ex)
        {
            LogRelayUnreachable(logger, ex.Message);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            LogRelayUnreachable(logger, ex.Message); // HttpClient timeout
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "InboxRelay is not configured; Telegram capture is idle.")]
    private static partial void LogNotConfigured(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Pulled {Count} relay envelopes; {Accepted} new.")]
    private static partial void LogPulled(ILogger logger, int count, int accepted);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Inbox relay unreachable ({Reason}); retrying next tick.")]
    private static partial void LogRelayUnreachable(ILogger logger, string reason);
}

/// <summary>Drains <see cref="InboxBacklog"/> serially (one extractor call at a time — the free-tier rate limit),
/// a DI scope per item. One bad item is logged and left unsettled for the next startup sweep, never a dead loop.</summary>
internal sealed partial class InboxProcessingWorker(
    InboxBacklog queue, IServiceScopeFactory scopes, ILogger<InboxProcessingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var itemId in queue.DequeueAllAsync(stoppingToken))
        {
            await ProcessAsync(itemId, stoppingToken);
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "One bad inbox item must not tear down the worker loop the rest of the queue depends on; "
            + "the item stays unsettled and the next startup sweep retries it.")]
    private async Task ProcessAsync(Guid itemId, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<InboxProcessor>().ProcessAsync(itemId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogItemFailed(logger, ex, itemId);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Processing inbox item {ItemId} failed.")]
    private static partial void LogItemFailed(ILogger logger, Exception exception, Guid itemId);
}
