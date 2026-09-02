using System.Net;
using System.Text;
using BiliStreamAudio.Tui.Infrastructure;

namespace BiliStreamAudio.Tests;

public sealed class LiveDirectoryServiceTests
{
    [Fact]
    public async Task Followed_live_uses_live_endpoint_and_filters_offline_entries()
    {
        var handler = new DirectoryResponseHandler();
        using var http = new BiliHttp(handler: handler, useRawCookieHeader: false);
        var service = new LiveDirectoryService(http);

        var entries = await service.GetFollowedLiveAsync(CancellationToken.None);

        var entry = Assert.Single(entries);
        Assert.Equal(100, entry.RoomId);
        Assert.Equal("直播主播", entry.Anchor);
        Assert.True(entry.IsLive);
        Assert.Equal("/xlive/web-ucenter/v1/xfetter/GetWebList", handler.RequestUris.Single().AbsolutePath);
        Assert.Equal("hit_ab=false", handler.RequestUris.Single().Query.TrimStart('?'));
    }

    [Fact]
    public async Task User_search_signs_request_and_maps_live_and_offline_users()
    {
        var handler = new DirectoryResponseHandler();
        using var http = new BiliHttp(handler: handler, useRawCookieHeader: false);
        var service = new LiveDirectoryService(http);

        var entries = await service.SearchUsersAsync("主播", CancellationToken.None);

        Assert.Equal(2, entries.Count);
        Assert.Equal("主播", entries[0].Anchor);
        Assert.True(entries[0].IsLive);
        Assert.Equal("离线主播", entries[1].Anchor);
        Assert.False(entries[1].IsLive);
        var search = Assert.Single(
            handler.RequestUris,
            uri => uri.AbsolutePath.EndsWith("/search/type", StringComparison.Ordinal));
        Assert.Contains("search_type=live_user", search.Query);
        Assert.Contains("w_rid=", search.Query);
    }

    private sealed class DirectoryResponseHandler : HttpMessageHandler
    {
        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            var body = request.RequestUri!.AbsolutePath switch
            {
                "/xlive/web-ucenter/v1/xfetter/GetWebList" =>
                    "{\"code\":0,\"data\":{\"rooms\":[{\"room_id\":100,\"uid\":1,\"uname\":\"直播主播\",\"title\":\"直播标题\",\"live_status\":1,\"liveTime\":1700000000},{\"room_id\":101,\"live_status\":0}]}}",
                "/x/web-interface/nav" =>
                    "{\"code\":0,\"data\":{\"wbi_img\":{\"img_url\":\"https://i0.hdslb.com/bfs/wbi/abcdefghijklmnopqrstuvwxyz123456.png\",\"sub_url\":\"https://i0.hdslb.com/bfs/wbi/ZYXWVUTSRQPONMLKJIHGFEDCBA654321.png\"}}}",
                "/x/web-interface/wbi/search/type" =>
                    "{\"code\":0,\"data\":{\"result\":[{\"type\":\"live_user\",\"roomid\":200,\"uid\":2,\"uname\":\"<em class=\\\"keyword\\\">主播</em>\",\"live_status\":1,\"live_time\":\"2026-09-02 10:00:00\"},{\"type\":\"live_user\",\"uid\":3,\"uname\":\"离线主播\",\"live_status\":0,\"live_time\":\"0000-00-00 00:00:00\"}]}}",
                _ => throw new InvalidOperationException($"Unexpected endpoint: {request.RequestUri}")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
