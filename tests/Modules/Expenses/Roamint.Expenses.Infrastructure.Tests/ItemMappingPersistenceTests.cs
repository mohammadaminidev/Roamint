using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Roamint.Expenses.Application;
using Roamint.Expenses.Application.Queries;
using Roamint.Expenses.Domain;
using Roamint.Expenses.Infrastructure;

namespace Roamint.Expenses.Infrastructure.Tests;

[Collection(nameof(PostgresCollection))]
public sealed class ItemMappingPersistenceTests(PostgresFixture fixture)
{
    private const int Dim = 1024;
    private static readonly DateTimeOffset Now = new(2026, 6, 20, 10, 0, 0, TimeSpan.Zero);

    // A near-one-hot vector — orthogonal axes are cosine-distance 1.0 apart, giving deterministic NN tests.
    private static float[] Axis(int index, float primary = 1f, int? secondIndex = null, float secondary = 0f)
    {
        var vector = new float[Dim];
        vector[index] = primary;
        if (secondIndex is int j)
        {
            vector[j] = secondary;
        }

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
            var found = await new ItemMappingRepository(db).FindBySourceTextAsync(source, ct);

            found.ShouldNotBeNull();
            found!.CanonicalDescription.ShouldBe("پنیر چاناخ");
            found.Unit.ShouldBe("g");
            found.CategoryId.ShouldBe(cat);
        }
    }

    [Fact]
    public async Task MatchItem_returns_nearest_within_threshold_and_null_beyond_it()
    {
        var clock = new FakeTimeProvider(Now);
        var ct = TestContext.Current.CancellationToken;
        var cheeseCat = CategoryId.New(clock);
        var limeCat = CategoryId.New(clock);

        await using var db = fixture.CreateContext(clock);

        // Isolate: the NN search scans the whole table, so start from empty (collection runs sequentially).
        await db.ItemMappings.ExecuteDeleteAsync(ct);

        var repo = new ItemMappingRepository(db);
        await repo.AddAsync(
            ItemMapping.Create(ItemMappingId.New(clock), $"cheese-{Guid.CreateVersion7():N}", "پنیر", "g", cheeseCat, clock),
            Axis(0), ct);
        await repo.AddAsync(
            ItemMapping.Create(ItemMappingId.New(clock), $"lime-{Guid.CreateVersion7():N}", "لیمو", null, limeCat, clock),
            Axis(1), ct);
        await db.SaveChangesAsync(ct);

        var embedder = new MapEmbedder(new Dictionary<string, float[]>
        {
            ["near cheese"] = Axis(0, 0.98f, secondIndex: 1, secondary: 0.02f), // ~cheese
            ["unrelated"] = Axis(2), // orthogonal to both → distance 1.0
        });
        var handler = new MatchItemHandler(db, embedder);

        var hit = await handler.HandleAsync(new MatchItem("near cheese"), ct);
        hit.ShouldNotBeNull();
        hit!.CanonicalDescription.ShouldBe("پنیر");
        hit.Unit.ShouldBe("g");
        hit.CategoryId.ShouldBe(cheeseCat);

        var miss = await handler.HandleAsync(new MatchItem("unrelated"), ct);
        miss.ShouldBeNull();
    }

    private sealed class MapEmbedder(Dictionary<string, float[]> map) : IItemEmbedder
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken ct) => Task.FromResult(map[text]);
    }
}
