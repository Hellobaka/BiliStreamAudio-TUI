using System.Net;
using System.Text.Json;
using BiliStreamAudio.Tui.Core;

namespace BiliStreamAudio.Tui.Infrastructure;

public sealed class BiliHttp : IDisposable
{
    private const string MainOrigin = "https://www.bilibili.com";

    internal const string DesktopBrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
        + "AppleWebKit/537.36 (KHTML, like Gecko) "
        + "Chrome/152.0.0.0 Safari/537.36 Edg/152.0.0.0";

    private readonly HttpClient _client;
    private readonly Func<string?> _cookieHeaderProvider;

    public BiliHttp(
        AuthSession? session = null,
        HttpMessageHandler? handler = null,
        bool useRawCookieHeader = true,
        Func<AuthSession?>? sessionProvider = null)
    {
        var cookies = new CookieContainer
        {
            PerDomainCapacity = 100
        };
        var sendRawCookies = useRawCookieHeader
            && (session is not null || sessionProvider is not null);
        if (session is not null && !sendRawCookies)
        {
            foreach (var item in session.Cookies)
            {
                cookies.Add(new Cookie(item.Key, item.Value, "/", ".bilibili.com"));
            }
        }

        var actualHandler = handler ?? new HttpClientHandler
        {
            CookieContainer = cookies,
            UseCookies = !sendRawCookies,
            AutomaticDecompression = DecompressionMethods.All
        };
        _client = new HttpClient(actualHandler)
        {
            BaseAddress = new Uri("https://api.live.bilibili.com/"),
            Timeout = TimeSpan.FromSeconds(20)
        };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd(DesktopBrowserUserAgent);
        _client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/plain, */*");
        _client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,en;q=0.8");
        _client.DefaultRequestHeaders.Referrer = new Uri(MainOrigin + "/");
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", MainOrigin);
        _cookieHeaderProvider = sendRawCookies
            ? () => CreateCookieHeader(sessionProvider?.Invoke() ?? session)
            : () => null;

        CookieContainer = cookies;
    }

    public CookieContainer CookieContainer
    {
        get;
    }
    public async Task<string> GetStringAsync(
        string url,
        CancellationToken cancellationToken,
        string? origin = null,
        string? referrer = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyRequestContext(request, origin, referrer);
        using var response = await _client
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<JsonDocument> GetJsonAsync(
        string url,
        CancellationToken cancellationToken,
        string? origin = null,
        string? referrer = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyRequestContext(request, origin, referrer);
        using var response = await _client
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
    }

    public async Task<JsonDocument> PostFormAsync(
        string url,
        IEnumerable<KeyValuePair<string, string>> form,
        CancellationToken cancellationToken,
        string? origin = null,
        string? referrer = null)
    {
        using var content = new FormUrlEncodedContent(form);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = content
        };
        ApplyRequestContext(request, origin, referrer);
        using var response = await _client
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
    }

    public async Task<JsonDocument> GetLiveJsonAsync(
        string url,
        long roomId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyLiveRequestContext(request, roomId);
        using var response = await _client
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
    }

    public async Task<JsonDocument> PostLiveFormAsync(
        string url,
        long roomId,
        IEnumerable<KeyValuePair<string, string>> form,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = content
        };
        ApplyLiveRequestContext(request, roomId);
        using var response = await _client
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
    }

    private void ApplyRequestContext(
        HttpRequestMessage request,
        string? origin,
        string? referrer)
    {
        var cookieHeader = _cookieHeaderProvider();
        if (!string.IsNullOrEmpty(cookieHeader))
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
        }

        var actualOrigin = string.IsNullOrWhiteSpace(origin) ? MainOrigin : origin;
        request.Headers.TryAddWithoutValidation("Origin", actualOrigin);

        var actualReferrer = string.IsNullOrWhiteSpace(referrer)
            ? MainOrigin + "/"
            : referrer;
        request.Headers.Referrer = new Uri(actualReferrer);
    }

    private void ApplyLiveRequestContext(HttpRequestMessage request, long roomId)
    {
        const string liveOrigin = "https://live.bilibili.com";
        ApplyRequestContext(request, liveOrigin, $"{liveOrigin}/{roomId}");

        request.Headers.Accept.Clear();
        request.Headers.Accept.ParseAdd("*/*");
        request.Headers.TryAddWithoutValidation("DNT", "1");
        request.Headers.TryAddWithoutValidation("Priority", "u=1, i");
        request.Headers.TryAddWithoutValidation(
            "sec-ch-ua",
            "\"Chromium\";v=\"152\", \"Not?A_Brand\";v=\"24\", \"Microsoft Edge\";v=\"152\"");
        request.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
        request.Headers.TryAddWithoutValidation("sec-ch-ua-platform", "\"Windows\"");
        request.Headers.TryAddWithoutValidation("sec-fetch-dest", "empty");
        request.Headers.TryAddWithoutValidation("sec-fetch-mode", "cors");
        request.Headers.TryAddWithoutValidation("sec-fetch-site", "same-site");
    }

    private static string? CreateCookieHeader(AuthSession? session)
    {
        if (session is null || session.Cookies.Count == 0)
        {
            return null;
        }

        return string.Join(
            "; ",
            session.Cookies.Select(cookie => $"{cookie.Key}={cookie.Value}"));
    }

    public void Dispose() => _client.Dispose();
}

internal static class BiliJson
{
    public static void EnsureOk(JsonDocument document)
    {
        if (document.RootElement.TryGetProperty("code", out var code) && code.GetInt32() != 0)
        {
            throw new InvalidOperationException($"哔哩哔哩接口请求失败（错误码：{code.GetInt32()}）。");
        }
    }

    public static string String(this JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value)
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    public static long Int64(this JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value)
            && value.TryGetInt64(out var result)
                ? result
                : 0;
    }
}
