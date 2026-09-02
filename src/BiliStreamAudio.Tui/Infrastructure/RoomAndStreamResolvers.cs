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
                    var quality = codec.TryGetProperty("current_qn", out var qn) ? qn.GetInt32() : 0;
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
                                onlyAudio,
                                room.RoomId));
                        }
                    }
                }
            }
        }
        return result
            .OrderByDescending(stream => stream.Protocol.Equals(
                "http_hls",
                StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(stream => stream.Quality)
            .ToArray();
    }
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
