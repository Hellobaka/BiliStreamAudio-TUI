using BiliStreamAudio.Tui.Core;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using GuiView = Terminal.Gui.ViewBase.View;

namespace BiliStreamAudio.Tui.Views;

internal sealed class MainWindow : ApplicationWindow
{
    private readonly Tabs _tabs;
    private readonly GuiView _statusBarContainer;
    private readonly SpectrumStatusBarView[] _statusBars;
    private readonly PlaybackHistoryWindow _playbackHistory;
    private readonly IAudioPlayer _audio;
    private readonly ISettingsStore _settingsStore;
    private readonly bool _mockMode;
    private readonly LiveRoomDisplayOptions _displayOptions;
    private readonly IAudioSpectrumSource? _spectrumSource;
    private readonly EventHandler<SpectrumFrame> _spectrumChangedHandler;
    private bool _isSpectrumSubscribed;

    public MainWindow(
        IApplication app,
        RoomSession session,
        IAuthService auth,
        ITokenRefreshService tokenRefresh,
        IRoomResolver rooms,
        ILiveDirectoryService directory,
        IAudioPlayer audio,
        IDanmakuConnection danmaku,
        IDanmakuSender sender,
        IHistoryStore history,
        bool mockMode,
        LiveRoomDisplayOptions liveRoomDisplayOptions,
        ISettingsStore settingsStore)
    {
        Title = mockMode ? "BiliStreamAudio-TUI（模拟模式）" : "BiliStreamAudio-TUI";
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();
        BorderStyle = LineStyle.None;
        _audio = audio;
        _settingsStore = settingsStore;
        _mockMode = mockMode;
        _displayOptions = liveRoomDisplayOptions;
        _spectrumSource = audio as IAudioSpectrumSource;
        _spectrumChangedHandler = (_, spectrum) => app.Invoke(() => SetSpectrum(spectrum));

        _statusBarContainer = new GuiView
        {
            X = 1,
            Y = Pos.AnchorEnd(2),
            Width = Dim.Fill(2),
            Height = 2
        };
        _statusBars = [new SpectrumStatusBarView { X = 0, Y = 0, Width = Dim.Fill(), Height = 1 },
            new SpectrumStatusBarView { X = 0, Y = 1, Width = Dim.Fill(), Height = 1 }];
        _statusBarContainer.Add(_statusBars);
        LiveRoom = new LiveRoomWindow(
            app,
            session,
            auth,
            tokenRefresh,
            audio,
            danmaku,
            sender,
            history,
            mockMode,
            liveRoomDisplayOptions,
            RefreshStatusBar,
            CloseLiveRoom,
            PersistVolume);
        _tabs = new Tabs
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1)
        };
        Browse = new BrowseWindow(app, directory, rooms, session, ShowLiveRoom);
        _playbackHistory = new PlaybackHistoryWindow(app, history, rooms, session, ShowLiveRoom);
        Settings = new SettingsWindow(app, liveRoomDisplayOptions, audio, () =>
        {
            LiveRoom.RefreshDisplay();
            RefreshStatusBar();
        }, SetStatusBarEditing, auth, tokenRefresh, settingsStore);
        _tabs.Add(
            LiveRoom,
            Browse,
            _playbackHistory,
            Settings);
        Browse.ShortcutHintChanged += RefreshStatusBar;
        _tabs.ValueChanged += (_, args) =>
        {
            if (ReferenceEquals(args.NewValue, _playbackHistory))
            {
                _playbackHistory.Load();
            }
            else if (ReferenceEquals(args.NewValue, LiveRoom))
            {
                LiveRoom.FocusInput();
            }

            RefreshStatusBar();
        };
        _tabs.Value = LiveRoom;
        audio.StateChanged += (_, _) => app.Invoke(RefreshStatusBar);
        session.RoomChanged += (_, _) => app.Invoke(RefreshStatusBar);
        danmaku.EventReceived += (_, _) => app.Invoke(RefreshStatusBar);
        RefreshStatusBar();

        app.AddTimeout(TimeSpan.FromSeconds(1), () =>
        {
            RefreshStatusBar();
            return true;
        });

        Add(_tabs, _statusBarContainer);

        if (mockMode)
        {
            LiveRoom.AddMessage("模拟模式已启用：所有直播间、音频、登录和弹幕操作均不会发送网络请求。");
        }
    }

    public LiveRoomWindow LiveRoom { get; }

    public BrowseWindow Browse { get; }

    public SettingsWindow Settings { get; } = null!;

    public bool IsTextInputFocused => LiveRoom.IsInputFocused || Browse.IsSearchInputFocused || Settings.IsTextInputFocused;

    public void SelectPreviousTab() => SelectTab(-1);

    public void SelectNextTab() => SelectTab(1);

    private void ShowLiveRoom() => _tabs.Value = LiveRoom;

    private void CloseLiveRoom() => _tabs.Value = Browse;

    private void RefreshStatusBar()
    {
        if (!_statusBarContainer.Visible)
        {
            return;
        }

        var settings = _settingsStore.Load();
        var layout = StatusBarLayout.Normalize(
            settings.StatusBarFirstRow,
            settings.StatusBarSecondRow,
            settings.StatusBarLayoutVersion is null);
        var rows = new[] { layout.FirstRow, layout.SecondRow }.Where(row => row.Count > 0).ToArray();
        UpdateSpectrumSubscription(layout);
        _statusBarContainer.Visible = rows.Length > 0;
        _statusBarContainer.Height = rows.Length;
        _statusBarContainer.Y = Pos.AnchorEnd(rows.Length);
        _tabs.Height = Dim.Fill(rows.Length);
        var content = new StatusBarContent(
            _audio.Volume,
            _audio.IsMuted,
            _audio.State,
            _mockMode,
            $"{GetShortcutHint()} · Q/E 切换标签 · Ctrl+C 退出",
            LiveRoom.Session.Room,
            LiveRoom.Session.Statistics.GetSnapshot());

        for (var index = 0; index < _statusBars.Length; index++)
        {
            var visible = index < rows.Length;
            var statusBar = _statusBars[index];
            statusBar.Visible = visible;
            if (!visible)
            {
                continue;
            }

            statusBar.Y = index;
            statusBar.SetElements(rows[index]);
            statusBar.SetContent(content);
            statusBar.SetBandCount(_displayOptions.SpectrumBandCount);
            statusBar.SetColorMode(_displayOptions.SpectrumColorMode);
        }

        SetNeedsDraw();
    }

    private void UpdateSpectrumSubscription((List<StatusBarElement> FirstRow, List<StatusBarElement> SecondRow) layout)
    {
        if (_spectrumSource is null)
        {
            return;
        }

        var shouldRenderSpectrum = layout.FirstRow.Contains(StatusBarElement.Spectrum)
                                   || layout.SecondRow.Contains(StatusBarElement.Spectrum);
        _spectrumSource.SetSpectrumEnabled(shouldRenderSpectrum);
        if (shouldRenderSpectrum == _isSpectrumSubscribed)
        {
            return;
        }

        if (shouldRenderSpectrum)
        {
            _spectrumSource.SpectrumChanged += _spectrumChangedHandler;
            _isSpectrumSubscribed = true;
            SetSpectrum(_spectrumSource.CurrentSpectrum);
        }
        else
        {
            _spectrumSource.SpectrumChanged -= _spectrumChangedHandler;
            _isSpectrumSubscribed = false;
            SetSpectrum(null);
        }
    }

    private void SetSpectrum(SpectrumFrame? spectrum)
    {
        foreach (var statusBar in _statusBars)
        {
            statusBar.SetSpectrum(spectrum);
        }
    }

    private void SetStatusBarEditing(bool editing)
    {
        _statusBarContainer.Visible = !editing;
        if (editing)
        {
            _tabs.Height = Dim.Fill();
        }
        else
        {
            RefreshStatusBar();
        }
    }

    private string GetShortcutHint()
    {
        if (ReferenceEquals(_tabs.Value, LiveRoom))
        {
            return "Esc 关闭直播间 · r 刷新 · m 静音 · +/- 音量";
        }

        if (ReferenceEquals(_tabs.Value, Browse))
        {
            return Browse.ShortcutHint;
        }

        if (ReferenceEquals(_tabs.Value, _playbackHistory))
        {
            return "r 刷新 · Enter 播放/删除";
        }

        return "Alt+1~4 切换分类 · 方向键导航 · Enter 操作";
    }

    private void PersistVolume()
    {
        var settings = _settingsStore.Load();
        settings.Volume = _audio.Volume;
        _settingsStore.Save(settings);
    }

    private void SelectTab(int offset)
    {
        var tabs = _tabs.TabCollection;
        var tabCount = tabs.Count();
        if (tabCount == 0)
        {
            return;
        }

        var currentIndex = _tabs.Value is { } current ? _tabs.IndexOf(current) : 0;
        var nextIndex = (currentIndex + offset + tabCount) % tabCount;
        _tabs.Value = tabs.ElementAt(nextIndex);
    }
}
