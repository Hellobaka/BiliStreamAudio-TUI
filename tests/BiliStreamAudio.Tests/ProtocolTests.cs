using System.IO.Compression;
using System.Text;
using BiliStreamAudio.Tui.Core;
using BiliStreamAudio.Tui.Infrastructure;

namespace BiliStreamAudio.Tests;

public sealed class ProtocolTests
{
    [Fact]
    public void Stream_url_joins_host_path_and_extra_once()
    {
        var url = StreamUrl.Build("https://cdn.example.test/", "/live/audio.m3u8", "?token=abc");
        Assert.Equal("https://cdn.example.test/live/audio.m3u8?token=abc", url?.ToString());
    }

    [Fact]
    public void Stream_url_deduplicates_query_separators_from_api_parts()
    {
        var url = StreamUrl.Build(
            "https://cdn.example.test/",
            "/live/audio.m3u8?",
            "?expires=123&sign=abc");

        Assert.Equal(
            "https://cdn.example.test/live/audio.m3u8?expires=123&sign=abc",
            url?.ToString());
    }

    [Fact]
    public void Wbi_signature_is_canonical_and_filters_reserved_characters()
    {
        KeyValuePair<string, string>[] parameters =
        [
            new("b", "hello!'()*"),
            new("a", "1")
        ];
        var signature = WbiSigner.Sign(
            parameters,
            "abcdefghijklmnopqrstuvwxyz123456",
            "ZYXWVUTSRQPONMLKJIHGFEDCBA654321",
            1_700_000_000);

        Assert.StartsWith("a=1&b=hello&wts=1700000000&w_rid=", signature, StringComparison.Ordinal);
        Assert.Equal(32, signature.Split("w_rid=")[1].Length);
    }

    [Fact]
    public void Vlc_media_options_include_http_playback_context()
    {
        var stream = new StreamDescriptor(
            new Uri("https://cdn.example.test/live.m3u8"),
            "http_hls",
            "fmp4",
            80,
            true,
            26044264);
        var options = VlcRequestOptions.Create(stream);

        Assert.Contains(options, option => option.StartsWith(":http-user-agent=Mozilla/5.0", StringComparison.Ordinal));
        Assert.Contains(":http-referrer=https://live.bilibili.com/26044264", options);
        Assert.Contains(":http-forward-cookies", options);
    }

    [Fact]
    public void Vlc_log_sanitizer_removes_cookies_and_signed_queries()
    {
        var cookieLog = VlcLogSanitizer.Sanitize(
            "Sending Cookie SESSDATA=secret; bili_jct=csrf-secret");
        var urlLog = VlcLogSanitizer.Sanitize(
            "GET https://cdn.example.test/live.m3u8?token=secret&expires=123 HTTP/1.1");
        var relativeRequestLog = VlcLogSanitizer.Sanitize(
            "GET /live/audio.m3u8??token=secret&expires=123 HTTP/1.1");

        Assert.DoesNotContain("secret", cookieLog, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", urlLog, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", relativeRequestLog, StringComparison.Ordinal);
        Assert.Contains("Cookie: <redacted>", cookieLog, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://cdn.example.test/live.m3u8?<redacted>", urlLog, StringComparison.Ordinal);
        Assert.Contains("GET /live/audio.m3u8?<redacted>", relativeRequestLog, StringComparison.Ordinal);
    }

    [Fact]
    public void Parses_zlib_wrapped_danmaku_frame()
    {
        var json = Encoding.UTF8.GetBytes("{\"cmd\":\"DANMU_MSG\",\"info\":[[],\"你好\",[1,\"Alice\"]]}");
        var inner = DanmakuProtocol.Frame(5, json);
        using var target = new MemoryStream();
        using (var zlib = new ZLibStream(target, CompressionLevel.SmallestSize, true))
        {
            zlib.Write(inner);
        }

        var events = DanmakuProtocol.Parse(DanmakuProtocol.Frame(5, target.ToArray(), 2));
        var item = Assert.Single(events);
        Assert.Equal("Alice", item.UserName);
        Assert.Equal("你好", item.Message);
    }

    [Fact]
    public void Parses_brotli_wrapped_danmaku_frame()
    {
        var json = Encoding.UTF8.GetBytes("{\"cmd\":\"DANMU_MSG:4:0:2:2:2:0\",\"info\":[[],\"hi\",[1,\"Bob\"]]}");
        var inner = DanmakuProtocol.Frame(5, json);
        using var target = new MemoryStream();
        using (var brotli = new BrotliStream(target, CompressionLevel.SmallestSize, true))
        {
            brotli.Write(inner);
        }

        var events = DanmakuProtocol.Parse(DanmakuProtocol.Frame(5, target.ToArray(), 3));
        Assert.Equal("Bob", Assert.Single(events).UserName);
    }
}
