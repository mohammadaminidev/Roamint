using Roamint.Receipts.Contracts;

namespace Roamint.Web.Processing;

/// <summary>
/// On boot (Development only, mirroring the migrate-on-startup services), re-enqueues any receipt left
/// <c>Uploaded</c> — orphaned by a crash mid-parse — so the worker finishes it. Cheap, and it doubles as the
/// crash-recovery path; a user-facing retry button is deferred (0019).
/// </summary>
internal sealed class ReceiptStartupSweep(
    ReceiptJobs jobs, IServiceScopeFactory scopes, IHostEnvironment environment) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredService<IReceiptsQueries>();

        foreach (var receiptId in await queries.ListPendingAsync(cancellationToken))
        {
            await jobs.EnqueueAsync(receiptId, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
