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
        var received = new System.Collections.Concurrent.ConcurrentQueue<DanmakuEvent>();
        danmaku.Received += (_, item) => received.Enqueue(item);
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
    public async Task Mock_audio_player_generates_a_spectrum_while_playing()
    {
        using var audio = new MockAudioPlayer();
        var generated = new TaskCompletionSource<SpectrumFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        audio.SpectrumChanged += (_, frame) =>
        {
            if (frame.Magnitudes.Count > 0)
            {
                generated.TrySetResult(frame);
            }
        };

        await audio.PlayAsync(
            new StreamDescriptor(new Uri("https://example.test/live.flv"), "http_stream", "flv", 0, true, 1000),
            CancellationToken.None);
        var spectrum = await generated.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(64, spectrum.Magnitudes.Count);
        Assert.All(spectrum.Magnitudes, magnitude => Assert.InRange(magnitude, 0f, 1f));
        Assert.True(spectrum.Magnitudes.Distinct().Count() > 1);
    }

    [Fact]
    public void Real_AudioPlayer_constructor_registers_callbacks_without_crash()
    {
        // Smoke test: creating a real AudioPlayer must succeed.
        // This exercises LibVLC initialization, SetAudioFormat, and SetAudioCallbacks
        // to confirm that callback registration does not break the player init path.
        using var audio = new AudioPlayer();
        Assert.Equal(PlaybackState.Stopped, audio.State);
        Assert.Equal(70, audio.Volume);
        Assert.False(audio.IsMuted);
    }

    [Fact]
    public async Task Stopping_a_room_session_stops_playback_and_clears_the_room()
    {
        var audio = new MockAudioPlayer();
        var danmaku = new MockDanmakuConnection();

        await using var session = new RoomSession(
            new MockRoomResolver(),
            new MockStreamResolver(),
            audio,
            danmaku);

        await session.SwitchAsync(1000, CancellationToken.None);
        await session.StopAsync();

        Assert.Equal(PlaybackState.Stopped, audio.State);
        Assert.Null(session.Room);
    }

    [Fact]
    public async Task Mock_danmaku_is_generated_only_while_connected()
    {
        await using var connection = new MockDanmakuConnection(
            TimeSpan.FromMilliseconds(5),
            TimeSpan.FromMilliseconds(10));
        var generated = new TaskCompletionSource<DanmakuEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var receivedCount = 0;
        connection.Received += (_, item) =>
        {
            if (item.UserName != "系统")
            {
                Interlocked.Increment(ref receivedCount);
                generated.TrySetResult(item);
            }
        };

        await connection.ConnectAsync(
            new LiveRoom(1000, 1000, 1, "模拟直播间", "Mock_主播", true),
            CancellationToken.None);

        var item = await generated.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await connection.DisconnectAsync();
        var countAfterDisconnect = Volatile.Read(ref receivedCount);
        await Task.Delay(TimeSpan.FromMilliseconds(50));

        Assert.NotEqual("系统", item.UserName);
        Assert.NotEmpty(item.Message);
        Assert.Equal(countAfterDisconnect, Volatile.Read(ref receivedCount));
    }

    [Fact]
    public async Task Mock_connection_generates_gifts_and_super_chats_while_connected()
    {
        await using var connection = new MockDanmakuConnection(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(5),
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(5),
            TimeSpan.FromMilliseconds(10));
        var gift = new TaskCompletionSource<GiftEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var superChat = new TaskCompletionSource<SuperChatEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.EventReceived += (_, item) =>
        {
            if (item is GiftEvent receivedGift)
            {
                gift.TrySetResult(receivedGift);
            }
            else if (item is SuperChatEvent receivedSuperChat)
            {
                superChat.TrySetResult(receivedSuperChat);
            }
        };

        await connection.ConnectAsync(
            new LiveRoom(1000, 1000, 1, "模拟直播间", "Mock_主播", true),
            CancellationToken.None);

        await Task.WhenAll(
            gift.Task.WaitAsync(TimeSpan.FromSeconds(1)),
            superChat.Task.WaitAsync(TimeSpan.FromSeconds(1)));

        Assert.True((await gift.Task).AmountCny > 0);
        Assert.True((await superChat.Task).PriceCny > 0);
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

    [Fact]
    public async Task Mock_gift_command_publishes_gift_event_without_normal_danmaku()
    {
        var connection = new MockDanmakuConnection();
        var sender = new MockDanmakuSender(connection);
        var auth = new MockAuthService();
        var liveEvents = new List<LiveEvent>();
        var danmaku = new List<DanmakuEvent>();
        connection.EventReceived += (_, item) => liveEvents.Add(item);
        connection.Received += (_, item) => danmaku.Add(item);

        await sender.SendAsync(1000, "gift 1.5 2 小花花", auth.Current!, CancellationToken.None);

        var gift = Assert.IsType<GiftEvent>(Assert.Single(liveEvents));
        Assert.Empty(danmaku);
        Assert.Equal("小花花", gift.GiftName);
        Assert.Equal(2, gift.Count);
        Assert.Equal(3m, gift.AmountCny);
    }

    [Fact]
    public async Task Mock_gift_command_rejects_invalid_amount_count_or_description()
    {
        var sender = new MockDanmakuSender(new MockDanmakuConnection());
        var auth = new MockAuthService();

        await Assert.ThrowsAsync<ArgumentException>(
            () => sender.SendAsync(1000, "gift free 2 小花花", auth.Current!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(
            () => sender.SendAsync(1000, "gift 1.5 0 小花花", auth.Current!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(
            () => sender.SendAsync(1000, "gift 1.5 2", auth.Current!, CancellationToken.None));
    }

    [Fact]
    public async Task Mock_guard_command_publishes_guard_purchase_with_the_selected_tier()
    {
        var connection = new MockDanmakuConnection();
        var sender = new MockDanmakuSender(connection);
        var auth = new MockAuthService();
        var liveEvents = new List<LiveEvent>();
        connection.EventReceived += (_, item) => liveEvents.Add(item);

        await sender.SendAsync(1000, "guard 2 3", auth.Current!, CancellationToken.None);

        var guard = Assert.IsType<GuardPurchaseEvent>(Assert.Single(liveEvents));
        Assert.Equal(2, guard.GuardLevel);
        Assert.Equal(3, guard.Count);
        Assert.Equal("提督", guard.GiftName);
        Assert.Equal(1998m, guard.AmountCny);
    }

    [Theory]
    [InlineData("guard 0 1")]
    [InlineData("guard 4 1")]
    [InlineData("guard 1 0")]
    [InlineData("guard 1")]
    public async Task Mock_guard_command_rejects_invalid_tier_or_month_count(string command)
    {
        var sender = new MockDanmakuSender(new MockDanmakuConnection());
        var auth = new MockAuthService();

        await Assert.ThrowsAsync<ArgumentException>(
            () => sender.SendAsync(1000, command, auth.Current!, CancellationToken.None));
    }

    [Fact]
    public async Task Mock_badge_command_attaches_the_selected_medal_to_later_danmaku()
    {
        var connection = new MockDanmakuConnection();
        var sender = new MockDanmakuSender(connection);
        var auth = new MockAuthService();
        var received = new List<DanmakuEvent>();
        connection.Received += (_, item) => received.Add(item);

        await sender.SendAsync(1000, "badge 17 测试团", auth.Current!, CancellationToken.None);
        await sender.SendAsync(1000, "携带勋章的弹幕", auth.Current!, CancellationToken.None);

        var danmaku = Assert.Single(received);
        var medal = Assert.IsType<FanMedal>(danmaku.Medal);
        Assert.Equal(17, medal.Level);
        Assert.Equal("测试团", medal.Name);
    }

    [Theory]
    [InlineData("badge -1 测试团")]
    [InlineData("badge 17")]
    [InlineData("badge 等级 测试团")]
    [InlineData("badge 17 测试后援团")]
    [InlineData("badge 17 fanclub")]
    public async Task Mock_badge_command_rejects_invalid_level_or_missing_name(string command)
    {
        var sender = new MockDanmakuSender(new MockDanmakuConnection());
        var auth = new MockAuthService();

        await Assert.ThrowsAsync<ArgumentException>(
            () => sender.SendAsync(1000, command, auth.Current!, CancellationToken.None));
    }

    [Theory]
    [InlineData(1, "✨ Alice送出了小花花。")]
    [InlineData(2, "✨ Alice送出了小花花 x2。")]
    public void Gift_message_omits_count_for_a_single_gift(int count, string expected)
    {
        var gift = new GiftEvent(
            42, "Alice", 1, "小花花", count, 100, count * 100, "gold", "gift-1", "", DateTimeOffset.Now);

        Assert.Equal(expected, LiveRoomWindow.FormatGiftMessage(gift));
    }

    [Fact]
    public void Gift_message_can_include_amount_when_enabled()
    {
        var gift = new GiftEvent(
            42, "Alice", 1, "小花花", 2, 1500, 3000, "gold", "gift-1", "", DateTimeOffset.Now);

        Assert.Equal("✨ Alice送出了小花花 x2。 ￥3", LiveRoomWindow.FormatGiftMessage(gift, showAmount: true));
    }

    [Fact]
    public void Guard_purchase_message_shares_the_gift_amount_setting()
    {
        var guard = new GuardPurchaseEvent(
            42, "Alice", 1, 2, 19_998_000, 0, "总督", DateTimeOffset.Now);

        Assert.Equal("⚓ Alice开通了总督 2个月。", LiveRoomWindow.FormatGuardPurchaseMessage(guard));
        Assert.Equal("⚓ Alice开通了总督 2个月。￥19998", LiveRoomWindow.FormatGuardPurchaseMessage(guard, showAmount: true));
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
