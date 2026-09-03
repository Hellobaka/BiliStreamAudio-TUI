using BiliStreamAudio.Tui.Core;
using BiliStreamAudio.Tui.Infrastructure;

namespace BiliStreamAudio.Tests;

public sealed class LiveRoomStatisticsTests
{
    [Fact]
    public void Statistics_aggregate_live_events_and_keep_a_one_minute_danmaku_rate()
    {
        var clock = new TestTimeProvider(new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero));
        var statistics = new LiveRoomStatistics(clock);
        statistics.Start();

        statistics.Record(new DanmakuEvent("Alice", "第一条", clock.GetUtcNow()));
        statistics.Record(new DanmakuEvent("Bob", "第二条", clock.GetUtcNow()));
        statistics.Record(new GiftEvent(
            1, "Alice", 1, "小花花", 2, 1500, 3000, "gold", "gift-1", "", clock.GetUtcNow()));
        statistics.Record(new GiftEvent(
            1, "Alice", 1, "小花花", 2, 1500, 3000, "gold", "gift-1", "", clock.GetUtcNow()));
        statistics.Record(new GiftEvent(
            2, "Bob", 2, "免费礼物", 1, 0, 0, "silver", "gift-2", "", clock.GetUtcNow()));
        statistics.Record(new GuardPurchaseEvent(
            3, "Carol", 3, 2, 396_000, 0, "舰长", clock.GetUtcNow()));
        statistics.Record(new SuperChatEvent(
            "sc-1", 4, "Dan", "加油", "", "", 30, clock.GetUtcNow(), null, 60));
        statistics.Record(new SuperChatEvent(
            "sc-1", 4, "Dan", "加油", "", "", 30, clock.GetUtcNow(), null, 60));

        clock.Advance(TimeSpan.FromSeconds(30));
        var snapshot = statistics.GetSnapshot();

        Assert.Equal(TimeSpan.FromSeconds(30), snapshot.WatchingDuration);
        Assert.Equal(2, snapshot.DanmakuCount);
        Assert.Equal(2, snapshot.DanmakuRatePerMinute);
        Assert.Equal(3, snapshot.GiftCount);
        Assert.Equal(429m, snapshot.GiftAmountCny);
        Assert.Equal(2, snapshot.GuardCount);
        Assert.Equal(1, snapshot.SuperChatCount);

        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(0, statistics.GetSnapshot().DanmakuRatePerMinute);
    }

    [Fact]
    public async Task Room_session_resets_statistics_for_a_new_room_and_stops_the_duration()
    {
        var clock = new TestTimeProvider(new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero));
        var danmaku = new MockDanmakuConnection();
        var audio = new MockAudioPlayer();
        await using var session = new RoomSession(
            new MockRoomResolver(),
            new MockStreamResolver(),
            audio,
            danmaku,
            timeProvider: clock);

        await session.SwitchAsync(1000, CancellationToken.None);
        danmaku.Publish(new DanmakuEvent("Alice", "测试", clock.GetUtcNow()));
        clock.Advance(TimeSpan.FromMinutes(2));
        await session.StopAsync();

        var stopped = session.Statistics.GetSnapshot();
        Assert.Equal(TimeSpan.FromMinutes(2), stopped.WatchingDuration);
        Assert.Equal(2, stopped.DanmakuCount);
        Assert.Equal(0, stopped.DanmakuRatePerMinute);

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(stopped, session.Statistics.GetSnapshot());

        await session.SwitchAsync(2000, CancellationToken.None);
        var restarted = session.Statistics.GetSnapshot();
        Assert.Equal(TimeSpan.Zero, restarted.WatchingDuration);
        Assert.Equal(1, restarted.DanmakuCount);
        Assert.Equal(0m, restarted.GiftAmountCny);
    }

    private sealed class TestTimeProvider(DateTimeOffset current) : TimeProvider
    {
        private DateTimeOffset _current = current;

        public override DateTimeOffset GetUtcNow() => _current;

        public void Advance(TimeSpan duration) => _current = _current.Add(duration);
    }
}
