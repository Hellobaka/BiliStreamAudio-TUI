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
        Received?.Invoke(this, new DanmakuEvent(userName, message, DateTimeOffset.Now));
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
