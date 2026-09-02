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
    bool IsDirectRoomEntry = false,
    DateTimeOffset? WatchedAt = null);

public sealed record StreamDescriptor(
    Uri Url,
    string Protocol,
    string Format,
    int Quality,
    bool IsAudioOnly,
    long RoomId,
    string Codec = "",
    int? BitrateKbps = null);

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

public static class PlaybackStateExtensions
{
    public static string ToDisplayText(this PlaybackState state) => state switch
    {
        PlaybackState.Stopped => "已停止",
        PlaybackState.Buffering => "缓冲中",
        PlaybackState.Playing => "播放中",
        PlaybackState.Error => "播放错误",
        _ => "未知状态"
    };
}

public static class ExceptionExtensions
{
    public static string ToDisplayText(this Exception exception) => exception switch
    {
        OperationCanceledException => "操作已取消。",
        TimeoutException => "操作超时，请稍后重试。",
        HttpRequestException => "网络请求失败，请检查网络连接后重试。",
        System.Net.Sockets.SocketException => "网络连接失败，请检查网络连接后重试。",
        System.Net.WebSockets.WebSocketException => "弹幕连接失败，请稍后重试。",
        System.Text.Json.JsonException => "服务器返回的数据格式无效，请稍后重试。",
        System.Security.Cryptography.CryptographicException => "本地登录信息无法读取，请重新登录。",
        UnauthorizedAccessException => "没有访问本地文件的权限。",
        IOException => "读取或写入本地文件失败，请检查磁盘空间和文件权限。",
        ArgumentException => RemoveParameterName(exception.Message),
        _ when ContainsChinese(exception.Message) => exception.Message,
        _ => "操作失败，请稍后重试。"
    };

    private static string RemoveParameterName(string message)
    {
        const string parameterPrefix = " (Parameter '";
        var parameterIndex = message.IndexOf(parameterPrefix, StringComparison.Ordinal);
        return parameterIndex >= 0 ? message[..parameterIndex] : message;
    }

    private static bool ContainsChinese(string value) => value.Any(character =>
        character is >= '\u3400' and <= '\u4dbf'
        or >= '\u4e00' and <= '\u9fff');
}

/// <summary>
/// 一条直播间播放历史记录。每个直播间只保留最近一次观看的记录。
/// </summary>
public sealed record PlaybackHistoryEntry(
    long RoomId,
    string Anchor,
    string Title,
    DateTimeOffset WatchedAt);

public sealed record DanmakuServer(string Host, int WsPort, int WssPort, string Token);

public sealed record RefreshResult(bool Success, AuthSession? Session, string? Error)
{
    public static RefreshResult Failed(string message) => new(false, null, message);
}
