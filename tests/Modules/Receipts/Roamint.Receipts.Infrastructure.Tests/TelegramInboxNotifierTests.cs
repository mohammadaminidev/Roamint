using System.Net;
using Microsoft.Extensions.Time.Testing;
using Roamint.Receipts.Application;
using Roamint.Receipts.Domain;

namespace Roamint.Receipts.Infrastructure.Tests;

public sealed class TelegramInboxNotifierTests
{
    [Fact]
    public async Task A_real_shaped_token_with_a_colon_reaches_the_bot_api()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 29, 6, 0, 0, TimeSpan.Zero));
        var item = InboxItem.Receive(
            InboxItemId.New(clock), "telegram", "-5262458813:7", "777", "5 هزار تا برای شام", clock.GetUtcNow(), "{}", clock);
        using var handler = new CapturingHandler();
        using var notifier = new TelegramInboxNotifier("123456:ABC-def_ghi", handler);

        await notifier.ReactAsync(item, InboxReaction.NeedsReview, TestContext.Current.CancellationToken);

        handler.RequestUri.ShouldBe(new Uri("https://api.telegram.org/bot123456:ABC-def_ghi/setMessageReaction"));
        handler.Body.ShouldContain("\"chat_id\":-5262458813");
        handler.Body.ShouldContain("\"message_id\":7");
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
