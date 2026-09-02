using System.Net;
using System.Text;
using BiliStreamAudio.Tui.Core;
using BiliStreamAudio.Tui.Infrastructure;

namespace BiliStreamAudio.Tests;

public sealed class SenderAndRefreshTests
{
    [Fact]
    public void Correspond_path_is_a_1024_bit_lowercase_hex_ciphertext()
    {
        var path = CookieRefreshService.CreateCorrespondPath(1_700_000_000_000);
        Assert.Matches("^[0-9a-f]{256}$", path);
    }

    [Fact]
    public async Task Sender_enforces_five_messages_per_thirty_seconds()
    {
        var handler = new JsonHandler("{\"code\":0,\"data\":{}}");
        var sender = new DanmakuSender(session => new BiliHttp(session, handler));
        var auth = Session();

        for (var i = 0; i < 5; i++)
        {
            await sender.SendAsync(1, "test", auth, CancellationToken.None);
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sender.SendAsync(1, "test", auth, CancellationToken.None));

        Assert.Contains("频繁", exception.Message);
        Assert.Equal(5, handler.Requests);
        Assert.Equal("https://live.bilibili.com", handler.Origin);
        Assert.Equal("https://live.bilibili.com/1", handler.Referrer?.AbsoluteUri);
        Assert.Contains("SESSDATA=redacted", handler.CookieHeader);
        Assert.Contains("Microsoft Edge", handler.ClientHints);
    }

    [Fact]
    public async Task Sender_accepts_thirty_characters_and_rejects_the_thirty_first()
    {
        var handler = new JsonHandler("{\"code\":0,\"data\":{}}");
        var sender = new DanmakuSender(session => new BiliHttp(session, handler));
        var auth = Session();

        await sender.SendAsync(1, new string('弹', 30), auth, CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentException>(
            () => sender.SendAsync(1, new string('弹', 31), auth, CancellationToken.None));

        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task Refresh_check_without_rotation_updates_daily_marker()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BiliStreamAudio.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new AuthStorage(directory);
            var refresh = new CookieRefreshService(
                storage,
                session => new BiliHttp(
                    session,
                    new JsonHandler("{\"code\":0,\"data\":{\"refresh\":false}}")));
            var result = await refresh.RefreshIfNeededAsync(Session(), CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal(DateOnly.FromDateTime(DateTime.Now), result.Session?.LastRefreshCheck);
            Assert.NotNull(await storage.LoadAsync(CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private static AuthSession Session()
    {
        var cookies = new Dictionary<string, string>
        {
            ["SESSDATA"] = "redacted",
            ["bili_jct"] = "csrf"
        };

        return new AuthSession(cookies, "refresh", 1, null);
    }

    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        public int Requests
        {
            get; private set;
        }
        public string? Origin
        {
            get; private set;
        }
        public Uri? Referrer
        {
            get; private set;
        }
        public string CookieHeader
        {
            get; private set;
        } = string.Empty;
        public string ClientHints
        {
            get; private set;
        } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests++;
            Origin = request.Headers.TryGetValues("Origin", out var origins)
                ? origins.Single()
                : null;
            Referrer = request.Headers.Referrer;
            CookieHeader = request.Headers.TryGetValues("Cookie", out var cookies)
                ? string.Join("; ", cookies)
                : string.Empty;
            ClientHints = request.Headers.TryGetValues("sec-ch-ua", out var clientHints)
                ? string.Join(", ", clientHints)
                : string.Empty;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }

        protected override void Dispose(bool disposing)
        {
            // Tests intentionally reuse the same handler across several short-lived HttpClient instances.
        }
    }
}
