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
            danmaku,
            () => Task.FromResult(false));

        await session.SwitchAsync(1000, CancellationToken.None);
        await sender.SendAsync(1000, "测试弹幕", auth.Current!, CancellationToken.None);

        Assert.Equal(PlaybackState.Playing, audio.State);
        Assert.Equal("模拟直播间 1000", session.Room?.Title);
        Assert.Contains(received, item => item.Message == "测试弹幕");
    }
}
