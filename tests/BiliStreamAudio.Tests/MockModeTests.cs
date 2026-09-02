using BiliStreamAudio.Tui.Core;
using BiliStreamAudio.Tui.Infrastructure;

namespace BiliStreamAudio.Tests;

public sealed class MockModeTests
{
    [Fact]
    public void Mock_mode_can_be_enabled_by_argument_or_environment()
    {
        Assert.True(AppOptions.IsMockMode(["--mock"], null));
        Assert.True(AppOptions.IsMockMode([], "1"));
        Assert.True(AppOptions.IsMockMode([], "true"));
        Assert.False(AppOptions.IsMockMode([], "false"));
    }

    [Fact]
    public async Task Mock_services_drive_a_room_session_without_network_dependencies()
    {
        var audio = new MockAudioPlayer();
        var danmaku = new MockDanmakuConnection();
        var received = new List<DanmakuEvent>();
        danmaku.Received += (_, item) => received.Add(item);
        var auth = new MockAuthService();
        var sender = new MockDanmakuSender(danmaku);

        await using var session = new RoomSession(
            new MockRoomResolver(),
            new MockStreamResolver(),
            audio,
            danmaku);

        await session.SwitchAsync(1000, CancellationToken.None);
        await sender.SendAsync(1000, "测试弹幕", auth.Current!, CancellationToken.None);

        Assert.Equal(PlaybackState.Playing, audio.State);
        Assert.Equal("模拟直播间 1000", session.Room?.Title);
        Assert.Contains(received, item => item.Message == "测试弹幕");
    }

    [Fact]
    public async Task Mock_danmaku_sender_fails_for_error_message()
    {
        var sender = new MockDanmakuSender(new MockDanmakuConnection());
        var auth = new MockAuthService();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sender.SendAsync(1000, "error", auth.Current!, CancellationToken.None));
    }

    [Fact]
    public async Task Mock_live_directory_has_playable_and_offline_cards()
    {
        var directory = new MockLiveDirectoryService();

        var followed = await directory.GetFollowedLiveAsync(CancellationToken.None);
        var searched = await directory.SearchUsersAsync("绮梦", CancellationToken.None);

        Assert.NotEmpty(followed);
        Assert.All(followed, item => Assert.True(item.IsLive));
        Assert.Contains(searched, item => item.IsLive && item.RoomId > 0);
        Assert.Contains(searched, item => !item.IsLive && item.RoomId == 0);
        Assert.Empty(await directory.SearchUsersAsync("empty", CancellationToken.None));
    }
}
