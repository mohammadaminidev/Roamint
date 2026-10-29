using System.Net.Http.Headers;

namespace Roamint.Web.Processing;

/// <summary>Where the relay lives and the bearer key for its pull/ack endpoints (0024), from <c>InboxRelay</c>
/// in user-secrets. Either missing → the puller stays idle.</summary>
public sealed class InboxRelayOptions
{
    public string? BaseUrl { get; set; }

    public string? ApiKey { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>One stored relay row: <see cref="Id"/> is the relay's row id (what ack takes); the rest is the
/// source-agnostic envelope.</summary>
public sealed record RelayItem(long Id, string Source, string ExternalId, string ReceivedAt, string Payload);

/// <summary>Typed client for the Cloudflare relay (<c>tools/inbox-relay</c>): pull the oldest stored envelopes,
/// then ack (delete) the ones Roamint has persisted.</summary>
public sealed class InboxRelayClient(HttpClient http)
{
    public const int MaxBatch = 100;

    public static void Configure(HttpClient http, InboxRelayOptions options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        http.BaseAddress = new Uri(options.BaseUrl!.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
    }

    public async Task<IReadOnlyList<RelayItem>> PullAsync(CancellationToken ct)
    {
        var page = await http.GetFromJsonAsync<RelayPage>($"inbox?limit={MaxBatch}", ct);
        return page?.Items ?? [];
    }

    public async Task AckAsync(IReadOnlyList<long> ids, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("inbox/ack", new { ids }, ct);
        response.EnsureSuccessStatusCode();
    }

    private sealed record RelayPage(IReadOnlyList<RelayItem> Items);
}
