using System.Net;
using System.Text;
using BiliStreamAudio.Tui.Core;
using BiliStreamAudio.Tui.Infrastructure;

namespace BiliStreamAudio.Tests;

public sealed class AccountInfoServiceTests
{
    [Fact]
    public async Task Populate_adds_user_name_and_server_user_id_to_session()
    {
        var handler = new AccountResponseHandler(
            "{\"code\":0,\"data\":{\"mid\":12345,\"uname\":\"测试用户\"}}");
        var service = new AccountInfoService(
            session => new BiliHttp(session, handler));
        var cookies = Enumerable.Range(0, 30)
            .ToDictionary(index => $"device_cookie_{index}", index => $"value_{index}");
        cookies["SESSDATA"] = "redacted";
        cookies["bili_jct"] = "csrf";
        var session = new AuthSession(
            cookies,
            "refresh-token",
            1,
            null);

        var populated = await service.PopulateAsync(session, CancellationToken.None);

        Assert.Equal("测试用户", populated.UserName);
        Assert.Equal(12345, populated.UserId);
        Assert.Equal("refresh-token", populated.RefreshToken);
        Assert.Equal(1, handler.Requests);
        Assert.StartsWith("Mozilla/5.0", handler.UserAgent);
        Assert.Contains("Chrome/", handler.UserAgent);
        Assert.Equal("https://www.bilibili.com/", handler.Referrer?.AbsoluteUri);
        Assert.Equal("https://www.bilibili.com", handler.Origin);
        Assert.Contains("device_cookie_29=value_29", handler.CookieHeader);
        Assert.Contains("SESSDATA=redacted", handler.CookieHeader);
    }

    [Fact]
    public async Task Http_client_reads_the_latest_session_for_every_request()
    {
        var handler = new AccountResponseHandler("{\"code\":0,\"data\":{}}");
        AuthSession? current = CreateSession("first-session");
        using var http = new BiliHttp(
            handler: handler,
            sessionProvider: () => current);

        using var firstResponse = await http.GetJsonAsync(
            "https://api.bilibili.com/test",
            CancellationToken.None);

        Assert.Contains("SESSDATA=first-session", handler.CookieHeader);

        current = CreateSession("second-session");
        using var secondResponse = await http.GetJsonAsync(
            "https://api.bilibili.com/test",
            CancellationToken.None);

        Assert.Contains("SESSDATA=second-session", handler.CookieHeader);
        Assert.DoesNotContain("SESSDATA=first-session", handler.CookieHeader);
    }

    private static AuthSession CreateSession(string sessionData)
    {
        return new AuthSession(
            new Dictionary<string, string>
            {
                ["SESSDATA"] = sessionData,
                ["bili_jct"] = "csrf"
            },
            "refresh-token",
            1,
            null);
    }

    private sealed class AccountResponseHandler(string json) : HttpMessageHandler
    {
        public int Requests
        {
            get;
            private set;
        }
        public string UserAgent
        {
            get;
            private set;
        } = string.Empty;
        public Uri? Referrer
        {
            get;
            private set;
        }
        public string? Origin
        {
            get;
            private set;
        }
        public string CookieHeader
        {
            get;
            private set;
        } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests++;
            UserAgent = request.Headers.UserAgent.ToString();
            Referrer = request.Headers.Referrer;
            Origin = request.Headers.TryGetValues("Origin", out var origins)
                ? origins.Single()
                : null;
            CookieHeader = string.Join(
                "; ",
                request.Headers.GetValues("Cookie"));
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }

        protected override void Dispose(bool disposing)
        {
            // The test owns and reuses the handler for its single request.
        }
    }
}
