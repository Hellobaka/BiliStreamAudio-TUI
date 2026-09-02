using System.Net;
using System.Text;
using BiliStreamAudio.Tui.Core;
using BiliStreamAudio.Tui.Infrastructure;

namespace BiliStreamAudio.Tests;

public sealed class RoomResolverTests
{
    [Fact]
    public async Task Resolve_uses_room_and_anchor_endpoints_for_title_and_user_name()
    {
        var handler = new RoomResponseHandler();
        using var http = new BiliHttp(handler: handler, useRawCookieHeader: false);
        var resolver = new RoomResolver(http);

        var room = await resolver.ResolveAsync(new RoomReference(42), CancellationToken.None);

        Assert.Equal(100, room.RoomId);
        Assert.Equal(42, room.ShortId);
        Assert.Equal(200, room.Uid);
        Assert.Equal("直播标题", room.Title);
        Assert.Equal("直播主播", room.Anchor);
        Assert.True(room.IsLive);
        Assert.Collection(
            handler.RequestUris,
            uri =>
            {
                Assert.Equal("/room/v1/Room/get_info", uri.AbsolutePath);
                Assert.Equal("?room_id=42", uri.Query);
            },
            uri =>
            {
                Assert.Equal("/live_user/v1/Master/info", uri.AbsolutePath);
                Assert.Equal("?uid=200", uri.Query);
            });
    }

    private sealed class RoomResponseHandler : HttpMessageHandler
    {
        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            var body = request.RequestUri!.AbsolutePath switch
            {
                "/room/v1/Room/get_info" =>
                    "{\"code\":0,\"data\":{\"uid\":200,\"room_id\":100,\"short_id\":42,\"title\":\"直播标题\",\"live_status\":1}}",
                "/live_user/v1/Master/info" =>
                    "{\"code\":0,\"data\":{\"info\":{\"uid\":200,\"uname\":\"直播主播\"}}}",
                _ => throw new InvalidOperationException($"Unexpected endpoint: {request.RequestUri}")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
