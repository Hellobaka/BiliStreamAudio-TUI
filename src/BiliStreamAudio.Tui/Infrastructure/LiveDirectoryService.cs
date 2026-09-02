using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Text.Json;
using BiliStreamAudio.Tui.Core;

namespace BiliStreamAudio.Tui.Infrastructure;

/// <summary>
/// Gets the followed-live list and live-user search results exposed by the web
/// client. Search requests are WBI-signed as required by the current API.
/// </summary>
public sealed partial class LiveDirectoryService(BiliHttp http) : ILiveDirectoryService
{
    private const string FollowingEndpoint = "xlive/web-ucenter/v1/xfetter/GetWebList?hit_ab=false";
    private const string NavEndpoint = "https://api.bilibili.com/x/web-interface/nav";
    private const string SearchEndpoint = "https://api.bilibili.com/x/web-interface/wbi/search/type?";

    public async Task<IReadOnlyList<LiveDirectoryEntry>> GetFollowedLiveAsync(
        CancellationToken cancellationToken)
    {
        using var json = await http
            .GetLiveJsonAsync(FollowingEndpoint, 0, cancellationToken)
            .ConfigureAwait(false);
        BiliJson.EnsureOk(json);

        var data = json.RootElement.GetProperty("data");
        var rooms = data.TryGetProperty("rooms", out var roomList)
            ? roomList
            : data.GetProperty("list");
        return rooms.EnumerateArray()
            .Select(room => new LiveDirectoryEntry(
                room.Int64("room_id") is var roomId && roomId > 0 ? roomId : room.Int64("roomid"),
                room.Int64("uid"),
                room.String("uname"),
                room.String("title"),
                room.Int64("live_status") == 1,
                UnixTime(room.Int64("liveTime"))))
            .Where(room => room.IsLive && room.RoomId > 0)
            .ToArray();
    }

    public async Task<IReadOnlyList<LiveDirectoryEntry>> SearchUsersAsync(
        string keyword,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return [];
        }

        var (imgKey, subKey) = await GetWbiKeysAsync(cancellationToken).ConfigureAwait(false);
        var query = WbiSigner.Sign(
            [
                new KeyValuePair<string, string>("keyword", keyword.Trim()),
                new KeyValuePair<string, string>("search_type", "live_user")
            ],
            imgKey,
            subKey,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        using var json = await http
            .GetJsonAsync(SearchEndpoint + query, cancellationToken)
            .ConfigureAwait(false);
        BiliJson.EnsureOk(json);

        if (!json.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("result", out var results)
            || results.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return results.EnumerateArray()
            .Where(item => item.String("type").Equals("live_user", StringComparison.Ordinal))
            .Select(item => new LiveDirectoryEntry(
                item.Int64("roomid"),
                item.Int64("uid"),
                StripSearchMarkup(item.String("uname")),
                string.Empty,
                item.Int64("live_status") == 1 || item.TryGetProperty("is_live", out var isLive) && isLive.ValueKind == JsonValueKind.True,
                ParseLiveTime(item.String("live_time"))))
            .ToArray();
    }

    private async Task<(string ImgKey, string SubKey)> GetWbiKeysAsync(CancellationToken cancellationToken)
    {
        using var json = await http.GetJsonAsync(NavEndpoint, cancellationToken).ConfigureAwait(false);
        BiliJson.EnsureOk(json);
        var image = json.RootElement.GetProperty("data").GetProperty("wbi_img");
        return (FileNameWithoutExtension(image.String("img_url")), FileNameWithoutExtension(image.String("sub_url")));
    }

    private static DateTimeOffset? UnixTime(long value) => value > 0
        ? DateTimeOffset.FromUnixTimeSeconds(value).ToLocalTime()
        : null;

    private static DateTimeOffset? ParseLiveTime(string value)
    {
        if (DateTime.TryParseExact(
                value,
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return new DateTimeOffset(parsed, TimeSpan.FromHours(8));
        }

        return null;
    }

    private static string FileNameWithoutExtension(string url) => Path.GetFileNameWithoutExtension(new Uri(url).AbsolutePath);

    private static string StripSearchMarkup(string value) => SearchMarkup().Replace(WebUtility.HtmlDecode(value), string.Empty);

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex SearchMarkup();
}
