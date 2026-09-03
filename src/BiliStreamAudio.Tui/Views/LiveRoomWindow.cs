using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text;
using BiliStreamAudio.Tui.Core;
using BiliStreamAudio.Tui.Infrastructure;
using Serilog;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Text;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using GuiAttribute = Terminal.Gui.Drawing.Attribute;
using GuiButton = Terminal.Gui.Views.Button;
using GuiColor = Terminal.Gui.Drawing.Color;
using GuiLabel = Terminal.Gui.Views.Label;
using GuiLine = Terminal.Gui.Views.Line;
using GuiListView = Terminal.Gui.Views.ListView;
using GuiView = Terminal.Gui.ViewBase.View;

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
    private readonly IHistoryStore _history;
    private readonly bool _mockMode;
    private readonly LiveRoomDisplayOptions _displayOptions;
    private readonly Action _refreshStatusBar;
    private static readonly GuiAttribute GiftMessageAttribute = new(new GuiColor("#8ED8FF"), GuiColor.None);
    private static readonly Scheme SuperChatScrollButtonScheme = new(
        new GuiAttribute(GuiColor.White, GuiColor.None))
    {
        Disabled = new GuiAttribute(GuiColor.DarkGray, GuiColor.None)
    };
    private readonly GuiLabel _header;
    private readonly GuiView _superChatTray;
    private readonly GuiButton _scrollSuperChatsLeft;
    private readonly GuiButton _scrollSuperChatsRight;
    private readonly GuiView _superChatCapsules;
    private readonly GuiLine _superChatSeparator;
    private readonly ExpandedSuperChatCardView _expandedSuperChatCard;
    private readonly GuiListView _messages;
    private readonly TextField _input;
    private readonly GuiLabel _inputStatus;
    private readonly ObservableCollection<DanmakuListItem> _messageItems = [];
    private readonly List<PendingDanmaku> _pendingDanmaku = [];
    private readonly List<ActiveSuperChat> _activeSuperChats = [];
    private readonly HashSet<string> _seenSuperChatIds = new(StringComparer.Ordinal);
    private long? _currentRoomId;
    private string _sessionStatus = "已停止";
    private DateTimeOffset? _cooldownEndsAt;
    private object? _cooldownToken;
    private List<string> _danmakuHistory = [];
    private int _danmakuHistoryIndex = -1;
    private string? _danmakuHistoryDraft;
    private int _firstVisibleSuperChat;
    private int _lastVisibleSuperChat = -1;
    private object? _superChatTimerToken;
    private SuperChatEvent? _expandedSuperChat;

    public LiveRoomWindow(
        IApplication app,
        RoomSession session,
        IAuthService auth,
        ITokenRefreshService tokenRefresh,
        IAudioPlayer audio,
        IDanmakuConnection danmaku,
        IDanmakuSender sender,
        IHistoryStore history,
        bool mockMode,
        LiveRoomDisplayOptions displayOptions,
        Action refreshStatusBar)
    {
        _app = app;
        _session = session;
        _auth = auth;
        _tokenRefresh = tokenRefresh;
        _audio = audio;
        _sender = sender;
        _history = history;
        _mockMode = mockMode;
        _displayOptions = displayOptions;
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
        _superChatTray = new GuiView
        {
            X = 1,
            Y = 2,
            Width = Dim.Fill(2),
            Height = 2,
            Visible = false
        };
        _scrollSuperChatsLeft = new GuiButton
        {
            Text = "◀",
            X = 0,
            Y = 0,
            Width = 3,
            NoDecorations = true,
            NoPadding = true,
            BorderStyle = LineStyle.None,
            ShadowStyle = ShadowStyles.None,
            Enabled = false
        };
        _scrollSuperChatsLeft.SetScheme(SuperChatScrollButtonScheme);
        _superChatCapsules = new GuiView
        {
            X = 4,
            Y = 0,
            Width = Dim.Fill(4),
            Height = 1
        };
        _scrollSuperChatsRight = new GuiButton
        {
            Text = "▶",
            X = Pos.AnchorEnd(3),
            Y = 0,
            Width = 3,
            NoDecorations = true,
            NoPadding = true,
            BorderStyle = LineStyle.None,
            ShadowStyle = ShadowStyles.None,
            Enabled = false
        };
        _scrollSuperChatsRight.SetScheme(SuperChatScrollButtonScheme);
        _superChatSeparator = new GuiLine
        {
            X = 0,
            Y = 1,
            Style = LineStyle.Single
        };
        _superChatTray.Add(
            _scrollSuperChatsLeft,
            _superChatCapsules,
            _scrollSuperChatsRight,
            _superChatSeparator);
        _superChatCapsules.ViewportChanged += (_, _) =>
        {
            if (_activeSuperChats.Count > 0)
            {
                RenderSuperChatCapsules();
            }
        };
        _expandedSuperChatCard = new ExpandedSuperChatCardView(HideExpandedSuperChat)
        {
            X = 1,
            Y = 4,
            Width = Dim.Fill(2),
            Height = 3,
            Visible = false
        };
        _messages = new GuiListView
        {
            X = 1,
            Y = 2,
            Width = Dim.Fill(2),
            Height = Dim.Fill(3),
            ViewportSettings = ViewportSettingsFlags.HasVerticalScrollBar
        };
        _messages.Source = new DanmakuListDataSource(_messageItems, () => _displayOptions.ShowFanMedals);
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
        Add(_header, _superChatTray, _expandedSuperChatCard, _messages, _input, _inputStatus);
        ViewportChanged += (_, _) => RefreshExpandedSuperChatCard();
        _app.Mouse.MouseEvent += (_, mouse) =>
        {
            if (_expandedSuperChat is not null
                && mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked)
                && mouse.View is not SuperChatCapsuleView)
            {
                HideExpandedSuperChat();
            }
        };
        ConfigureEvents(danmaku);
        RefreshHeader();
    }

    public bool IsInputFocused => _input.HasFocus;

    public void FocusInput() => _input.SetFocus();

    public void RefreshDanmakuRendering() => _messages.SetNeedsDraw();

    private void OnRoomChanged(LiveRoom room)
    {
        if (_currentRoomId is { } previousRoomId && previousRoomId != room.RoomId)
        {
            ClearDanmakuList();
        }

        _currentRoomId = room.RoomId;
        LoadDanmakuHistory(room.RoomId);
        RefreshHeader();
        _input.SetFocus();
    }

    private void LoadDanmakuHistory(long roomId)
    {
        try
        {
            _danmakuHistory = _history.GetDanmakuHistory(roomId).ToList();
        }
        catch
        {
            _danmakuHistory = [];
        }

        _danmakuHistoryIndex = -1;
        _danmakuHistoryDraft = null;
    }

    private void ClearDanmakuList()
    {
        foreach (var pending in _pendingDanmaku)
        {
            StopDanmakuAnimation(pending);
            if (pending.ConfirmationExpiryToken is { } expiryToken)
            {
                _app.RemoveTimeout(expiryToken);
            }
        }

        _pendingDanmaku.Clear();
        ClearSuperChats();
        _messageItems.Clear();
        _messages.SetNeedsDraw();
    }

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
        danmaku.EventReceived += (_, item) =>
        {
            if (item is SuperChatEvent or SuperChatDeleteEvent or GiftEvent)
            {
                _app.Invoke(() => HandleLiveEvent(item));
            }
        };
        danmaku.Received += (_, item) => _app.Invoke(() =>
        {
            if (!TryConfirmDanmaku(item))
            {
                AddMessageItem(
                    FormatDanmaku(item.ReceivedAt, item.UserName, item.Message),
                    danmaku: item);
            }
        });
        danmaku.StatusChanged += (_, status) => _app.Invoke(() =>
        {
            _sessionStatus = status;
            RefreshHeader();
        });
        _session.RoomChanged += (_, room) => _app.Invoke(() => OnRoomChanged(room));
        _session.StatusChanged += (_, status) => _app.Invoke(() =>
        {
            _sessionStatus = status;
            RefreshHeader();
        });
        _input.TextChanging += (_, args) =>
        {
            var proposedText = args.Result ?? string.Empty;
            if (_mockMode && IsMockLiveEventCommand(proposedText))
            {
                return;
            }

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
            if (key == Key.Esc && _expandedSuperChat is not null)
            {
                HideExpandedSuperChat();
                key.Handled = true;
                return;
            }

            if (key == Key.CursorUp || key == Key.CursorDown)
            {
                NavigateDanmakuHistory(key == Key.CursorUp ? -1 : 1);
                key.Handled = true;
                return;
            }

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
                if (_mockMode && IsMockLiveEventCommand(value))
                {
                    _ = SendMockLiveEventAsync(room.RoomId, value);
                    key.Handled = true;
                    return;
                }

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

        _scrollSuperChatsLeft.Accepted += (_, _) => ScrollSuperChats(-1);
        _scrollSuperChatsRight.Accepted += (_, _) => ScrollSuperChats(1);
        _messages.Accepted += (_, _) =>
        {
            if (_messages.SelectedItem is { } index
                && index >= 0
                && index < _messageItems.Count
                && _messageItems[index].SuperChat is { } superChat)
            {
                ShowExpandedSuperChat(superChat);
            }
        };

        KeyDown += (_, key) =>
        {
            if (key == Key.Esc && _expandedSuperChat is not null)
            {
                HideExpandedSuperChat();
                key.Handled = true;
            }
            else if (key == Key.R || key == Key.R.WithShift)
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

    private async Task SendMockLiveEventAsync(long roomId, string text)
    {
        try
        {
            await SendDanmakuAsync(roomId, text).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Mock live event send failed");
            AddMessage($"模拟事件失败：{exception.ToDisplayText()}");
        }
    }

    private async Task SendDanmakuWithFeedbackAsync(long roomId, string text, PendingDanmaku pending)
    {
        try
        {
            await SendDanmakuAsync(roomId, text).ConfigureAwait(false);
            try
            {
                _history.RecordDanmakuSent(roomId, text, DateTimeOffset.Now);
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Failed to save danmaku history");
            }

            _app.Invoke(() =>
            {
                CompleteDanmakuSend(pending, success: true);
                LoadDanmakuHistory(roomId);
            });
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Danmaku send failed");
            _app.Invoke(() => CompleteDanmakuSend(pending, success: false));
        }
    }

    /// <summary>
    /// 使用键盘上下键在当前直播间的弹幕发送历史中切换。
    /// 返回 true 表示已处理该按键（应阻止默认行为）。
    /// </summary>
    private bool NavigateDanmakuHistory(int direction)
    {
        if (_danmakuHistory.Count == 0)
        {
            return false;
        }

        var nextIndex = GetNextDanmakuHistoryIndex(
            _danmakuHistoryIndex,
            _danmakuHistory.Count,
            direction);
        if (_danmakuHistoryIndex < 0)
        {
            if (nextIndex < 0)
            {
                return false;
            }

            // 首次进入历史导航：保存当前输入，以便按 Down 回到原始内容。
            _danmakuHistoryDraft = _input.Text.ToString() ?? string.Empty;
        }

        _danmakuHistoryIndex = nextIndex;
        if (_danmakuHistoryIndex < 0)
        {
            _input.Text = _danmakuHistoryDraft ?? string.Empty;
            RefreshInputStatus();
            return true;
        }

        _input.Text = _danmakuHistory[_danmakuHistoryIndex];
        _input.InsertionPoint = _input.Text.Length;
        RefreshInputStatus();
        return true;
    }

    internal static int GetNextDanmakuHistoryIndex(int currentIndex, int count, int direction)
    {
        if (count <= 0 || currentIndex < 0 && direction > 0)
        {
            return -1;
        }

        if (currentIndex < 0)
        {
            return 0;
        }

        return direction < 0
            ? Math.Min(currentIndex + 1, count - 1)
            : currentIndex - 1;
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
        UpdateMessageItem(
            pending.Id,
            FormatDanmaku(item.ReceivedAt, item.UserName, item.Message),
            danmaku: item);
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
            var inputText = _input.Text.ToString() ?? string.Empty;
            _inputStatus.Text = _mockMode && MockSuperChatCommand.IsCommand(inputText)
                ? "Mock SC：sc:<金额> <正文>"
                : _mockMode && MockGiftCommand.IsCommand(inputText)
                    ? "Mock 礼物：gift <金额> <个数> <描述>"
                    : _mockMode && MockFanMedalCommand.IsCommand(inputText)
                        ? "Mock 勋章：badge <等级> <名称>"
                    : $"{CountDanmakuCharacters(inputText)}/{MaximumDanmakuLength}";
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

    private void HandleLiveEvent(LiveEvent item)
    {
        switch (item)
        {
            case SuperChatEvent superChat:
                AddSuperChat(superChat);
                break;
            case SuperChatDeleteEvent deleted:
                RemoveSuperChatCapsules(deleted.Ids);
                break;
            case GiftEvent gift:
                AddGiftMessage(gift);
                break;
        }
    }

    private void AddGiftMessage(GiftEvent gift)
    {
        AddMessageItem(FormatGiftMessage(gift, _displayOptions.ShowGiftAmount), isGift: true);
    }

    private void AddSuperChat(SuperChatEvent superChat)
    {
        if (!string.IsNullOrEmpty(superChat.Id) && !_seenSuperChatIds.Add(superChat.Id))
        {
            return;
        }

        AddSuperChatCard(superChat);
        var lifetime = SuperChatPresentation.GetLifetime(superChat.PriceCny);
        if (lifetime <= TimeSpan.Zero)
        {
            return;
        }

        var displayedAt = DateTimeOffset.Now;
        _activeSuperChats.Add(new ActiveSuperChat(
            superChat,
            displayedAt,
            displayedAt.Add(lifetime)));
        UpdateSuperChatLayout();
        EnsureNewestSuperChatsVisible();
        RenderSuperChatCapsules();
        StartSuperChatTimer();
    }

    private void AddSuperChatCard(SuperChatEvent superChat)
    {
        var width = _messages.Viewport.Width > 16 ? _messages.Viewport.Width : 60;
        var tier = SuperChatPresentation.GetTier(superChat.PriceCny);
        foreach (var line in FormatSuperChatCard(superChat, width))
        {
            _messageItems.Add(new DanmakuListItem(
                Guid.NewGuid(),
                line,
                superChat,
                tier));
        }

        while (_messageItems.Count > MaximumMessageCount)
        {
            _messageItems.RemoveAt(0);
        }

        _messages.MoveEnd(extend: false);
    }

    internal static IReadOnlyList<string> FormatSuperChatCard(
        SuperChatEvent superChat,
        int availableWidth)
    {
        var width = Math.Max(16, availableWidth);
        var userName = string.IsNullOrWhiteSpace(superChat.UserName)
            ? "匿名用户"
            : superChat.UserName;
        var heading = $" SC ¥{superChat.PriceCny} · {userName} ";
        var lines = new List<string>
        {
            $"┌{FitToColumns(heading, width - 2, '─')}┐"
        };
        foreach (var contentLine in WrapText(superChat.Message, width - 4))
        {
            lines.Add($"│ {FitToColumns(contentLine, width - 4, ' ')} │");
        }
        lines.Add($"└{new string('─', width - 2)}┘");
        return lines;
    }

    internal static string FormatSuperChatDetails(SuperChatEvent superChat)
    {
        var userName = string.IsNullOrWhiteSpace(superChat.UserName)
            ? "匿名用户"
            : superChat.UserName;
        return $"发送人：{userName}\n金额：¥{superChat.PriceCny}\n\n{superChat.Message}";
    }

    private static IReadOnlyList<string> WrapText(string text, int width)
    {
        width = Math.Max(1, width);
        var lines = new List<string>();
        var current = new StringBuilder();
        var currentWidth = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value == '\r')
            {
                continue;
            }
            if (rune.Value == '\n')
            {
                lines.Add(current.ToString());
                current.Clear();
                currentWidth = 0;
                continue;
            }

            var runeWidth = Math.Max(1, rune.GetColumns());
            if (currentWidth > 0 && currentWidth + runeWidth > width)
            {
                lines.Add(current.ToString());
                current.Clear();
                currentWidth = 0;
            }

            current.Append(rune);
            currentWidth += runeWidth;
        }

        if (current.Length > 0 || lines.Count == 0)
        {
            lines.Add(current.ToString());
        }

        return lines;
    }

    private static string FitToColumns(string value, int width, char padding)
    {
        var result = new StringBuilder();
        var used = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var runeWidth = Math.Max(1, rune.GetColumns());
            if (used + runeWidth > width)
            {
                break;
            }

            result.Append(rune);
            used += runeWidth;
        }

        if (used < width)
        {
            result.Append(padding, width - used);
        }

        return result.ToString();
    }

    private void RemoveSuperChatCapsules(IReadOnlyList<string> ids)
    {
        if (ids.Count == 0)
        {
            return;
        }

        var deletedIds = ids.ToHashSet(StringComparer.Ordinal);
        if (_activeSuperChats.RemoveAll(item => deletedIds.Contains(item.SuperChat.Id)) == 0)
        {
            return;
        }

        ClampSuperChatViewport();
        RenderSuperChatCapsules();
    }

    private void StartSuperChatTimer()
    {
        if (_superChatTimerToken is not null)
        {
            return;
        }

        _superChatTimerToken = _app.AddTimeout(TimeSpan.FromSeconds(1), () =>
        {
            var now = DateTimeOffset.Now;
            var removed = _activeSuperChats.RemoveAll(item => item.ExpiresAt <= now) > 0;
            if (removed)
            {
                ClampSuperChatViewport();
                RenderSuperChatCapsules();
            }
            else
            {
                foreach (var item in _activeSuperChats)
                {
                    item.Capsule?.SetRemainingFraction(
                        SuperChatPresentation.GetRemainingFraction(
                            now,
                            item.DisplayedAt,
                            item.ExpiresAt));
                }
            }

            if (_activeSuperChats.Count > 0)
            {
                return true;
            }

            _superChatTimerToken = null;
            return false;
        });
    }

    private void ClearSuperChats()
    {
        if (_superChatTimerToken is { } timerToken)
        {
            _app.RemoveTimeout(timerToken);
            _superChatTimerToken = null;
        }

        _activeSuperChats.Clear();
        _seenSuperChatIds.Clear();
        _expandedSuperChat = null;
        _expandedSuperChatCard.Clear();
        _firstVisibleSuperChat = 0;
        _lastVisibleSuperChat = -1;
        _superChatCapsules.RemoveAll();
        _scrollSuperChatsLeft.Enabled = false;
        _scrollSuperChatsRight.Enabled = false;
        UpdateSuperChatLayout();
        _superChatCapsules.SetNeedsDraw();
    }

    private void ScrollSuperChats(int direction)
    {
        if (direction < 0 && _firstVisibleSuperChat > 0)
        {
            _firstVisibleSuperChat--;
        }
        else if (direction > 0 && _lastVisibleSuperChat < _activeSuperChats.Count - 1)
        {
            _firstVisibleSuperChat++;
        }

        RenderSuperChatCapsules();
    }

    private void EnsureNewestSuperChatsVisible()
    {
        var availableWidth = GetCapsuleHostWidth();
        var start = _activeSuperChats.Count - 1;
        var used = GetCapsuleWidth(_activeSuperChats[start].SuperChat);
        while (start > 0)
        {
            var previousWidth = GetCapsuleWidth(_activeSuperChats[start - 1].SuperChat);
            if (used + 1 + previousWidth > availableWidth)
            {
                break;
            }

            start--;
            used += 1 + previousWidth;
        }

        _firstVisibleSuperChat = Math.Max(0, start);
    }

    private void ClampSuperChatViewport()
    {
        _firstVisibleSuperChat = _activeSuperChats.Count == 0
            ? 0
            : Math.Clamp(_firstVisibleSuperChat, 0, _activeSuperChats.Count - 1);
    }

    private void RenderSuperChatCapsules()
    {
        UpdateSuperChatLayout();
        _superChatCapsules.RemoveAll();
        foreach (var item in _activeSuperChats)
        {
            item.Capsule = null;
        }

        ClampSuperChatViewport();
        var availableWidth = GetCapsuleHostWidth();
        var x = 0;
        _lastVisibleSuperChat = _firstVisibleSuperChat - 1;
        var now = DateTimeOffset.Now;
        for (var index = _firstVisibleSuperChat; index < _activeSuperChats.Count; index++)
        {
            var active = _activeSuperChats[index];
            var width = Math.Min(GetCapsuleWidth(active.SuperChat), availableWidth - x);
            if (width < 4 || x > 0 && width < GetCapsuleWidth(active.SuperChat))
            {
                break;
            }

            var capsule = new SuperChatCapsuleView(
                active.SuperChat,
                GetSuperChatPalette(SuperChatPresentation.GetTier(active.SuperChat.PriceCny)),
                () => ShowExpandedSuperChat(active.SuperChat))
            {
                X = x,
                Y = 0,
                Width = width,
                Height = 1
            };
            capsule.SetRemainingFraction(SuperChatPresentation.GetRemainingFraction(
                now,
                active.DisplayedAt,
                active.ExpiresAt));
            active.Capsule = capsule;
            _superChatCapsules.Add(capsule);
            _lastVisibleSuperChat = index;
            x += width + 1;
            if (x >= availableWidth)
            {
                break;
            }
        }

        _scrollSuperChatsLeft.Enabled = _firstVisibleSuperChat > 0;
        _scrollSuperChatsRight.Enabled =
            _lastVisibleSuperChat >= 0
            && _lastVisibleSuperChat < _activeSuperChats.Count - 1;
        _superChatCapsules.SetNeedsDraw();
    }

    private void UpdateSuperChatLayout()
    {
        var hasActiveSuperChats = _activeSuperChats.Count > 0;
        var hasExpandedSuperChat = _expandedSuperChat is not null;
        var expandedCardHeight = hasExpandedSuperChat ? _expandedSuperChatCard.CardHeight : 0;
        var superChatTrayHeight = 2 + expandedCardHeight;
        _superChatTray.Visible = hasActiveSuperChats;
        _superChatTray.Height = superChatTrayHeight;
        _superChatSeparator.Y = superChatTrayHeight - 1;
        _expandedSuperChatCard.Visible = hasExpandedSuperChat;
        _expandedSuperChatCard.Y = hasActiveSuperChats ? 3 : 2;
        _messages.Y = hasActiveSuperChats
            ? 2 + superChatTrayHeight
            : 2 + expandedCardHeight;
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private int GetCapsuleHostWidth()
    {
        var width = _superChatCapsules.Viewport.Width;
        return Math.Max(4, width > 0 ? width : _superChatCapsules.Frame.Width);
    }

    private static int GetCapsuleWidth(SuperChatEvent superChat) =>
        Math.Max(8, $"¥{superChat.PriceCny}".GetColumns() + 4);

    private void ShowExpandedSuperChat(SuperChatEvent superChat)
    {
        if (ReferenceEquals(_expandedSuperChat, superChat))
        {
            HideExpandedSuperChat();
            return;
        }

        _expandedSuperChat = superChat;
        RefreshExpandedSuperChatCard();
    }

    private void HideExpandedSuperChat()
    {
        if (_expandedSuperChat is null)
        {
            return;
        }

        _expandedSuperChat = null;
        _expandedSuperChatCard.Clear();
        UpdateSuperChatLayout();
        _input.SetFocus();
    }

    private void RefreshExpandedSuperChatCard()
    {
        if (_expandedSuperChat is not { } superChat)
        {
            return;
        }

        var width = _expandedSuperChatCard.Viewport.Width > 16
            ? _expandedSuperChatCard.Viewport.Width
            : _messages.Viewport.Width > 16
                ? _messages.Viewport.Width
                : 60;
        var cardY = _activeSuperChats.Count > 0 ? 4 : 2;
        var maximumHeight = Math.Max(3, Math.Min(10, Viewport.Height - cardY - 8));
        _expandedSuperChatCard.SetSuperChat(superChat, width, maximumHeight);
        UpdateSuperChatLayout();
    }

    private static SuperChatPalette GetSuperChatPalette(SuperChatTier tier) => tier switch
    {
        SuperChatTier.LightBlue => new(new GuiColor("#347FA8"), new GuiColor("#A8DCF5")),
        SuperChatTier.Cyan => new(new GuiColor("#008B8B"), new GuiColor("#8DE5DF")),
        SuperChatTier.Gold => new(new GuiColor("#A66F00"), new GuiColor("#F2D06B")),
        SuperChatTier.Red => new(new GuiColor("#B3262E"), new GuiColor("#F59AA0")),
        _ => new(new GuiColor("#347FA8"), new GuiColor("#A8DCF5"))
    };

    private void AddMessageItem(
        string text,
        bool isGift = false,
        DanmakuEvent? danmaku = null) =>
        UpdateMessageItem(Guid.NewGuid(), text, isGift, addIfMissing: true, danmaku: danmaku);

    private void UpdateMessageItem(
        Guid id,
        string text,
        bool isGift = false,
        bool addIfMissing = false,
        DanmakuEvent? danmaku = null)
    {
        for (var index = 0; index < _messageItems.Count; index++)
        {
            if (_messageItems[index].Id != id)
            {
                continue;
            }

            var existing = _messageItems[index];
            _messageItems[index] = new DanmakuListItem(
                id,
                text,
                existing.SuperChat,
                existing.SuperChatTier,
                existing.IsGift,
                danmaku ?? existing.Danmaku);
            _messages.SetNeedsDraw();
            return;
        }

        if (!addIfMissing)
        {
            return;
        }

        _messageItems.Add(new DanmakuListItem(id, text, danmaku: danmaku, isGift: isGift));
        while (_messageItems.Count > MaximumMessageCount)
        {
            _messageItems.RemoveAt(0);
        }

        _messages.MoveEnd(extend: false);
    }

    private static string FormatDanmaku(DateTimeOffset receivedAt, string userName, string message) =>
        $"[{receivedAt:HH:mm:ss}] {userName}: {message}";

    internal static string FormatGiftMessage(GiftEvent gift, bool showAmount = false)
    {
        var userName = string.IsNullOrWhiteSpace(gift.UserName) ? "匿名用户" : gift.UserName;
        var giftName = string.IsNullOrWhiteSpace(gift.GiftName) ? "礼物" : gift.GiftName;
        var count = Math.Max(1, gift.Count);
        var countText = count == 1 ? string.Empty : $" x{count}";
        var amountText = showAmount && gift.IsPaid
            ? $" ￥{gift.AmountCny:0.##}"
            : string.Empty;
        return $"✨ {userName}送出了{giftName}{countText}。{amountText}";
    }

    private static bool IsMockLiveEventCommand(string value) =>
        MockSuperChatCommand.IsCommand(value)
        || MockGiftCommand.IsCommand(value)
        || MockFanMedalCommand.IsCommand(value);

    private sealed class DanmakuListDataSource : IListDataSource
    {
        private readonly ObservableCollection<DanmakuListItem> _items;
        private readonly Func<bool> _showFanMedals;
        private readonly NotifyCollectionChangedEventHandler _itemsChanged;

        public DanmakuListDataSource(
            ObservableCollection<DanmakuListItem> items,
            Func<bool> showFanMedals)
        {
            _items = items;
            _showFanMedals = showFanMedals;
            _itemsChanged = (_, args) =>
            {
                if (!SuspendCollectionChangedEvent)
                {
                    CollectionChanged?.Invoke(this, args);
                }
            };
            _items.CollectionChanged += _itemsChanged;
        }

        public event NotifyCollectionChangedEventHandler? CollectionChanged;

        public int Count => _items.Count;

        public int MaxItemLength => _items.Count == 0
            ? 0
            : _items.Max(item => item.ToString().GetColumns());

        public bool SuspendCollectionChangedEvent { get; set; }

        public bool IsMarked(int item) => false;

        public void SetMark(int item, bool value)
        {
        }

        public System.Collections.IList ToList() => _items;

        public void Dispose() => _items.CollectionChanged -= _itemsChanged;

        public bool RenderMark(
            GuiListView listView,
            int item,
            int row,
            bool isMarked,
            bool markMultiple) => false;

        public void Render(
            GuiListView listView,
            bool selected,
            int item,
            int col,
            int row,
            int width,
            int viewportX)
        {
            var message = _items[item];
            var rowAttribute = GetRowAttribute(listView, message, selected);
            if (!_showFanMedals()
                || message.Danmaku?.Medal is not { } medal)
            {
                DrawSegment(listView, col, row, message.ToString(), rowAttribute);
                return;
            }

            var danmaku = message.Danmaku;
            DrawSegment(listView, col, row, $"[{danmaku.ReceivedAt:HH:mm:ss}] ", rowAttribute);
            var medalText = FanMedalPresentation.GetDisplayText(medal.Level, medal.Name);
            DrawSegment(listView, col + $"[{danmaku.ReceivedAt:HH:mm:ss}] ".GetColumns(), row, medalText,
                new GuiAttribute(GuiColor.White, new GuiColor(FanMedalPresentation.GetBackgroundColor(medal.Level)), TextStyle.Bold));
            DrawSegment(
                listView,
                col + $"[{danmaku.ReceivedAt:HH:mm:ss}] ".GetColumns() + medalText.GetColumns(),
                row,
                $" {danmaku.UserName}: {danmaku.Message}",
                rowAttribute);
        }

        private static GuiAttribute GetRowAttribute(
            GuiListView listView,
            DanmakuListItem message,
            bool selected)
        {
            if (selected)
            {
                return listView.GetAttributeForRole(VisualRole.Active);
            }

            if (message.SuperChatTier is { } tier)
            {
                return GetSuperChatPalette(tier).Card;
            }

            return message.IsGift
                ? GiftMessageAttribute
                : listView.GetAttributeForRole(VisualRole.Normal);
        }

        private static void DrawSegment(
            GuiListView listView,
            int col,
            int row,
            string text,
            GuiAttribute attribute)
        {
            listView.SetAttribute(attribute);
            listView.AddStr(col, row, text);
        }
    }

    private sealed class DanmakuListItem(
        Guid id,
        string text,
        SuperChatEvent? superChat = null,
        SuperChatTier? superChatTier = null,
        bool isGift = false,
        DanmakuEvent? danmaku = null)
    {
        public Guid Id { get; } = id;
        public SuperChatEvent? SuperChat { get; } = superChat;
        public SuperChatTier? SuperChatTier { get; } = superChatTier;
        public bool IsGift { get; } = isGift;
        public DanmakuEvent? Danmaku { get; } = danmaku;

        public override string ToString() => text;
    }

    private sealed class ActiveSuperChat(
        SuperChatEvent superChat,
        DateTimeOffset displayedAt,
        DateTimeOffset expiresAt)
    {
        public SuperChatEvent SuperChat { get; } = superChat;
        public DateTimeOffset DisplayedAt { get; } = displayedAt;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public SuperChatCapsuleView? Capsule { get; set; }
    }

    private sealed record SuperChatPalette(
        GuiColor DeepBackground,
        GuiColor LightBackground)
    {
        public GuiAttribute Deep { get; } = new(
            GuiColor.White,
            DeepBackground,
            TextStyle.Bold);

        public GuiAttribute Light { get; } = new(
            GuiColor.Black,
            LightBackground,
            TextStyle.Bold);

        public GuiAttribute Card { get; } = new(
            GuiColor.Black,
            LightBackground);
    }

    private sealed class ExpandedSuperChatCardView(Action dismissed) : GuiView
    {
        private IReadOnlyList<string> _lines = [];
        private GuiAttribute _attribute;

        public int CardHeight => _lines.Count;

        public void SetSuperChat(SuperChatEvent superChat, int width, int maximumHeight)
        {
            width = Math.Max(16, width);
            maximumHeight = Math.Max(3, maximumHeight);
            var lines = FormatSuperChatCard(superChat, width);
            if (lines.Count > maximumHeight)
            {
                var visibleLines = lines.Take(maximumHeight - 2).ToList();
                visibleLines.Add($"│ {FitToColumns("…", width - 4, ' ')} │");
                visibleLines.Add(lines[^1]);
                lines = visibleLines;
            }

            _lines = lines;
            _attribute = GetSuperChatPalette(
                SuperChatPresentation.GetTier(superChat.PriceCny)).Card;
            Height = _lines.Count;
            SetNeedsDraw();
        }

        public void Clear()
        {
            _lines = [];
            SetNeedsDraw();
        }

        protected override bool OnDrawingContent(DrawContext? context)
        {
            SetAttribute(_attribute);
            for (var row = 0; row < _lines.Count; row++)
            {
                AddStr(0, row, _lines[row]);
            }

            return true;
        }

        protected override bool OnMouseEvent(Mouse mouse)
        {
            if (!mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked))
            {
                return false;
            }

            dismissed();
            return true;
        }
    }

    private sealed class SuperChatCapsuleView : GuiView
    {
        private readonly Action _accepted;
        private readonly string _displayText;
        private readonly SuperChatPalette _palette;
        private double _remainingFraction;

        public SuperChatCapsuleView(
            SuperChatEvent superChat,
            SuperChatPalette palette,
            Action accepted)
        {
            _accepted = accepted;
            _displayText = $"¥{superChat.PriceCny}";
            _palette = palette;
        }

        public void SetRemainingFraction(double value)
        {
            _remainingFraction = Math.Clamp(value, 0, 1);
            SetNeedsDraw();
        }

        protected override bool OnDrawingContent(DrawContext? context)
        {
            var width = Viewport.Width;
            var filledWidth = (int)Math.Ceiling(width * _remainingFraction);
            var textWidth = _displayText.GetColumns();
            var textStart = Math.Max(0, (width - textWidth) / 2);
            for (var x = 0; x < width; x++)
            {
                SetAttribute(x < filledWidth ? _palette.Deep : _palette.Light);
                var characterIndex = x - textStart;
                var character = characterIndex >= 0 && characterIndex < _displayText.Length
                    ? _displayText[characterIndex]
                    : ' ';
                AddRune(x, 0, new Rune(character));
            }

            return true;
        }

        protected override bool OnMouseEvent(Mouse mouse)
        {
            if (!mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked))
            {
                return false;
            }

            _accepted();
            return true;
        }
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
