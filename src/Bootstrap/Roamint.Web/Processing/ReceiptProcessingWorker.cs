using System.Diagnostics.CodeAnalysis;
using Roamint.Expenses.Contracts;
using Roamint.Receipts.Contracts;
using Roamint.Web.Capture;

namespace Roamint.Web.Processing;

/// <summary>
/// Drains <see cref="ReceiptJobs"/> serially (one Gemini call at a time — respects the free-tier rate limit),
/// running the OCR-then-match orchestration the capture page used to do inline (0015/0016): parse the receipt,
/// resolve each line against the learned item vocabulary, and stash the fully-seeded <see cref="CaptureSeed"/>
/// on the job so "Review" is instant. A scope per job; a failed job surfaces as a <see cref="ReceiptJobStage"/>
/// Failed row, never a crashed loop.
/// </summary>
internal sealed class ReceiptProcessingWorker(ReceiptJobs jobs, IServiceScopeFactory scopes)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var receiptId in jobs.DequeueAllAsync(stoppingToken))
        {
            await ProcessAsync(receiptId, stoppingToken);
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "One bad receipt must degrade to a Failed job row, not tear down the worker loop that "
            + "the rest of the queue depends on.")]
    private async Task ProcessAsync(Guid receiptId, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var receipts = scope.ServiceProvider.GetRequiredService<IReceiptsApi>();
            var expenses = scope.ServiceProvider.GetRequiredService<IExpensesQueries>();

            jobs.Advance(receiptId, ReceiptJobStage.Reading);
            var result = await receipts.ParseAsync(receiptId, ct);
            if (!result.Parsed || result.Draft is null)
            {
                jobs.MarkFailed(receiptId, "Couldn't read this receipt.");
                return;
            }

            jobs.Advance(receiptId, ReceiptJobStage.Matching);

            // Resolve each line against the learned item vocabulary (0016): a match carries the remembered
            // canonical name + category + unit; a miss carries the first-time translation for review.
            var draft = result.Draft;
            var seedLines = new List<CaptureLineSeed>();
            var total = 0m;
            foreach (var line in draft.Lines)
            {
                Guid? categoryId = null;
                string? unit = null;
                var description = line.Description;

                if (!string.IsNullOrWhiteSpace(line.SourceText))
                {
                    var match = await expenses.MatchItemAsync(line.SourceText, ct);
                    if (match is not null)
                    {
                        description = match.CanonicalDescription;
                        categoryId = match.CategoryId;
                        unit = match.Unit;
                    }
                }

                total += line.Amount;
                seedLines.Add(new CaptureLineSeed(line.SourceText, description, line.Amount, categoryId, unit));
            }

            var seed = new CaptureSeed(
                CaptureSource.Receipt, receiptId, draft.CurrencyCode, draft.Date, seedLines);
            jobs.MarkReady(receiptId, seed, draft.Merchant, total, seedLines.Count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // host shutting down — let the loop unwind.
        }
        catch (Exception ex)
        {
            jobs.MarkFailed(receiptId, ex.Message);
        }
    }
}
