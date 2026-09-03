namespace BiliStreamAudio.Tui.Core;

public sealed class RoomSession : IAsyncDisposable
{
    private readonly IRoomResolver _rooms;
    private readonly IStreamResolver _streams;
    private readonly IAudioPlayer _audio;
    private readonly IDanmakuConnection _danmaku;
    private readonly IHistoryStore? _history;
    private CancellationTokenSource? _sessionLifetime;

    public LiveRoom? Room
    {
        get; private set;
    }

    public event EventHandler<LiveRoom>? RoomChanged;
    public event EventHandler<string>? StatusChanged;

    public RoomSession(
        IRoomResolver rooms,
        IStreamResolver streams,
        IAudioPlayer audio,
        IDanmakuConnection danmaku,
        IHistoryStore? history = null)
    {
        _rooms = rooms;
        _streams = streams;
        _audio = audio;
        _danmaku = danmaku;
        _history = history;
    }

    public async Task SwitchAsync(long roomId, CancellationToken cancellationToken)
    {
        await StopCurrentAsync().ConfigureAwait(false);
        _sessionLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _sessionLifetime.Token;
        StatusChanged?.Invoke(this, "正在解析直播间…");

        var room = await _rooms.ResolveAsync(new RoomReference(roomId), token).ConfigureAwait(false);
        if (!room.IsLive)
        {
            throw new InvalidOperationException("该直播间当前未开播。");
        }

        Room = room;
        RoomChanged?.Invoke(this, room);

        var candidates = await _streams.ResolveAudioAsync(room, false, token).ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            candidates = await _streams.ResolveAudioAsync(room, true, token).ConfigureAwait(false);
        }

        var stream = candidates.FirstOrDefault() ?? throw new InvalidOperationException("没有可用的音频流。");
        await _audio.PlayAsync(stream, token).ConfigureAwait(false);
        await _danmaku.ConnectAsync(room, token).ConfigureAwait(false);
        var historyWarning = string.Empty;
        try
        {
            _history?.RecordPlayback(room.RoomId, room.Anchor, room.Title, DateTimeOffset.Now);
        }
        catch (Exception exception)
        {
            historyWarning = $"（观看历史保存失败：{exception.ToDisplayText()}）";
        }

        StatusChanged?.Invoke(this, $"已启动 {stream.Protocol}/{stream.Format}，等待音频输出…{historyWarning}");
    }
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (Room is { } room)
        {
            await SwitchAsync(room.RoomId, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task StopAsync()
    {
        await StopCurrentAsync().ConfigureAwait(false);
        Room = null;
        StatusChanged?.Invoke(this, "已停止");
    }

    private async Task StopCurrentAsync()
    {
        _sessionLifetime?.Cancel();
        await _audio.StopAsync().ConfigureAwait(false);
        await _danmaku.DisconnectAsync().ConfigureAwait(false);
        _sessionLifetime?.Dispose();
        _sessionLifetime = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopCurrentAsync().ConfigureAwait(false);
        _audio.Dispose();
        await _danmaku.DisposeAsync().ConfigureAwait(false);
    }
}
