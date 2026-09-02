using BiliStreamAudio.Tui.Core;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using GuiLabel = Terminal.Gui.Views.Label;

namespace BiliStreamAudio.Tui.Views;

internal sealed class MainWindow : Window
{
    private readonly Tabs _tabs;
    private readonly GuiLabel _statusBar;
    private readonly IAudioPlayer _audio;
    private readonly bool _mockMode;

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
        bool mockMode)
    {
        Title = mockMode ? "BiliStreamAudio-TUI（模拟模式）" : "BiliStreamAudio-TUI";
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();
        BorderStyle = LineStyle.None;
        _audio = audio;
        _mockMode = mockMode;

        _statusBar = new GuiLabel
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(2)
        };
        LiveRoom = new LiveRoomWindow(
            app,
            session,
            auth,
            tokenRefresh,
            audio,
            danmaku,
            sender,
            RefreshStatusBar);
        _tabs = new Tabs
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1)
        };
        Browse = new BrowseWindow(app, directory, rooms, session, ShowLiveRoom);
        _tabs.Add(
            LiveRoom,
            Browse,
            new PlaceholderWindow("观看历史", "观看历史将在这里显示。"),
            new PlaceholderWindow("设置", "应用设置将在这里配置。"));
        _tabs.Value = LiveRoom;
        audio.StateChanged += (_, _) => app.Invoke(RefreshStatusBar);
        RefreshStatusBar();

        Add(_tabs, _statusBar);

        if (mockMode)
        {
            LiveRoom.AddMessage("模拟模式已启用：所有直播间、音频、登录和弹幕操作均不会发送网络请求。");
        }
    }

    public LiveRoomWindow LiveRoom { get; }

    public BrowseWindow Browse { get; }

    public bool IsTextInputFocused => LiveRoom.IsInputFocused || Browse.IsSearchInputFocused;

    public void SelectPreviousTab() => SelectTab(-1);

    public void SelectNextTab() => SelectTab(1);

    private void ShowLiveRoom() => _tabs.Value = LiveRoom;

    private void RefreshStatusBar()
    {
        var muteStatus = _audio.IsMuted ? " (静音)" : string.Empty;
        var mockStatus = _mockMode ? " · 模拟模式" : string.Empty;
        _statusBar.Text = $"音量 {_audio.Volume}{muteStatus} · {_audio.State}{mockStatus} · r 刷新 · m 静音 · +/- 音量 · l 登录 · Q/E 切换标签 · Ctrl+Q 退出";
        _statusBar.SetNeedsDraw();
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
