using System.Net.WebSockets;
using System.Text.Json;
using BiliStreamAudio.Tui.Core;

namespace BiliStreamAudio.Tui.Infrastructure;

public sealed class DanmakuConnection(
    Func<AuthSession?> sessionProvider,
    Func<AuthSession?, BiliHttp>? httpFactory = null) : IDanmakuConnection
{
    private const string LiveOrigin = "https://live.bilibili.com";
    private const string NavigationEndpoint = "https://api.bilibili.com/x/web-interface/nav";

    private readonly Func<AuthSession?, BiliHttp> _httpFactory =
        httpFactory ?? (session => new BiliHttp(session));

    private CancellationTokenSource? _lifetime;
    private ClientWebSocket? _socket;
    private Task? _receiveTask;

    public event EventHandler<LiveEvent>? EventReceived;
    public event EventHandler<DanmakuEvent>? Received;
    public event EventHandler<string>? StatusChanged;

    public async Task ConnectAsync(LiveRoom room, CancellationToken cancellationToken)
    {
        await DisconnectAsync().ConfigureAwait(false);
        var authSession = sessionProvider();
        using var http = _httpFactory(authSession);
        var (imageKey, subKey) = await GetWbiKeysAsync(http, cancellationToken)
            .ConfigureAwait(false);
        var infoUrl = CreateDanmakuInfoUrl(room.RoomId, imageKey, subKey);
        using var info = await http
            .GetLiveJsonAsync(infoUrl, room.RoomId, cancellationToken)
            .ConfigureAwait(false);

        BiliJson.EnsureOk(info);

        var data = info.RootElement.GetProperty("data");
        var token = data.String("token");
        var servers = ParseServers(data, token);
        if (servers.Length == 0)
        {
            throw new InvalidOperationException("哔哩哔哩未返回弹幕服务器。");
        }

        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _receiveTask = RunReconnectLoopAsync(
            room.RoomId,
            servers,
            authSession,
            _lifetime.Token);
        await Task.Yield();
    }

    private async Task RunReconnectLoopAsync(
        long roomId,
        IReadOnlyList<DanmakuServer> servers,
        AuthSession? authSession,
        CancellationToken token)
    {
        for (var attempt = 0; !token.IsCancellationRequested; attempt++)
        {
            try
            {
                var server = servers[attempt % servers.Count];
                _socket = new ClientWebSocket();
                ConfigureWebSocket(_socket, authSession);
                var serverUri = new Uri($"wss://{server.Host}:{server.WssPort}/sub");
                await _socket.ConnectAsync(serverUri, token).ConfigureAwait(false);

                var authPayload = CreateAuthPayload(roomId, server.Token, authSession);
                var authFrame = DanmakuProtocol.Frame(7, authPayload);
                await _socket
                    .SendAsync(authFrame, WebSocketMessageType.Binary, true, token)
                    .ConfigureAwait(false);

                StatusChanged?.Invoke(this, "弹幕已连接");

                using var heartbeat = new PeriodicTimer(TimeSpan.FromSeconds(30));
                using var connectionLifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
                var heartbeatTask = HeartbeatAsync(_socket, heartbeat, connectionLifetime.Token);
                try
                {
                    await ReceiveAsync(_socket, token).ConfigureAwait(false);
                }
                finally
                {
                    await connectionLifetime.CancelAsync().ConfigureAwait(false);
                    await heartbeatTask.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                StatusChanged?.Invoke(this, "弹幕连接断开，正在重连");
            }
            finally
            {
                _socket?.Dispose();
                _socket = null;
            }

            var delaySeconds = Math.Min(30, Math.Pow(2, Math.Min(attempt, 5)));
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static async Task HeartbeatAsync(ClientWebSocket socket, PeriodicTimer timer, CancellationToken token)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                var heartbeatFrame = DanmakuProtocol.Frame(2, []);
                await socket
                    .SendAsync(heartbeatFrame, WebSocketMessageType.Binary, true, token)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Normal shutdown for the per-connection heartbeat loop.
        }
    }

    private async Task ReceiveAsync(ClientWebSocket socket, CancellationToken token)
    {
        using var stream = new MemoryStream();
        var buffer = new byte[32 * 1024];

        while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
        {
            var result = await socket.ReceiveAsync(buffer, token).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return;
            }

            stream.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage)
            {
                continue;
            }

            foreach (var item in DanmakuProtocol.Parse(stream.ToArray()))
            {
                EventReceived?.Invoke(this, item);
                if (item is DanmakuEvent danmaku)
                {
                    Received?.Invoke(this, danmaku);
                }
            }

            stream.SetLength(0);
        }
    }

    public async Task DisconnectAsync()
    {
        if (_lifetime is null)
        {
            return;
        }

        await _lifetime.CancelAsync().ConfigureAwait(false);
        if (_socket?.State == WebSocketState.Open)
        {
            await _socket
                .CloseOutputAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "switch room",
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        if (_receiveTask is not null)
        {
            try
            {
                await _receiveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when changing rooms or exiting the application.
            }
        }

        _lifetime.Dispose();
        _lifetime = null;
        _receiveTask = null;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
    }

    private static DanmakuServer[] ParseServers(JsonElement data, string token)
    {
        return data.GetProperty("host_list")
            .EnumerateArray()
            .Select(server => new DanmakuServer(
                server.String("host"),
                server.TryGetProperty("ws_port", out var wsPort) ? wsPort.GetInt32() : 2244,
                server.TryGetProperty("wss_port", out var wssPort) ? wssPort.GetInt32() : 443,
                token))
            .ToArray();
    }

    private static async Task<(string ImageKey, string SubKey)> GetWbiKeysAsync(
        BiliHttp http,
        CancellationToken cancellationToken)
    {
        using var navigation = await http
            .GetJsonAsync(NavigationEndpoint, cancellationToken)
            .ConfigureAwait(false);

        var data = navigation.RootElement.GetProperty("data");
        var wbiImages = data.GetProperty("wbi_img");
        var imageKey = ExtractFileName(wbiImages.String("img_url"));
        var subKey = ExtractFileName(wbiImages.String("sub_url"));
        if (string.IsNullOrEmpty(imageKey) || string.IsNullOrEmpty(subKey))
        {
            throw new InvalidOperationException("哔哩哔哩未返回 WBI 签名密钥。");
        }

        return (imageKey, subKey);
    }

    private static string CreateDanmakuInfoUrl(
        long roomId,
        string imageKey,
        string subKey)
    {
        KeyValuePair<string, string>[] parameters =
        [
            new("id", roomId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("type", "0"),
            new("web_location", "444.8")
        ];
        var signedQuery = WbiSigner.Sign(
            parameters,
            imageKey,
            subKey,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return "xlive/web-room/v1/index/getDanmuInfo?" + signedQuery;
    }

    private static string ExtractFileName(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? Path.GetFileNameWithoutExtension(uri.AbsolutePath)
            : string.Empty;
    }

    private static void ConfigureWebSocket(
        ClientWebSocket socket,
        AuthSession? session)
    {
        socket.Options.SetRequestHeader("User-Agent", BiliHttp.DesktopBrowserUserAgent);
        socket.Options.SetRequestHeader("Origin", LiveOrigin);
        socket.Options.SetRequestHeader("Referer", LiveOrigin + "/");
        socket.Options.SetRequestHeader("Accept-Language", "zh-CN");
        socket.Options.SetRequestHeader("Pragma", "no-cache");
        socket.Options.SetRequestHeader("Cache-Control", "no-cache");
        if (session is not null && session.Cookies.Count > 0)
        {
            var cookieHeader = string.Join(
                "; ",
                session.Cookies.Select(cookie => $"{cookie.Key}={cookie.Value}"));
            socket.Options.SetRequestHeader("Cookie", cookieHeader);
        }
    }

    private static byte[] CreateAuthPayload(
        long roomId,
        string token,
        AuthSession? session)
    {
        var buvid = session?.Cookies.GetValueOrDefault("buvid3") ?? string.Empty;
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            uid = session?.UserId ?? 0,
            roomid = roomId,
            protover = 3,
            buvid,
            platform = "web",
            type = 2,
            key = token
        });
    }
}
