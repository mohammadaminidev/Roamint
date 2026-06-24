using Roamint.Web.Capture;
using Roamint.Web.Processing;

namespace Roamint.Web.Tests;

public sealed class ReceiptJobsTests
{
    private static CaptureSeed Seed(Guid id) => new(
        CaptureSource.Receipt, id, "AMD", new DateOnly(2026, 6, 24),
        [new CaptureLineSeed("բան", "Thing", 450m, null, null)]);

    [Fact]
    public async Task Enqueue_tracks_a_queued_job_and_makes_the_id_dequeuable()
    {
        var jobs = new ReceiptJobs();
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();

        await jobs.EnqueueAsync(a, TestContext.Current.CancellationToken);
        await jobs.EnqueueAsync(b, TestContext.Current.CancellationToken);

        jobs.Snapshot().Select(j => j.Stage).ShouldAllBe(s => s == ReceiptJobStage.Queued);

        var drained = new List<Guid>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await foreach (var id in jobs.DequeueAllAsync(cts.Token))
        {
            drained.Add(id);
            if (drained.Count == 2)
            {
                break;
            }
        }

        drained.ShouldBe([a, b]); // FIFO
    }

    [Fact]
    public async Task Snapshot_is_newest_first()
    {
        var jobs = new ReceiptJobs();
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        await jobs.EnqueueAsync(first, TestContext.Current.CancellationToken);
        await jobs.EnqueueAsync(second, TestContext.Current.CancellationToken);

        jobs.Snapshot().Select(j => j.ReceiptId).ShouldBe([second, first]);
    }

    [Fact]
    public async Task Duplicate_enqueue_is_ignored()
    {
        var jobs = new ReceiptJobs();
        var id = Guid.CreateVersion7();

        await jobs.EnqueueAsync(id, TestContext.Current.CancellationToken);
        await jobs.EnqueueAsync(id, TestContext.Current.CancellationToken);

        jobs.Snapshot().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task MarkReady_carries_the_seed_and_summary()
    {
        var jobs = new ReceiptJobs();
        var id = Guid.CreateVersion7();
        await jobs.EnqueueAsync(id, TestContext.Current.CancellationToken);
        jobs.Advance(id, ReceiptJobStage.Reading);

        var seed = Seed(id);
        jobs.MarkReady(id, seed, "Yerevan City", 450m, 1);

        var job = jobs.Snapshot().ShouldHaveSingleItem();
        job.Stage.ShouldBe(ReceiptJobStage.Ready);
        job.Seed.ShouldBe(seed);
        job.Merchant.ShouldBe("Yerevan City");
        job.Total.ShouldBe(450m);
        job.ItemCount.ShouldBe(1);
        job.CurrencyCode.ShouldBe("AMD"); // pulled from the seed
    }

    [Fact]
    public async Task MarkFailed_records_the_reason()
    {
        var jobs = new ReceiptJobs();
        var id = Guid.CreateVersion7();
        await jobs.EnqueueAsync(id, TestContext.Current.CancellationToken);

        jobs.MarkFailed(id, "boom");

        var job = jobs.Snapshot().ShouldHaveSingleItem();
        job.Stage.ShouldBe(ReceiptJobStage.Failed);
        job.Error.ShouldBe("boom");
    }

    [Fact]
    public async Task Dismiss_removes_a_job()
    {
        var jobs = new ReceiptJobs();
        var id = Guid.CreateVersion7();
        await jobs.EnqueueAsync(id, TestContext.Current.CancellationToken);

        jobs.Dismiss(id);

        jobs.Snapshot().ShouldBeEmpty();
    }

    [Fact]
    public async Task Changed_fires_on_mutation()
    {
        var jobs = new ReceiptJobs();
        var count = 0;
        jobs.Changed += () => Interlocked.Increment(ref count);
        var id = Guid.CreateVersion7();

        await jobs.EnqueueAsync(id, TestContext.Current.CancellationToken);
        jobs.Advance(id, ReceiptJobStage.Reading);

        count.ShouldBeGreaterThanOrEqualTo(2);
    }
}
