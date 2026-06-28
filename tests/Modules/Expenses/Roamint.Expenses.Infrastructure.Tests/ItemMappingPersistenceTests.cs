using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Roamint.Expenses.Application.Queries;
using Roamint.Expenses.Domain;
using Roamint.Expenses.Infrastructure;

namespace Roamint.Expenses.Infrastructure.Tests;

[Collection(nameof(PostgresCollection))]
public sealed class ItemMappingPersistenceTests(PostgresFixture fixture)
{
    private const int Dim = 1024;
    private static readonly DateTimeOffset Now = new(2026, 6, 20, 10, 0, 0, TimeSpan.Zero);

    // The embedding is dormant now (kept for Insights) but still a NOT-NULL write input; any vector will do.
    private static float[] Axis(int index)
    {
        var vector = new float[Dim];
        vector[index] = 1f;
        return vector;
    }

    [Fact]
    public async Task Round_trips_a_mapping_found_by_source_text()
    {
        var clock = new FakeTimeProvider(Now);
        var ct = TestContext.Current.CancellationToken;
        var cat = CategoryId.New(clock);
        var source = $"Պանիր Չանախ {Guid.CreateVersion7():N}";

        await using (var db = fixture.CreateContext(clock))
        {
            var repo = new ItemMappingRepository(db);
            var mapping = ItemMapping.Create(ItemMappingId.New(clock), source, "پنیر چاناخ", "g", cat, clock);
            await repo.AddAsync(mapping, Axis(0), ct);
            await db.SaveChangesAsync(ct);
        }

        await using (var db = fixture.CreateContext(clock))
        {
            // The repo keys on the normalised text — the same transform the write side stored.
            var found = await new ItemMappingRepository(db).FindBySourceTextAsync(ItemMapping.Normalize(source), ct);

            found.ShouldNotBeNull();
            found!.CanonicalDescription.ShouldBe("پنیر چاناخ");
            found.Unit.ShouldBe("g");
            found.CategoryId.ShouldBe(cat);
        }
    }

    [Fact]
    public async Task MatchItem_returns_the_exact_mapping_and_null_for_unseen_text()
    {
        var clock = new FakeTimeProvider(Now);
        var ct = TestContext.Current.CancellationToken;
        var cheeseCat = CategoryId.New(clock);
        var limeCat = CategoryId.New(clock);

        await using var db = fixture.CreateContext(clock);

        // Isolate: the lookup scans the whole table, so start from empty (collection runs sequentially).
        await db.ItemMappings.ExecuteDeleteAsync(ct);

        var cheeseSource = $"Պանիր {Guid.CreateVersion7():N}";
        var limeSource = $"Կիտրոն {Guid.CreateVersion7():N}";

        var repo = new ItemMappingRepository(db);
        await repo.AddAsync(
            ItemMapping.Create(ItemMappingId.New(clock), cheeseSource, "پنیر", "g", cheeseCat, clock), Axis(0), ct);
        await repo.AddAsync(
            ItemMapping.Create(ItemMappingId.New(clock), limeSource, "لیمو", null, limeCat, clock), Axis(1), ct);
        await db.SaveChangesAsync(ct);

        var handler = new MatchItemHandler(db);

        // Exact text resolves to its own mapping — never a different one (the bug 0021 fixes).
        var lime = await handler.HandleAsync(new MatchItem(limeSource), ct);
        lime.ShouldNotBeNull();
        lime!.CanonicalDescription.ShouldBe("لیمو");
        lime.Unit.ShouldBeNull();
        lime.CategoryId.ShouldBe(limeCat);

        // Case / surrounding-whitespace noise still hits via the shared normalisation.
        var noisy = await handler.HandleAsync(new MatchItem($"  {cheeseSource.ToUpperInvariant()}  "), ct);
        noisy.ShouldNotBeNull();
        noisy!.CanonicalDescription.ShouldBe("پنیر");
        noisy.Unit.ShouldBe("g");
        noisy.CategoryId.ShouldBe(cheeseCat);

        // Never-seen text is a miss, not a nearest-neighbour guess.
        var miss = await handler.HandleAsync(new MatchItem($"Միս {Guid.CreateVersion7():N}"), ct);
        miss.ShouldBeNull();
    }
}
