using BiliStreamAudio.Tui.Core;
using BiliStreamAudio.Tui.Infrastructure;

namespace BiliStreamAudio.Tests;

public sealed class HistoryStoreTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"bili-history-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        try
        {
            File.Delete(_dbPath);
        }
        catch (IOException)
        {
            // Best-effort cleanup of the temporary database file.
        }
    }

    [Fact]
    public void Danmaku_history_is_scoped_per_room_by_default()
    {
        using var store = new HistoryStore(_dbPath);
        var now = DateTimeOffset.Now;

        store.RecordDanmakuSent(1001, "第一条", now.AddMinutes(-2));
        store.RecordDanmakuSent(1001, "第二条", now.AddMinutes(-1));
        store.RecordDanmakuSent(1002, "别的房间", now);

        var room1001 = store.GetDanmakuHistory(1001);
        var room1002 = store.GetDanmakuHistory(1002);

        Assert.Equal(["第二条", "第一条"], room1001);
        Assert.Equal(["别的房间"], room1002);
    }

    [Fact]
    public void Danmaku_history_global_scope_returns_all_rooms()
    {
        using var store = new HistoryStore(_dbPath, DanmakuHistoryScope.Global);
        var now = DateTimeOffset.Now;

        store.RecordDanmakuSent(1001, "房间一", now.AddMinutes(-1));
        store.RecordDanmakuSent(1002, "房间二", now);

        var all = store.GetDanmakuHistory(roomId: null);

        Assert.Equal(["房间二", "房间一"], all);
    }

    [Fact]
    public void Danmaku_history_is_ordered_newest_first_and_limited()
    {
        using var store = new HistoryStore(_dbPath);
        var now = DateTimeOffset.Now;

        for (var index = 0; index < 5; index++)
        {
            store.RecordDanmakuSent(1001, $"消息{index}", now.AddMinutes(index));
        }

        var history = store.GetDanmakuHistory(1001, limit: 3);

        Assert.Equal(["消息4", "消息3", "消息2"], history);
    }

    [Fact]
    public void Danmaku_history_ignores_blank_messages()
    {
        using var store = new HistoryStore(_dbPath);

        store.RecordDanmakuSent(1001, "   ", sentAt: DateTimeOffset.Now);
        store.RecordDanmakuSent(1001, string.Empty, sentAt: DateTimeOffset.Now);

        Assert.Empty(store.GetDanmakuHistory(1001));
    }

    [Fact]
    public void Playback_history_keeps_only_latest_entry_per_room()
    {
        using var store = new HistoryStore(_dbPath);
        var now = DateTimeOffset.Now;

        store.RecordPlayback(1001, "主播A", "旧标题", now.AddHours(-2));
        store.RecordPlayback(1001, "主播A", "新标题", now.AddHours(-1));
        store.RecordPlayback(1002, "主播B", "标题B", now);

        var history = store.GetPlaybackHistory();

        Assert.Equal(2, history.Count);
        var room1001 = Assert.Single(history, entry => entry.RoomId == 1001);
        Assert.Equal("新标题", room1001.Title);
        Assert.Equal(now.AddHours(-1), room1001.WatchedAt, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void Playback_history_is_ordered_by_watched_at_descending()
    {
        using var store = new HistoryStore(_dbPath);
        var now = DateTimeOffset.Now;

        store.RecordPlayback(1001, "主播A", "标题A", now.AddHours(-3));
        store.RecordPlayback(1002, "主播B", "标题B", now.AddHours(-1));
        store.RecordPlayback(1003, "主播C", "标题C", now.AddHours(-2));

        var history = store.GetPlaybackHistory();

        Assert.Equal([1002L, 1003L, 1001L], history.Select(entry => entry.RoomId));
    }

    [Fact]
    public void Delete_playback_removes_only_the_targeted_room()
    {
        using var store = new HistoryStore(_dbPath);
        var now = DateTimeOffset.Now;

        store.RecordPlayback(1001, "主播A", "标题A", now);
        store.RecordPlayback(1002, "主播B", "标题B", now);

        store.DeletePlayback(1001);

        var history = store.GetPlaybackHistory();
        Assert.Single(history);
        Assert.Equal(1002L, history[0].RoomId);
    }

    [Fact]
    public void History_persists_across_store_instances()
    {
        var now = DateTimeOffset.Now;
        using (var store = new HistoryStore(_dbPath))
        {
            store.RecordDanmakuSent(1001, "持久化弹幕", now);
            store.RecordPlayback(1001, "主播A", "标题A", now);
        }

        using var reopened = new HistoryStore(_dbPath);
        Assert.Equal(["持久化弹幕"], reopened.GetDanmakuHistory(1001));
        var playback = Assert.Single(reopened.GetPlaybackHistory());
        Assert.Equal(1001L, playback.RoomId);
    }
}
