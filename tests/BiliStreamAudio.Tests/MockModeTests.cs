using BiliStreamAudio.Tui.Core;
using BiliStreamAudio.Tui.Infrastructure;
using BiliStreamAudio.Tui.Views;

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
    public async Task Playback_continues_when_history_cannot_be_saved()
    {
        var audio = new MockAudioPlayer();
        var danmaku = new MockDanmakuConnection();
        string? latestStatus = null;

        await using var session = new RoomSession(
            new MockRoomResolver(),
            new MockStreamResolver(),
            audio,
            danmaku,
            new ThrowingHistoryStore());
        session.StatusChanged += (_, status) => latestStatus = status;

        await session.SwitchAsync(1000, CancellationToken.None);

        Assert.Equal(PlaybackState.Playing, audio.State);
        Assert.Contains("观看历史保存失败", latestStatus);
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
    public void Danmaku_history_navigation_moves_older_with_up_and_returns_to_draft_with_down()
    {
        var newest = LiveRoomWindow.GetNextDanmakuHistoryIndex(-1, count: 3, direction: -1);
        var older = LiveRoomWindow.GetNextDanmakuHistoryIndex(newest, count: 3, direction: -1);
        var newestAgain = LiveRoomWindow.GetNextDanmakuHistoryIndex(older, count: 3, direction: 1);
        var draft = LiveRoomWindow.GetNextDanmakuHistoryIndex(newestAgain, count: 3, direction: 1);

        Assert.Equal(0, newest);
        Assert.Equal(1, older);
        Assert.Equal(0, newestAgain);
        Assert.Equal(-1, draft);
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

    private sealed class ThrowingHistoryStore : IHistoryStore
    {
        public void RecordDanmakuSent(long roomId, string message, DateTimeOffset sentAt)
        {
        }

        public IReadOnlyList<string> GetDanmakuHistory(long? roomId, int limit = 50) => [];

        public void RecordPlayback(long roomId, string anchor, string title, DateTimeOffset watchedAt) =>
            throw new IOException("磁盘不可写");

        public IReadOnlyList<PlaybackHistoryEntry> GetPlaybackHistory(int limit = 100) => [];

        public void DeletePlayback(long roomId)
        {
        }

        public void Dispose()
        {
        }
    }
}
