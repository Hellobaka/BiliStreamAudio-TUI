using System.Collections.ObjectModel;
using System.Text;
using BiliStreamAudio.Tui.Core;
using Serilog;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using GuiLabel = Terminal.Gui.Views.Label;
using GuiListView = Terminal.Gui.Views.ListView;

namespace BiliStreamAudio.Tui.Views;

internal sealed class LiveRoomWindow : Window
{
    private const int MaximumMessageCount = 500;
    private const string DanmakuInputUnavailableText = "请先在“浏览”页面选择直播间并登录，然后发送弹幕。";

    private readonly IApplication _app;
    private readonly RoomSession _session;
    private readonly IAuthService _auth;
    private readonly ITokenRefreshService _tokenRefresh;
    private readonly IAudioPlayer _audio;
    private readonly IDanmakuSender _sender;
    private readonly Action _refreshStatusBar;
    private readonly GuiLabel _header;
    private readonly GuiListView _messages;
    private readonly TextField _input;
    private readonly ObservableCollection<string> _messageItems = [];
    private string _sessionStatus = "已停止";

    public LiveRoomWindow(
        IApplication app,
        RoomSession session,
        IAuthService auth,
        ITokenRefreshService tokenRefresh,
        IAudioPlayer audio,
        IDanmakuConnection danmaku,
        IDanmakuSender sender,
        Action refreshStatusBar)
    {
        _app = app;
        _session = session;
        _auth = auth;
        _tokenRefresh = tokenRefresh;
        _audio = audio;
        _sender = sender;
        _refreshStatusBar = refreshStatusBar;

        Title = "直播间";
        _header = new GuiLabel
        {
            Text = "未选择直播间 · 已停止 · 未登录",
            HotKeySpecifier = new Rune(0xffff),
            X = 1,
            Y = 0,
            Width = Dim.Fill(2)
        };
        _messages = new GuiListView
        {
            X = 1,
            Y = 2,
            Width = Dim.Fill(2),
            Height = Dim.Fill(4)
        };
        _messages.SetSource(_messageItems);
        _input = new TextField
        {
            Text = "",
            X = 1,
            Y = Pos.AnchorEnd(2),
            Width = Dim.Fill(2)
        };
        Add(_header, _messages, _input);
        ConfigureEvents(danmaku);
        RefreshHeader();
    }

    public bool IsInputFocused => _input.HasFocus;

    public void RefreshHeader()
    {
        var roomStatus = _session.Room is { } room
            ? $"{room.RoomId} · {room.Anchor} · {room.Title}"
            : "未选择直播间";
        _header.Text = $"{roomStatus} · {_sessionStatus} · {CreateLoginStatus(_auth.Current)}";
        _header.SetNeedsDraw();
        RefreshDanmakuInput();
    }

    public void AddMessage(string value)
    {
        _app.Invoke(() =>
        {
            _messageItems.Add(value);
            while (_messageItems.Count > MaximumMessageCount)
            {
                _messageItems.RemoveAt(0);
            }

            _messages.MoveEnd(extend: false);
        });
    }

    internal static async Task RunUiTask(
        Func<Task> operation,
        Action<string> showError,
        Action? onSuccess = null)
    {
        try
        {
            await operation().ConfigureAwait(false);
            onSuccess?.Invoke();
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Operation failed");
            showError(exception.Message);
        }
    }

    private void ConfigureEvents(IDanmakuConnection danmaku)
    {
        danmaku.Received += (_, item) =>
            AddMessage($"[{item.ReceivedAt:HH:mm:ss}] {item.UserName}: {item.Message}");
        danmaku.StatusChanged += (_, status) => _app.Invoke(() =>
        {
            _sessionStatus = status;
            RefreshHeader();
        });
        _session.RoomChanged += (_, _) => _app.Invoke(RefreshHeader);
        _session.StatusChanged += (_, status) => _app.Invoke(() =>
        {
            _sessionStatus = status;
            RefreshHeader();
        });

        _input.KeyDown += (_, key) =>
        {
            if (key != Key.Enter)
            {
                return;
            }

            var value = _input.Text.ToString() ?? string.Empty;
            _input.Text = string.Empty;

            if (_session.Room is { } room && _auth.Current?.IsAuthenticated == true)
            {
                _ = RunUiTask(
                    () => SendDanmakuAsync(room.RoomId, value),
                    AddMessage);
            }
            else
            {
                RefreshDanmakuInput();
            }

            key.Handled = true;
        };

        KeyDown += (_, key) =>
        {
            if (key == Key.R || key == Key.R.WithShift)
            {
                _ = RunUiTask(
                    () => _session.RefreshAsync(CancellationToken.None),
                    AddMessage);
                key.Handled = true;
            }
            else if (key == Key.M || key == Key.M.WithShift)
            {
                _audio.ToggleMute();
                _refreshStatusBar();
                key.Handled = true;
            }
            else if (key == (Key)'+')
            {
                _audio.SetVolume(_audio.Volume + 5);
                _refreshStatusBar();
                key.Handled = true;
            }
            else if (key == (Key)'-')
            {
                _audio.SetVolume(_audio.Volume - 5);
                _refreshStatusBar();
                key.Handled = true;
            }
            else if (key == Key.L || key == Key.L.WithShift)
            {
                _ = LoginAsync();
                key.Handled = true;
            }
        };
    }

    private async Task LoginAsync()
    {
        try
        {
            await _auth.LoginAsync(CancellationToken.None).ConfigureAwait(false);
            AddMessage($"已登录：{_auth.Current?.UserName}");
            _app.Invoke(RefreshHeader);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Official login window failed");
            AddMessage($"登录失败：{exception.Message}");
        }
    }

    private async Task SendDanmakuAsync(long roomId, string text)
    {
        var current = _auth.Current ?? throw new InvalidOperationException("请先登录。");
        var result = await _tokenRefresh.RefreshIfNeededAsync(current, CancellationToken.None).ConfigureAwait(false);
        if (!result.Success || result.Session is null)
        {
            throw new InvalidOperationException(result.Error ?? "登录会话需要重新登录。");
        }

        await _auth.SaveAsync(result.Session, CancellationToken.None).ConfigureAwait(false);
        await _sender.SendAsync(roomId, text, result.Session, CancellationToken.None).ConfigureAwait(false);
    }

    private void RefreshDanmakuInput()
    {
        var isAvailable = _session.Room is not null && _auth.Current?.IsAuthenticated == true;
        _input.Enabled = isAvailable;
        _input.Text = isAvailable ? string.Empty : DanmakuInputUnavailableText;
        _input.SetNeedsDraw();
    }

    private static string CreateLoginStatus(AuthSession? session)
    {
        if (session?.IsAuthenticated != true)
        {
            return "未登录";
        }

        return string.IsNullOrWhiteSpace(session.UserName)
            ? "已登录"
            : $"已登录（{session.UserName}）";
    }
}
