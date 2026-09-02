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
        "Mock 用户");
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
            "Mock 主播",
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
    public Task SendAsync(
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

        if (string.IsNullOrWhiteSpace(message) || message.Length > 20)
        {
            throw new ArgumentException("弹幕长度须为 1–20 个字符。", nameof(message));
        }

        connection.Publish(session.UserName ?? "Mock 用户", message);
        return Task.CompletedTask;
    }
}
