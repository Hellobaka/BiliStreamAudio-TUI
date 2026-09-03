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
    public async Task Mock_sc_command_publishes_super_chat_without_normal_danmaku()
    {
        var connection = new MockDanmakuConnection();
        var sender = new MockDanmakuSender(connection);
        var auth = new MockAuthService();
        var liveEvents = new List<LiveEvent>();
        var danmaku = new List<DanmakuEvent>();
        connection.EventReceived += (_, item) => liveEvents.Add(item);
        connection.Received += (_, item) => danmaku.Add(item);
        var longMessage = new string('长', 80);

        await sender.SendAsync(
            1000,
            $"sc:30 {longMessage}",
            auth.Current!,
            CancellationToken.None);

        var superChat = Assert.IsType<SuperChatEvent>(Assert.Single(liveEvents));
        Assert.Empty(danmaku);
        Assert.Equal(30, superChat.PriceCny);
        Assert.Equal(longMessage, superChat.Message);
        Assert.Equal(60, superChat.DurationSeconds);
        Assert.Equal(TimeSpan.FromSeconds(60), superChat.EndsAt - superChat.StartsAt);
    }

    [Fact]
    public async Task Mock_sc_command_rejects_invalid_price_or_missing_message()
    {
        var sender = new MockDanmakuSender(new MockDanmakuConnection());
        var auth = new MockAuthService();

        await Assert.ThrowsAsync<ArgumentException>(
            () => sender.SendAsync(1000, "sc:free 文本", auth.Current!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(
            () => sender.SendAsync(1000, "sc:30", auth.Current!, CancellationToken.None));
    }

    [Theory]
    [InlineData(30, SuperChatTier.LightBlue)]
    [InlineData(31, SuperChatTier.Cyan)]
    [InlineData(100, SuperChatTier.Cyan)]
    [InlineData(101, SuperChatTier.Gold)]
    [InlineData(1000, SuperChatTier.Gold)]
    [InlineData(1001, SuperChatTier.Red)]
    public void Super_chat_tiers_follow_price_boundaries(int price, SuperChatTier expected)
    {
        Assert.Equal(expected, SuperChatPresentation.GetTier(price));
        Assert.Equal(TimeSpan.FromSeconds(price * 2), SuperChatPresentation.GetLifetime(price));
    }

    [Fact]
    public void Super_chat_remaining_fraction_is_clamped_over_its_lifetime()
    {
        var startsAt = new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);
        var expiresAt = startsAt.AddSeconds(60);

        Assert.Equal(1, SuperChatPresentation.GetRemainingFraction(
            startsAt.AddSeconds(-1), startsAt, expiresAt));
        Assert.Equal(0.5, SuperChatPresentation.GetRemainingFraction(
            startsAt.AddSeconds(30), startsAt, expiresAt));
        Assert.Equal(0, SuperChatPresentation.GetRemainingFraction(
            expiresAt.AddSeconds(1), startsAt, expiresAt));
    }

    [Fact]
    public void Super_chat_card_and_details_contain_sender_price_and_wrapped_message()
    {
        var item = new SuperChatEvent(
            "sc-1",
            42,
            "Alice",
            "一段足够长的醒目留言正文",
            string.Empty,
            string.Empty,
            50,
            DateTimeOffset.Now,
            null,
            100);

        var card = LiveRoomWindow.FormatSuperChatCard(item, 20);
        var details = LiveRoomWindow.FormatSuperChatDetails(item);

        Assert.True(card.Count > 3);
        Assert.Contains(card, line => line.Contains("SC ¥50", StringComparison.Ordinal));
        Assert.Contains("Alice", details, StringComparison.Ordinal);
        Assert.Contains("¥50", details, StringComparison.Ordinal);
        Assert.Contains(item.Message, details, StringComparison.Ordinal);
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
