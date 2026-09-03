using System.Globalization;
using System.Text;
using BiliStreamAudio.Tui.Core;

namespace BiliStreamAudio.Tui.Infrastructure;

internal sealed class MockAuthService : IAuthService
{
    private static readonly IReadOnlyDictionary<string, string> MockCookies =
        new Dictionary<string, string>
        {
            ["SESSDATA"] = "mock-session",
            ["bili_jct"] = "mock-csrf"
        };

    private AuthSession? _current = CreateSession();

    public AuthSession? Current => _current;

    public Task<AuthSession> LoginAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _current = CreateSession();
        return Task.FromResult(_current);
    }

    public Task<AuthSession?> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_current);
    }

    public Task SaveAsync(AuthSession session, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _current = session;
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _current = null;
        return Task.CompletedTask;
    }

    private static AuthSession CreateSession() => new(
        MockCookies,
        null,
        1,
        null,
        "Mock_用户");
}

internal sealed class MockTokenRefreshService : ITokenRefreshService
{
    public Task<RefreshResult> RefreshIfNeededAsync(
        AuthSession session,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new RefreshResult(true, session, null));
    }
}

internal sealed class MockRoomResolver : IRoomResolver
{
    public Task<LiveRoom> ResolveAsync(RoomReference room, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (room.RequestedId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(room), "直播间号必须为正数。");
        }

        return Task.FromResult(new LiveRoom(
            room.RequestedId,
            room.RequestedId,
            1,
            $"模拟直播间 {room.RequestedId}",
            "Mock_主播",
            true));
    }
}

internal sealed class MockStreamResolver : IStreamResolver
{
    public Task<IReadOnlyList<StreamDescriptor>> ResolveAudioAsync(
        LiveRoom room,
        bool allowVideoFallback,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<StreamDescriptor> streams =
        [
            new StreamDescriptor(
                new Uri($"mock://audio/{room.RoomId}"),
                "mock",
                "none",
                0,
                true,
                room.RoomId)
        ];
        return Task.FromResult(streams);
    }
}

internal sealed class MockLiveDirectoryService : ILiveDirectoryService
{
    private static readonly IReadOnlyList<LiveDirectoryEntry> FollowedLive =
    [
        new(10001, 101, "绮梦", "深夜电台：一起听歌", true, DateTimeOffset.Now.AddHours(-2)),
        new(10002, 102, "小北", "独立游戏试玩", true, DateTimeOffset.Now.AddMinutes(-48)),
        new(10003, 103, "阿澈", "晚间杂谈", true, DateTimeOffset.Now.AddMinutes(-15))
    ];

    private static readonly IReadOnlyList<LiveDirectoryEntry> SearchResults =
    [
        new(10001, 101, "绮梦", "", true, DateTimeOffset.Now.AddHours(-2)),
        new(0, 104, "未开播的模拟主播", "", false, null),
        new(10004, 105, "游戏小屋", "", true, DateTimeOffset.Now.AddHours(-1))
    ];

    public Task<IReadOnlyList<LiveDirectoryEntry>> GetFollowedLiveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(FollowedLive);
    }

    public async Task<IReadOnlyList<LiveDirectoryEntry>> SearchUsersAsync(string keyword, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Delay(Random.Shared.Next(300, 801), cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(keyword)
            || keyword.Equals("empty", StringComparison.OrdinalIgnoreCase)
            ? []
            : SearchResults;
    }
}

internal sealed class MockAudioPlayer : IAudioPlayer
{
    private PlaybackState _state = PlaybackState.Stopped;
    private int _volume = 70;
    private bool _muted;

    public event EventHandler<PlaybackState>? StateChanged;

    public PlaybackState State => _state;
    public int Volume => _volume;
    public bool IsMuted => _muted;

    public Task PlayAsync(StreamDescriptor stream, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetState(PlaybackState.Buffering);
        SetState(PlaybackState.Playing);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        SetState(PlaybackState.Stopped);
        return Task.CompletedTask;
    }

    public void SetVolume(int volume) => _volume = Math.Clamp(volume, 0, 100);

    public void ToggleMute() => _muted = !_muted;

    public void Dispose()
    {
    }

    private void SetState(PlaybackState state)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;
        StateChanged?.Invoke(this, state);
    }
}

internal sealed class MockDanmakuConnection : IDanmakuConnection
{
    public event EventHandler<LiveEvent>? EventReceived;
    public event EventHandler<DanmakuEvent>? Received;
    public event EventHandler<string>? StatusChanged;

    public Task ConnectAsync(LiveRoom room, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StatusChanged?.Invoke(this, "弹幕模拟已连接");
        Publish("系统", $"已进入模拟直播间 {room.RoomId}");
        return Task.CompletedTask;
    }

    public Task DisconnectAsync() => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void Publish(string userName, string message)
    {
        Publish(new DanmakuEvent(userName, message, DateTimeOffset.Now));
    }

    public void Publish(LiveEvent item)
    {
        EventReceived?.Invoke(this, item);
        if (item is DanmakuEvent danmaku)
        {
            Received?.Invoke(this, danmaku);
        }
    }
}

internal static class MockSuperChatCommand
{
    private const string Prefix = "sc:";

    public static bool IsCommand(string value) =>
        value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    public static SuperChatEvent Parse(string value, AuthSession session, DateTimeOffset now)
    {
        var payload = value[Prefix.Length..];
        var parts = payload.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var price)
            || price <= 0
            || string.IsNullOrWhiteSpace(parts[1]))
        {
            throw new ArgumentException("Mock SC 格式应为：sc:<正整数金额> <正文>。", nameof(value));
        }

        var lifetime = SuperChatPresentation.GetLifetime(price);
        var durationSeconds = price > int.MaxValue / 2 ? int.MaxValue : price * 2;
        return new SuperChatEvent(
            Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
            session.UserId,
            session.UserName ?? "Mock 用户",
            parts[1],
            string.Empty,
            string.Empty,
            price,
            now,
            now.Add(lifetime),
            durationSeconds);
    }
}

internal sealed class MockHistoryStore : IHistoryStore
{
    private readonly object _gate = new();
    private readonly List<(long RoomId, string Message, DateTimeOffset SentAt)> _danmaku = [];
    private readonly Dictionary<long, PlaybackHistoryEntry> _playback = [];

    public void RecordDanmakuSent(long roomId, string message, DateTimeOffset sentAt)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        lock (_gate)
        {
            _danmaku.Add((roomId, message, sentAt));
        }
    }

    public IReadOnlyList<string> GetDanmakuHistory(long? roomId, int limit = 50)
    {
        lock (_gate)
        {
            return _danmaku
                .Where(item => roomId is null || item.RoomId == roomId)
                .OrderByDescending(item => item.SentAt)
                .Take(limit)
                .Select(item => item.Message)
                .ToList();
        }
    }

    public void RecordPlayback(long roomId, string anchor, string title, DateTimeOffset watchedAt)
    {
        lock (_gate)
        {
            _playback[roomId] = new PlaybackHistoryEntry(roomId, anchor, title, watchedAt);
        }
    }

    public IReadOnlyList<PlaybackHistoryEntry> GetPlaybackHistory(int limit = 100)
    {
        lock (_gate)
        {
            return _playback.Values
                .OrderByDescending(entry => entry.WatchedAt)
                .Take(limit)
                .ToList();
        }
    }

    public void DeletePlayback(long roomId)
    {
        lock (_gate)
        {
            _playback.Remove(roomId);
        }
    }

    public void Dispose()
    {
    }
}

internal sealed class MockDanmakuSender(MockDanmakuConnection connection) : IDanmakuSender
{
    public async Task SendAsync(
        long roomId,
        string message,
        AuthSession session,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!session.IsAuthenticated)
        {
            throw new InvalidOperationException("请先登录。");
        }

        if (MockSuperChatCommand.IsCommand(message))
        {
            var superChat = MockSuperChatCommand.Parse(message, session, DateTimeOffset.Now);
            await Task.Delay(Random.Shared.Next(100, 301), cancellationToken).ConfigureAwait(false);
            connection.Publish(superChat);
            return;
        }

        if (string.IsNullOrWhiteSpace(message) || message.EnumerateRunes().Count() > 30)
        {
            throw new ArgumentException("弹幕长度须为 1–30 个字符。", nameof(message));
        }

        await Task.Delay(Random.Shared.Next(400, 1201), cancellationToken).ConfigureAwait(false);
        if (string.Equals(message, "error", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("模拟发送失败。");
        }

        connection.Publish(session.UserName ?? "Mock 用户", message);
    }
}
