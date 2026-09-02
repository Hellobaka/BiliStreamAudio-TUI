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
    private const int MaximumDanmakuLength = 30;
    private const int SendCooldownSeconds = 3;
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
    private readonly GuiLabel _inputStatus;
    private readonly ObservableCollection<DanmakuListItem> _messageItems = [];
    private readonly List<PendingDanmaku> _pendingDanmaku = [];
    private string _sessionStatus = "已停止";
    private DateTimeOffset? _cooldownEndsAt;
    private object? _cooldownToken;

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
        _inputStatus = new GuiLabel
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(2)
        };
        Add(_header, _messages, _input, _inputStatus);
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
        _app.Invoke(() => AddMessageItem(value));
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
            showError(exception.ToDisplayText());
        }
    }

    private void ConfigureEvents(IDanmakuConnection danmaku)
    {
        danmaku.Received += (_, item) => _app.Invoke(() =>
        {
            if (!TryConfirmDanmaku(item))
            {
                AddMessageItem(FormatDanmaku(item.ReceivedAt, item.UserName, item.Message));
            }
        });
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
        _input.TextChanging += (_, args) =>
        {
            var proposedText = args.Result ?? string.Empty;
            if (CountDanmakuCharacters(proposedText) <= MaximumDanmakuLength)
            {
                return;
            }

            args.Result = TruncateDanmaku(proposedText);
            args.Handled = true;
        };
        _input.TextChanged += (_, _) => RefreshInputStatus();

        _input.KeyDown += (_, key) =>
        {
            if (key != Key.Enter)
            {
                return;
            }

            if (_session.Room is { } room
                && _auth.Current?.IsAuthenticated == true)
            {
                var value = _input.Text.ToString() ?? string.Empty;
                _input.Text = string.Empty;
                _input.HasFocus = true;
                StartSendCooldown();
                var pending = StartDanmakuSend(value);
                _ = SendDanmakuWithFeedbackAsync(room.RoomId, value, pending);
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
            AddMessage($"登录失败：{exception.ToDisplayText()}");
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

    private async Task SendDanmakuWithFeedbackAsync(long roomId, string text, PendingDanmaku pending)
    {
        try
        {
            await SendDanmakuAsync(roomId, text).ConfigureAwait(false);
            _app.Invoke(() => CompleteDanmakuSend(pending, success: true));
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Danmaku send failed");
            _app.Invoke(() => CompleteDanmakuSend(pending, success: false));
        }
    }

    private PendingDanmaku StartDanmakuSend(string text)
    {
        var pending = new PendingDanmaku(
            Guid.NewGuid(),
            text,
            _auth.Current?.UserName ?? "我",
            DateTimeOffset.Now);
        _pendingDanmaku.Add(pending);
        UpdateMessageItem(pending.Id, $"{FormatDanmaku(pending.SentAt, pending.UserName, text)} 发送中 {SendingFrames[0]}", addIfMissing: true);
        pending.AnimationToken = _app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
        {
            pending.FrameIndex = (pending.FrameIndex + 1) % SendingFrames.Length;
            UpdateMessageItem(
                pending.Id,
                $"{FormatDanmaku(pending.SentAt, pending.UserName, text)} 发送中 {SendingFrames[pending.FrameIndex]}");
            return true;
        });
        pending.ConfirmationExpiryToken = _app.AddTimeout(TimeSpan.FromSeconds(5), () =>
        {
            _pendingDanmaku.Remove(pending);
            return false;
        });
        return pending;
    }

    private void CompleteDanmakuSend(PendingDanmaku pending, bool success)
    {
        if (pending.IsConfirmed)
        {
            return;
        }

        StopDanmakuAnimation(pending);
        if (!success)
        {
            _pendingDanmaku.Remove(pending);
            if (pending.ConfirmationExpiryToken is { } expiryToken)
            {
                _app.RemoveTimeout(expiryToken);
            }
        }

        UpdateMessageItem(
            pending.Id,
            success
                ? FormatDanmaku(pending.SentAt, pending.UserName, pending.Text)
                : $"{FormatDanmaku(pending.SentAt, pending.UserName, pending.Text)} ❌ 发送失败");
    }

    private bool TryConfirmDanmaku(DanmakuEvent item)
    {
        var pending = _pendingDanmaku.FirstOrDefault(candidate =>
        {
            var elapsed = item.ReceivedAt - candidate.SentAt;
            return string.Equals(candidate.UserName, item.UserName, StringComparison.Ordinal)
                && string.Equals(candidate.Text, item.Message, StringComparison.Ordinal)
                && elapsed >= TimeSpan.Zero
                && elapsed <= TimeSpan.FromSeconds(5);
        });
        if (pending is null)
        {
            return false;
        }

        pending.IsConfirmed = true;
        StopDanmakuAnimation(pending);
        if (pending.ConfirmationExpiryToken is { } expiryToken)
        {
            _app.RemoveTimeout(expiryToken);
        }

        _pendingDanmaku.Remove(pending);
        UpdateMessageItem(pending.Id, FormatDanmaku(item.ReceivedAt, item.UserName, item.Message));
        return true;
    }

    private void StopDanmakuAnimation(PendingDanmaku pending)
    {
        if (pending.AnimationToken is { } token)
        {
            _app.RemoveTimeout(token);
            pending.AnimationToken = null;
        }
    }

    private void RefreshDanmakuInput()
    {
        var canSendDanmaku = _session.Room is not null && _auth.Current?.IsAuthenticated == true;
        _input.Enabled = canSendDanmaku && !IsSendCooldownActive;
        if (!canSendDanmaku)
        {
            _input.Text = DanmakuInputUnavailableText;
        }
        else if (_input.Text.ToString() == DanmakuInputUnavailableText)
        {
            _input.Text = string.Empty;
        }

        _input.SetNeedsDraw();
        RefreshInputStatus();
    }

    private void StartSendCooldown()
    {
        _cooldownEndsAt = DateTimeOffset.UtcNow.AddSeconds(SendCooldownSeconds);
        if (_cooldownToken is { } token)
        {
            _app.RemoveTimeout(token);
        }

        _cooldownToken = _app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
        {
            if (IsSendCooldownActive)
            {
                RefreshInputStatus();
                return true;
            }

            _cooldownEndsAt = null;
            _cooldownToken = null;
            RefreshDanmakuInput();
            if (_input.Enabled)
            {
                _input.HasFocus = true;
            }

            return false;
        });
        RefreshDanmakuInput();
    }

    private bool IsSendCooldownActive => _cooldownEndsAt is { } endsAt && endsAt > DateTimeOffset.UtcNow;

    private void RefreshInputStatus()
    {
        if (IsSendCooldownActive && _cooldownEndsAt is { } endsAt)
        {
            var remainingSeconds = Math.Max(
                1,
                (int)Math.Ceiling((endsAt - DateTimeOffset.UtcNow).TotalSeconds));
            _inputStatus.Text = $"发送冷却中：{remainingSeconds} 秒";
        }
        else if (_session.Room is not null && _auth.Current?.IsAuthenticated == true)
        {
            _inputStatus.Text = $"{CountDanmakuCharacters(_input.Text.ToString() ?? string.Empty)}/{MaximumDanmakuLength}";
        }
        else
        {
            _inputStatus.Text = string.Empty;
        }

        _inputStatus.SetNeedsDraw();
    }

    private static int CountDanmakuCharacters(string text) => text.EnumerateRunes().Count();

    private static string TruncateDanmaku(string text)
    {
        var result = new StringBuilder();
        foreach (var rune in text.EnumerateRunes().Take(MaximumDanmakuLength))
        {
            result.Append(rune);
        }

        return result.ToString();
    }

    private void AddMessageItem(string text) => UpdateMessageItem(Guid.NewGuid(), text, addIfMissing: true);

    private void UpdateMessageItem(Guid id, string text, bool addIfMissing = false)
    {
        for (var index = 0; index < _messageItems.Count; index++)
        {
            if (_messageItems[index].Id != id)
            {
                continue;
            }

            _messageItems[index] = new DanmakuListItem(id, text);
            _messages.SetNeedsDraw();
            return;
        }

        if (!addIfMissing)
        {
            return;
        }

        _messageItems.Add(new DanmakuListItem(id, text));
        while (_messageItems.Count > MaximumMessageCount)
        {
            _messageItems.RemoveAt(0);
        }

        _messages.MoveEnd(extend: false);
    }

    private static string FormatDanmaku(DateTimeOffset receivedAt, string userName, string message) =>
        $"[{receivedAt:HH:mm:ss}] {userName}: {message}";

    private sealed class DanmakuListItem(Guid id, string text)
    {
        public Guid Id { get; } = id;

        public override string ToString() => text;
    }

    private sealed class PendingDanmaku(Guid id, string text, string userName, DateTimeOffset sentAt)
    {
        public Guid Id { get; } = id;
        public string Text { get; } = text;
        public string UserName { get; } = userName;
        public DateTimeOffset SentAt { get; } = sentAt;
        public int FrameIndex { get; set; }
        public object? AnimationToken { get; set; }
        public object? ConfirmationExpiryToken { get; set; }
        public bool IsConfirmed { get; set; }
    }

    private static readonly string[] SendingFrames = ["|", "/", "-", "\\"];

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
