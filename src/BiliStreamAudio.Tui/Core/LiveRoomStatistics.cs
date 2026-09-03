namespace BiliStreamAudio.Tui.Core;

/// <summary>
/// 当前观看会话的内存统计。每次开始新的会话时重置，停止后保留最后的统计快照。
/// </summary>
public sealed class LiveRoomStatistics
{
    private static readonly TimeSpan DanmakuRateWindow = TimeSpan.FromMinutes(1);

    private readonly object _sync = new();
    private readonly TimeProvider _timeProvider;
    private readonly Queue<DateTimeOffset> _recentDanmaku = [];
    private readonly HashSet<string> _giftEventIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _superChatIds = new(StringComparer.Ordinal);
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _stoppedAt;
    private long _danmakuCount;
    private long _giftCount;
    private decimal _giftAmountCny;
    private long _guardCount;
    private long _superChatCount;

    public LiveRoomStatistics(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public void Start()
    {
        lock (_sync)
        {
            _startedAt = _timeProvider.GetUtcNow();
            _stoppedAt = null;
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _startedAt = null;
            _stoppedAt = null;
            _danmakuCount = 0;
            _giftCount = 0;
            _giftAmountCny = 0;
            _guardCount = 0;
            _superChatCount = 0;
            _recentDanmaku.Clear();
            _giftEventIds.Clear();
            _superChatIds.Clear();
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (_startedAt is not null && _stoppedAt is null)
            {
                _stoppedAt = _timeProvider.GetUtcNow();
            }
        }
    }

    /// <summary>
    /// 记录一个从弹幕连接接收的事件。COMBO_SEND 是 SEND_GIFT 的聚合通知，
    /// 因此不重复计入礼物数量和金额。
    /// </summary>
    public void Record(LiveEvent liveEvent)
    {
        lock (_sync)
        {
            if (_startedAt is null || _stoppedAt is not null)
            {
                return;
            }

            switch (liveEvent)
            {
                case DanmakuEvent:
                    _danmakuCount++;
                    _recentDanmaku.Enqueue(_timeProvider.GetUtcNow());
                    break;
                case GiftEvent gift when IsNewEvent(_giftEventIds, gift.EventId):
                    _giftCount += Math.Max(0, gift.Count);
                    _giftAmountCny += gift.AmountCny;
                    break;
                case GuardPurchaseEvent guard:
                    _guardCount += Math.Max(0, guard.Count);
                    _giftAmountCny += Math.Max(0, guard.AmountCny);
                    break;
                case SuperChatEvent superChat when IsNewEvent(_superChatIds, superChat.Id):
                    _superChatCount++;
                    _giftAmountCny += Math.Max(0, (decimal)superChat.PriceCny);
                    break;
            }
        }
    }

    public LiveRoomStatisticsSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            var endedAt = _stoppedAt ?? _timeProvider.GetUtcNow();
            TrimDanmakuRateWindow(endedAt);
            var watchingDuration = _startedAt is { } startedAt
                ? endedAt - startedAt
                : TimeSpan.Zero;
            return new LiveRoomStatisticsSnapshot(
                watchingDuration,
                _danmakuCount,
                _recentDanmaku.Count,
                _giftCount,
                _giftAmountCny,
                _guardCount,
                _superChatCount);
        }
    }

    private void TrimDanmakuRateWindow(DateTimeOffset now)
    {
        while (_recentDanmaku.TryPeek(out var receivedAt)
            && now - receivedAt >= DanmakuRateWindow)
        {
            _recentDanmaku.Dequeue();
        }
    }

    private static bool IsNewEvent(HashSet<string> eventIds, string eventId) =>
        string.IsNullOrEmpty(eventId) || eventIds.Add(eventId);
}

public sealed record LiveRoomStatisticsSnapshot(
    TimeSpan WatchingDuration,
    long DanmakuCount,
    int DanmakuRatePerMinute,
    long GiftCount,
    decimal GiftAmountCny,
    long GuardCount,
    long SuperChatCount);
