using System.Text.Json;
using BiliStreamAudio.Tui.Core;

namespace BiliStreamAudio.Tui.Infrastructure;

public sealed class RoomResolver(BiliHttp http) : IRoomResolver
{
    public async Task<LiveRoom> ResolveAsync(RoomReference room, CancellationToken cancellationToken)
    {
        var roomInfoUrl = $"room/v1/Room/get_info?room_id={room.RequestedId}";
        using var roomInfoJson = await http
            .GetLiveJsonAsync(roomInfoUrl, room.RequestedId, cancellationToken)
            .ConfigureAwait(false);

        BiliJson.EnsureOk(roomInfoJson);

        var data = roomInfoJson.RootElement.GetProperty("data");
        var uid = data.Int64("uid");
        var anchorInfoUrl = $"live_user/v1/Master/info?uid={uid}";
        using var anchorInfoJson = await http
            .GetLiveJsonAsync(anchorInfoUrl, data.Int64("room_id"), cancellationToken)
            .ConfigureAwait(false);

        BiliJson.EnsureOk(anchorInfoJson);

        var anchor = anchorInfoJson.RootElement
            .GetProperty("data")
            .GetProperty("info")
            .String("uname");
        return new LiveRoom(
            data.Int64("room_id"),
            data.Int64("short_id"),
            uid,
            data.String("title"),
            anchor,
            data.Int64("live_status") == 1);
    }
}

public sealed class StreamResolver(BiliHttp http) : IStreamResolver
{
    private const string PlayInfo = "xlive/web-room/v2/index/getRoomPlayInfo";

    public async Task<IReadOnlyList<StreamDescriptor>> ResolveAudioAsync(
        LiveRoom room,
        bool allowVideoFallback,
        CancellationToken cancellationToken)
    {
        var audio = await ResolveAsync(room, true, cancellationToken).ConfigureAwait(false);
        if (audio.Count > 0 || !allowVideoFallback)
        {
            return audio;
        }

        return await ResolveAsync(room, false, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<StreamDescriptor>> ResolveAsync(
        LiveRoom room,
        bool onlyAudio,
        CancellationToken cancellationToken)
    {
        var audioFlag = onlyAudio ? 1 : 0;
        var query = $"{PlayInfo}?room_id={room.RoomId}"
            + "&protocol=0,1&format=0,1,2&codec=0,1&qn=80&platform=web&ptype=8"
            + $"&only_audio={audioFlag}";
        using var json = await http
            .GetLiveJsonAsync(query, room.RoomId, cancellationToken)
            .ConfigureAwait(false);

        BiliJson.EnsureOk(json);

        var streams = json.RootElement
            .GetProperty("data")
            .GetProperty("playurl_info")
            .GetProperty("playurl")
            .GetProperty("stream");
        var result = new List<StreamDescriptor>();

        foreach (var stream in streams.EnumerateArray())
        {
            var protocol = stream.String("protocol_name");
            foreach (var format in stream.GetProperty("format").EnumerateArray())
            {
                var formatName = format.String("format_name");
                foreach (var codec in format.GetProperty("codec").EnumerateArray())
                {
                    var codecName = codec.String("codec_name");
                    var quality = codec.TryGetProperty("current_qn", out var qn) ? qn.GetInt32() : 0;
                    var bitrateKbps = StreamProfile.GetBitrateKbps(codec);
                    foreach (var urlInfo in codec.GetProperty("url_info").EnumerateArray())
                    {
                        var url = StreamUrl.Build(
                            urlInfo.String("host"),
                            codec.String("base_url"),
                            urlInfo.String("extra"));
                        if (url is not null)
                        {
                            result.Add(new StreamDescriptor(
                                url,
                                protocol,
                                formatName,
                                quality,
                                StreamProfile.IsAudioOnly(onlyAudio, codecName, url),
                                room.RoomId,
                                codecName,
                                bitrateKbps));
                        }
                    }
                }
            }
        }
        // 实测中码率（画质）对启动延迟的影响远大于协议/格式。因此优先选最低画质，
        // 同画质下再按低延迟顺序（FLV → fMP4 → TS）选择。
        return (onlyAudio
                ? result.Where(stream => stream.IsAudioOnly)
                : result)
            .OrderBy(stream => stream.Quality)
            .ThenBy(StreamProfile.GetLatencyRank)
            .ThenBy(stream => StreamProfile.GetCodecRank(stream.Codec))
            .ToArray();
    }
}

internal static class StreamProfile
{
    public static bool IsAudioOnly(bool requestedAudioOnly, string codec, Uri url)
    {
        if (!requestedAudioOnly || IsVideoCodec(codec))
        {
            return false;
        }

        return !url.Query.TrimStart('?').Split('&').Any(parameter =>
            string.Equals(parameter, "media_type=0", StringComparison.Ordinal));
    }

    public static int GetLatencyRank(StreamDescriptor stream)
    {
        if (stream.Protocol.Equals("http_stream", StringComparison.OrdinalIgnoreCase)
            && stream.Format.Equals("flv", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (stream.Protocol.Equals("http_hls", StringComparison.OrdinalIgnoreCase)
            && stream.Format.Equals("fmp4", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (stream.Protocol.Equals("http_hls", StringComparison.OrdinalIgnoreCase)
            && stream.Format.Equals("ts", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 3;
    }

    public static int GetCodecRank(string codec) => codec.Equals(
        "avc",
        StringComparison.OrdinalIgnoreCase) ? 0 : 1;

    /// <summary>
    /// 读取接口返回的实时平均码率（media_info.realtime_avg_bw，单位 字节/秒），
    /// 换算为 kbps 用于日志展示。
    /// </summary>
    public static int? GetBitrateKbps(JsonElement codec)
    {
        if (codec.TryGetProperty("media_info", out var mediaInfo)
            && mediaInfo.TryGetProperty("realtime_avg_bw", out var bandwidth)
            && bandwidth.ValueKind == JsonValueKind.Number
            && bandwidth.GetInt64() > 0)
        {
            // 字节/秒 → 比特/秒 → kbps
            return (int)(bandwidth.GetInt64() * 8 / 1000);
        }

        return null;
    }

    private static bool IsVideoCodec(string codec) => codec.Equals(
        "avc",
        StringComparison.OrdinalIgnoreCase)
        || codec.Equals("h264", StringComparison.OrdinalIgnoreCase)
        || codec.Equals("hevc", StringComparison.OrdinalIgnoreCase)
        || codec.Equals("h265", StringComparison.OrdinalIgnoreCase)
        || codec.Equals("av1", StringComparison.OrdinalIgnoreCase);
}

public static class StreamUrl
{
    public static Uri? Build(string host, string baseUrl, string extra)
    {
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        var raw = host.TrimEnd('/') + "/" + baseUrl.TrimStart('/');
        if (!string.IsNullOrEmpty(extra))
        {
            var query = extra.TrimStart('?', '&');
            if (!string.IsNullOrEmpty(query))
            {
                if (!raw.Contains('?'))
                {
                    raw += "?" + query;
                }
                else if (raw.EndsWith('?') || raw.EndsWith('&'))
                {
                    raw += query;
                }
                else
                {
                    raw += "&" + query;
                }
            }
        }

        var valid = Uri.TryCreate(raw, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https";
        return valid ? uri : null;
    }
}

public static class WbiSigner
{
    private static readonly int[] MixinKeyEncTab =
    [
        46, 47, 18, 2, 53, 8, 23, 32, 15, 50, 10, 31, 58, 3, 45, 35,
        27, 43, 5, 49, 33, 9, 42, 19, 29, 28, 14, 39, 12, 38, 41, 13,
        37, 48, 7, 16, 24, 55, 40, 61, 26, 17, 0, 1, 60, 51, 30, 4,
        22, 25, 54, 21, 56, 59, 6, 63, 57, 62, 11, 36, 20, 34, 44, 52
    ];
    private static readonly char[] Filter = "!'()*".ToCharArray();

    public static string MixinKey(string imgKey, string subKey)
    {
        var source = imgKey + subKey;
        return new string(MixinKeyEncTab.Select(index => source[index]).Take(32).ToArray());
    }

    public static string Sign(
        IEnumerable<KeyValuePair<string, string>> parameters,
        string imgKey,
        string subKey,
        long unixSeconds)
    {
        var timestamp = unixSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var values = parameters
            .Append(new KeyValuePair<string, string>("wts", timestamp))
            .OrderBy(parameter => parameter.Key, StringComparer.Ordinal)
            .Select(parameter =>
            {
                var filteredValue = new string(parameter.Value
                    .Where(character => !Filter.Contains(character))
                    .ToArray());
                return $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(filteredValue)}";
            });
        var query = string.Join("&", values);
        var signatureSource = System.Text.Encoding.UTF8.GetBytes(query + MixinKey(imgKey, subKey));
        var wRid = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(signatureSource))
            .ToLowerInvariant();
        return query + "&w_rid=" + wRid;
    }
}
