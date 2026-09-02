using System.Net;
using System.Text;
using BiliStreamAudio.Tui.Core;
using BiliStreamAudio.Tui.Infrastructure;

namespace BiliStreamAudio.Tests;

public sealed class StreamResolverTests
{
    [Fact]
    public async Task Audio_request_excludes_an_avc_video_response()
    {
        using var http = new BiliHttp(handler: new StreamResponseHandler(VideoStreams), useRawCookieHeader: false);
        var resolver = new StreamResolver(http);

        var streams = await resolver.ResolveAudioAsync(Room, false, CancellationToken.None);

        Assert.Empty(streams);
    }

    [Fact]
    public async Task Video_fallback_prefers_flv_before_hls_and_reports_actual_stream_type()
    {
        using var http = new BiliHttp(handler: new StreamResponseHandler(VideoStreams), useRawCookieHeader: false);
        var resolver = new StreamResolver(http);

        var streams = await resolver.ResolveAudioAsync(Room, true, CancellationToken.None);

        Assert.NotEmpty(streams);
        var stream = streams[0];
        Assert.Equal("http_stream", stream.Protocol);
        Assert.Equal("flv", stream.Format);
        Assert.False(stream.IsAudioOnly);
        Assert.Equal("avc", stream.Codec);
    }

    [Fact]
    public async Task Lower_quality_wins_over_flv_format()
    {
        using var http = new BiliHttp(handler: new StreamResponseHandler(QualityBeatsFormat), useRawCookieHeader: false);
        var resolver = new StreamResolver(http);

        var streams = await resolver.ResolveAudioAsync(Room, true, CancellationToken.None);

        // 2 Mbps 的 TS 流实测远快于 8 Mbps 的 FLV 流，因此最低画质优先于 FLV 格式。
        Assert.Equal(2, streams.Count);
        Assert.Equal("http_hls", streams[0].Protocol);
        Assert.Equal("ts", streams[0].Format);
        Assert.Equal(80, streams[0].Quality);
        Assert.Equal("http_stream", streams[1].Protocol);
        Assert.Equal("flv", streams[1].Format);
        Assert.Equal(250, streams[1].Quality);
    }

    [Fact]
    public async Task Same_protocol_prefers_the_lowest_quality()
    {
        using var http = new BiliHttp(handler: new StreamResponseHandler(QualityOverCodec), useRawCookieHeader: false);
        var resolver = new StreamResolver(http);

        var streams = await resolver.ResolveAudioAsync(Room, true, CancellationToken.None);

        Assert.Equal(2, streams.Count);
        Assert.Equal(80, streams[0].Quality);
        Assert.Equal("hevc", streams[0].Codec);
        Assert.Equal(250, streams[1].Quality);
        Assert.Equal("avc", streams[1].Codec);
    }

    [Fact]
    public async Task Resolver_reports_expected_bitrate_from_origin_bitrate()
    {
        using var http = new BiliHttp(handler: new StreamResponseHandler(BitrateStreams), useRawCookieHeader: false);
        var resolver = new StreamResolver(http);

        var stream = Assert.Single(await resolver.ResolveAudioAsync(Room, true, CancellationToken.None));

        Assert.Equal(2048, stream.BitrateKbps);
    }

    [Fact]
    public async Task Audio_request_keeps_a_non_video_audio_response()
    {
        using var http = new BiliHttp(handler: new StreamResponseHandler(AudioStreams), useRawCookieHeader: false);
        var resolver = new StreamResolver(http);

        var stream = Assert.Single(await resolver.ResolveAudioAsync(Room, false, CancellationToken.None));

        Assert.True(stream.IsAudioOnly);
        Assert.Equal("aac", stream.Codec);
    }

    private static readonly LiveRoom Room = new(42, 42, 1, "title", "anchor", true);

    private const string VideoStreams = """
        {"code":0,"data":{"playurl_info":{"playurl":{"stream":[
          {"protocol_name":"http_hls","format":[
            {"format_name":"ts","codec":[{"codec_name":"avc","current_qn":80,"base_url":"/live.ts.m3u8","url_info":[{"host":"https://cdn.example.test","extra":"?media_type=0"}]}]},
            {"format_name":"fmp4","codec":[{"codec_name":"avc","current_qn":80,"base_url":"/index.m3u8","url_info":[{"host":"https://cdn.example.test","extra":"?media_type=0"}]}]}
          ]},
          {"protocol_name":"http_stream","format":[
            {"format_name":"flv","codec":[{"codec_name":"avc","current_qn":80,"base_url":"/live.flv","url_info":[{"host":"https://cdn.example.test","extra":"?media_type=0"}]}]}
          ]}
        ]}}}}
        """;

    private const string BitrateStreams = """
        {"code":0,"data":{"playurl_info":{"playurl":{"stream":[
          {"protocol_name":"http_stream","format":[
            {"format_name":"flv","codec":[{"codec_name":"avc","current_qn":80,"media_info":{"realtime_avg_bw":256000},"base_url":"/live.flv","url_info":[{"host":"https://cdn.example.test","extra":"?media_type=0"}]}]}
          ]}
        ]}}}}
        """;

    private const string QualityBeatsFormat = """
        {"code":0,"data":{"playurl_info":{"playurl":{"stream":[
          {"protocol_name":"http_stream","format":[
            {"format_name":"flv","codec":[{"codec_name":"avc","current_qn":250,"base_url":"/high.flv","url_info":[{"host":"https://cdn.example.test","extra":"?media_type=0"}]}]}
          ]},
          {"protocol_name":"http_hls","format":[
            {"format_name":"ts","codec":[{"codec_name":"avc","current_qn":80,"base_url":"/low.m3u8","url_info":[{"host":"https://cdn.example.test","extra":"?media_type=0"}]}]}
          ]}
        ]}}}}
        """;

    private const string QualityOverCodec = """
        {"code":0,"data":{"playurl_info":{"playurl":{"stream":[
          {"protocol_name":"http_stream","format":[
            {"format_name":"flv","codec":[
              {"codec_name":"hevc","current_qn":80,"base_url":"/low.flv","url_info":[{"host":"https://cdn.example.test","extra":"?media_type=0"}]},
              {"codec_name":"avc","current_qn":250,"base_url":"/high.flv","url_info":[{"host":"https://cdn.example.test","extra":"?media_type=0"}]}
            ]}
          ]}
        ]}}}}
        """;

    private const string AudioStreams = """
        {"code":0,"data":{"playurl_info":{"playurl":{"stream":[
          {"protocol_name":"http_hls","format":[
            {"format_name":"ts","codec":[{"codec_name":"aac","current_qn":80,"base_url":"/audio.m3u8","url_info":[{"host":"https://cdn.example.test","extra":"?media_type=1"}]}]}
          ]}
        ]}}}}
        """;

    private sealed class StreamResponseHandler(string response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response, Encoding.UTF8, "application/json")
        });
    }
}
