using BiliStreamAudio.Tui.Core;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace BiliStreamAudio.Tui.Views;

internal sealed class MainWindow : ApplicationWindow
{
    private readonly Tabs _tabs;
    private readonly SpectrumStatusBarView _statusBar;
    private readonly IAudioPlayer _audio;
    private readonly bool _mockMode;
    private readonly LiveRoomDisplayOptions _displayOptions;

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
        _mockMode = mockMode;
        _displayOptions = liveRoomDisplayOptions;

        _statusBar = new SpectrumStatusBarView
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(2),
            Height = 1
        };
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
            CloseLiveRoom);
        _tabs = new Tabs
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1)
        };
        Browse = new BrowseWindow(app, directory, rooms, session, ShowLiveRoom);
        var playbackHistory = new PlaybackHistoryWindow(app, history, rooms, session, ShowLiveRoom);
        Settings = new SettingsWindow(app, liveRoomDisplayOptions, () =>
        {
            LiveRoom.RefreshDisplay();
            RefreshStatusBar();
        }, auth, tokenRefresh, settingsStore);
        _tabs.Add(
            LiveRoom,
            Browse,
            playbackHistory,
            Settings);
        _tabs.ValueChanged += (_, args) =>
        {
            if (ReferenceEquals(args.NewValue, playbackHistory))
            {
                playbackHistory.Load();
            }
            else if (ReferenceEquals(args.NewValue, LiveRoom))
            {
                LiveRoom.FocusInput();
            }
        };
        _tabs.Value = LiveRoom;
        audio.StateChanged += (_, _) => app.Invoke(RefreshStatusBar);
        if (audio is IAudioSpectrumSource spectrumSource)
        {
            spectrumSource.SpectrumChanged += (_, spectrum) => app.Invoke(() => _statusBar.SetSpectrum(spectrum));
            _statusBar.SetSpectrum(spectrumSource.CurrentSpectrum);
        }
        RefreshStatusBar();

        Add(_tabs, _statusBar);

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
        var muteStatus = _audio.IsMuted ? " (静音)" : string.Empty;
        var mockStatus = _mockMode ? " · 模拟模式" : string.Empty;
        _statusBar.SetStatus($"音量 {_audio.Volume}{muteStatus} · {_audio.State.ToDisplayText()}{mockStatus} · Esc 关闭直播间 · r 刷新 · m 静音 · +/- 音量 · l 登录 · Q/E 切换标签 · Ctrl+C 退出");
        _statusBar.SetBandCount(_displayOptions.SpectrumBandCount);
        _statusBar.SetColorMode(_displayOptions.SpectrumColorMode);
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
