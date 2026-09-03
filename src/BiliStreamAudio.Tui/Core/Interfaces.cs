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

public interface ILiveDirectoryService
{
    Task<IReadOnlyList<LiveDirectoryEntry>> GetFollowedLiveAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyList<LiveDirectoryEntry>> SearchUsersAsync(
        string keyword,
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
    event EventHandler<LiveEvent>? EventReceived;
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

/// <summary>
/// 本地历史记录存储（弹幕发送历史 + 直播间播放历史）。
/// </summary>
public interface IHistoryStore : IDisposable
{
    /// <summary>
    /// 记录一次弹幕发送。按 <see cref="Scope"/> 决定是否区分直播间。
    /// </summary>
    void RecordDanmakuSent(long roomId, string message, DateTimeOffset sentAt);

    /// <summary>
    /// 获取弹幕发送历史（最新在前）。
    /// <paramref name="roomId"/> 为 null 时返回全部历史（不区分直播间）。
    /// </summary>
    IReadOnlyList<string> GetDanmakuHistory(long? roomId, int limit = 50);

    /// <summary>
    /// 记录一次直播间播放。每个直播间只保留最近一次观看记录。
    /// </summary>
    void RecordPlayback(long roomId, string anchor, string title, DateTimeOffset watchedAt);

    /// <summary>
    /// 获取直播间播放历史（最近观看在前）。
    /// </summary>
    IReadOnlyList<PlaybackHistoryEntry> GetPlaybackHistory(int limit = 100);

    /// <summary>
    /// 删除一条播放历史。
    /// </summary>
    void DeletePlayback(long roomId);
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

/// <summary>
/// 可选的音频频谱数据源。真实播放器接入 PCM 分析后可实现此接口，
/// 而不影响 <see cref="IAudioPlayer"/> 的播放职责。
/// </summary>
public interface IAudioSpectrumSource
{
    event EventHandler<SpectrumFrame>? SpectrumChanged;

    SpectrumFrame? CurrentSpectrum { get; }
}
