namespace BiliStreamAudio.Tui.Core;

public sealed record RoomReference(long RequestedId);

public sealed record LiveRoom(
    long RoomId,
    long ShortId,
    long Uid,
    string Title,
    string Anchor,
    bool IsLive);

/// <summary>
/// A live-room entry displayed in the browse page.  A room id can be zero for
/// an offline user returned from a user-name search.
/// </summary>
public sealed record LiveDirectoryEntry(
    long RoomId,
    long Uid,
    string Anchor,
    string Title,
    bool IsLive,
    DateTimeOffset? StartedAt,
    bool IsDirectRoomEntry = false);

public sealed record StreamDescriptor(
    Uri Url,
    string Protocol,
    string Format,
    int Quality,
    bool IsAudioOnly,
    long RoomId);

public sealed record AuthSession(
    IReadOnlyDictionary<string, string> Cookies,
    string? RefreshToken,
    long UserId,
    DateOnly? LastRefreshCheck,
    string? UserName = null)
{
    public bool IsAuthenticated => Cookies.ContainsKey("SESSDATA") && Cookies.ContainsKey("bili_jct");
}

public sealed record DanmakuEvent(
    string UserName,
    string Message,
    DateTimeOffset ReceivedAt,
    string Type = "DANMU_MSG");

public enum PlaybackState
{
    Stopped,
    Buffering,
    Playing,
    Error
}

public sealed record DanmakuServer(string Host, int WsPort, int WssPort, string Token);

public sealed record RefreshResult(bool Success, AuthSession? Session, string? Error)
{
    public static RefreshResult Failed(string message) => new(false, null, message);
}
