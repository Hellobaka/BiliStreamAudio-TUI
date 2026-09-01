namespace BiliStreamAudio.Tui.Core;

public interface IRoomResolver
{
    Task<LiveRoom> ResolveAsync(RoomReference room, CancellationToken cancellationToken);
}

public interface IStreamResolver
{
    Task<IReadOnlyList<StreamDescriptor>> ResolveAudioAsync(
        LiveRoom room,
        bool allowVideoFallback,
        CancellationToken cancellationToken);
}

public interface IAuthService
{
    AuthSession? Current
    {
        get;
    }

    Task<AuthSession> LoginAsync(CancellationToken cancellationToken);
    Task<AuthSession?> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(AuthSession session, CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}

public interface ITokenRefreshService
{
    Task<RefreshResult> RefreshIfNeededAsync(
        AuthSession session,
        CancellationToken cancellationToken);
}

public interface IDanmakuConnection : IAsyncDisposable
{
    event EventHandler<DanmakuEvent>? Received;
    event EventHandler<string>? StatusChanged;

    Task ConnectAsync(LiveRoom room, CancellationToken cancellationToken);
    Task DisconnectAsync();
}

public interface IDanmakuSender
{
    Task SendAsync(
        long roomId,
        string message,
        AuthSession session,
        CancellationToken cancellationToken);
}

public interface IAudioPlayer : IDisposable
{
    event EventHandler<PlaybackState>? StateChanged;

    PlaybackState State
    {
        get;
    }
    int Volume
    {
        get;
    }
    bool IsMuted
    {
        get;
    }

    Task PlayAsync(StreamDescriptor stream, CancellationToken cancellationToken);
    Task StopAsync();
    void SetVolume(int volume);
    void ToggleMute();
}
