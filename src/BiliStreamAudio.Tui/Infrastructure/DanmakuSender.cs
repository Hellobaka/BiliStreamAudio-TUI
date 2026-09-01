using BiliStreamAudio.Tui.Core;

namespace BiliStreamAudio.Tui.Infrastructure;

public sealed class DanmakuSender(Func<AuthSession, BiliHttp>? httpFactory = null) : IDanmakuSender
{
    private const int MaximumMessageLength = 20;
    private const int MaximumMessagesPerWindow = 5;
    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromSeconds(30);

    private readonly Queue<DateTimeOffset> _sent = new();
    private readonly Func<AuthSession, BiliHttp> _httpFactory = httpFactory ?? (session => new BiliHttp(session));

    public async Task SendAsync(long roomId, string message, AuthSession session, CancellationToken cancellationToken)
    {
        if (!session.IsAuthenticated)
        {
            throw new InvalidOperationException("请先登录。");
        }

        if (string.IsNullOrWhiteSpace(message) || message.Length > MaximumMessageLength)
        {
            throw new ArgumentException("弹幕长度须为 1–20 个字符。", nameof(message));
        }

        var now = DateTimeOffset.UtcNow;
        while (_sent.Count > 0 && now - _sent.Peek() > RateLimitWindow)
        {
            _sent.Dequeue();
        }

        if (_sent.Count >= MaximumMessagesPerWindow)
        {
            throw new InvalidOperationException("发送过于频繁，请稍后再试。");
        }

        var csrf = session.Cookies["bili_jct"];
        KeyValuePair<string, string>[] form =
        [
            new("bubble", "0"),
            new("msg", message),
            new("color", "16777215"),
            new("mode", "1"),
            new("fontsize", "25"),
            new("rnd", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()),
            new("roomid", roomId.ToString()),
            new("csrf", csrf),
            new("csrf_token", csrf)
        ];

        using var http = _httpFactory(session);
        using var response = await http
            .PostLiveFormAsync(
                "https://api.live.bilibili.com/msg/send",
                roomId,
                form,
                cancellationToken)
            .ConfigureAwait(false);

        BiliJson.EnsureOk(response);
        _sent.Enqueue(now);
    }
}
