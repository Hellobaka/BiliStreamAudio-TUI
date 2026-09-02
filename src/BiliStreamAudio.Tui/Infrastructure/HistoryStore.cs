using BiliStreamAudio.Tui.Core;
using LiteDB;

namespace BiliStreamAudio.Tui.Infrastructure;

/// <summary>
/// 弹幕发送历史的范围。
/// <see cref="PerRoom"/> 按直播间区分历史（当前默认）；
/// <see cref="Global"/> 不区分直播间，所有历史合并为一条时间线，
/// 为将来“全局弹幕历史”功能预留。
/// </summary>
public enum DanmakuHistoryScope
{
    PerRoom,
    Global
}

/// <summary>
/// 基于 LiteDB 的本地历史记录存储，记录弹幕发送历史与直播间播放历史。
/// </summary>
public sealed class HistoryStore : IHistoryStore
{
    private const string DanmakuCollection = "danmaku_history";
    private const string PlaybackCollection = "playback_history";
    private const int MaximumDanmakuPerRoom = 200;

    private readonly LiteDatabase _db;
    private readonly ILiteCollection<DanmakuHistoryRecord> _danmaku;
    private readonly ILiteCollection<PlaybackHistoryRecord> _playback;
    private readonly DanmakuHistoryScope _scope;

    public HistoryStore(string? path = null, DanmakuHistoryScope scope = DanmakuHistoryScope.PerRoom)
    {
        _scope = scope;
        var directory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var resolvedPath = Path.GetFullPath(
            path ?? Path.Combine(directory, "BiliStreamAudio-TUI", "history.db"));
        var parentDirectory = Path.GetDirectoryName(resolvedPath);
        if (!string.IsNullOrEmpty(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }

        _db = new LiteDatabase(new ConnectionString($"filename={resolvedPath}"), new BsonMapper());
        _danmaku = _db.GetCollection<DanmakuHistoryRecord>(DanmakuCollection, BsonAutoId.Int64);
        _playback = _db.GetCollection<PlaybackHistoryRecord>(PlaybackCollection, BsonAutoId.Int64);
        _danmaku.EnsureIndex(record => record.RoomId);
        _playback.EnsureIndex(record => record.RoomId, unique: true);
    }

    public void RecordDanmakuSent(long roomId, string message, DateTimeOffset sentAt)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _danmaku.Insert(new DanmakuHistoryRecord
        {
            RoomId = roomId,
            Message = message,
            SentAt = sentAt.ToUniversalTime()
        });

        // 每个直播间只保留最近 N 条，避免历史无限增长。
        var overflow = _danmaku
            .Find(record => record.RoomId == roomId)
            .OrderByDescending(record => record.SentAt)
            .Skip(MaximumDanmakuPerRoom)
            .Select(record => record.Id)
            .ToList();
        foreach (var id in overflow)
        {
            _danmaku.Delete(id);
        }
    }

    public IReadOnlyList<string> GetDanmakuHistory(long? roomId, int limit = 50)
    {
        var records = _scope == DanmakuHistoryScope.PerRoom && roomId is { } scopedRoomId
            ? _danmaku.Find(record => record.RoomId == scopedRoomId)
            : _danmaku.FindAll();

        return records
            .OrderByDescending(record => record.SentAt)
            .Take(limit)
            .Select(record => record.Message)
            .ToList();
    }

    public void RecordPlayback(long roomId, string anchor, string title, DateTimeOffset watchedAt)
    {
        _playback.Upsert(new PlaybackHistoryRecord
        {
            RoomId = roomId,
            Anchor = anchor,
            Title = title,
            WatchedAt = watchedAt.ToUniversalTime()
        });
    }

    public IReadOnlyList<PlaybackHistoryEntry> GetPlaybackHistory(int limit = 100)
    {
        return _playback
            .FindAll()
            .OrderByDescending(record => record.WatchedAt)
            .Take(limit)
            .Select(record => new PlaybackHistoryEntry(
                record.RoomId,
                record.Anchor,
                record.Title,
                record.WatchedAt.ToLocalTime()))
            .ToList();
    }

    public void DeletePlayback(long roomId)
    {
        _playback.Delete(roomId);
    }

    public void Dispose() => _db.Dispose();

    private sealed class DanmakuHistoryRecord
    {
        public long Id
        {
            get; set;
        }
        public long RoomId
        {
            get; set;
        }
        public string Message { get; set; } = string.Empty;
        public DateTimeOffset SentAt
        {
            get; set;
        }
    }

    private sealed class PlaybackHistoryRecord
    {
        [BsonId]
        public long RoomId
        {
            get; set;
        }
        public string Anchor { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public DateTimeOffset WatchedAt
        {
            get; set;
        }
    }
}
