using BiliStreamAudio.Tui.Core;
using BiliStreamAudio.Tui.Infrastructure;
using BiliStreamAudio.Tui.Views;
using Serilog;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using GuiApplication = Terminal.Gui.App.Application;
using GuiMessageBox = Terminal.Gui.Views.MessageBox;

namespace BiliStreamAudio.Tui;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var logPath = Path.Combine(localData, "BiliStreamAudio-TUI", "logs", "app-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
            .CreateLogger();

        try
        {
            var mockMode = AppOptions.IsMockMode(
                args,
                Environment.GetEnvironmentVariable(AppOptions.MockModeEnvironmentVariable));
            Run(mockMode);
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Fatal application error");
            Console.Error.WriteLine("启动失败。请查看应用日志。");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void Run(bool mockMode)
    {
        IAuthService auth;
        ITokenRefreshService tokenRefresh;
        IRoomResolver rooms;
        IStreamResolver streams;
        IAudioPlayer audio;
        IDanmakuConnection danmaku;
        IDanmakuSender sender;
        BiliHttp? http = null;

        if (mockMode)
        {
            auth = new MockAuthService();
            tokenRefresh = new MockTokenRefreshService();
            rooms = new MockRoomResolver();
            streams = new MockStreamResolver();
            audio = new MockAudioPlayer();
            var mockDanmaku = new MockDanmakuConnection();
            danmaku = mockDanmaku;
            sender = new MockDanmakuSender(mockDanmaku);
        }
        else
        {
            var storage = new AuthStorage();
            auth = new WebViewAuthService(storage);
            auth.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            tokenRefresh = new CookieRefreshService(storage);
            http = new BiliHttp(sessionProvider: () => auth.Current);
            rooms = new RoomResolver(http);
            streams = new StreamResolver(http);
            audio = new AudioPlayer();
            danmaku = new DanmakuConnection(() => auth.Current);
            sender = new DanmakuSender();
        }

        using IApplication app = GuiApplication.Create();
        app.Init();

        var session = new RoomSession(
            rooms,
            streams,
            audio,
            danmaku,
            () => AskFallbackAsync(app));
        var mainWindow = new MainWindow(app, session, auth, tokenRefresh, audio, danmaku, sender, mockMode);

        app.Keyboard.KeyDown += (_, key) =>
        {
            // Application-level handling runs before the focused TextField consumes printable keys.
            // Q/E must remain normal input while the live room input has focus.
            if (mainWindow.IsLiveRoomInputFocused)
            {
                return;
            }

            if (key == Key.Q || key == Key.Q.WithShift)
            {
                mainWindow.SelectPreviousTab();
                key.Handled = true;
            }
            else if (key == Key.E || key == Key.E.WithShift)
            {
                mainWindow.SelectNextTab();
                key.Handled = true;
            }
            else if (key == Key.Q.WithCtrl)
            {
                app.RequestStop(mainWindow);
                key.Handled = true;
            }
        };

        _ = LiveRoomWindow.RunUiTask(
            () => LoadAndRefreshAsync(auth, tokenRefresh),
            mainWindow.LiveRoom.AddMessage,
            () => app.Invoke(mainWindow.LiveRoom.RefreshHeader));
        app.Run(mainWindow);

        session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        http?.Dispose();
    }

    private static Task<bool> AskFallbackAsync(IApplication app)
    {
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        app.Invoke(() =>
        {
            var choice = GuiMessageBox.Query(
                app,
                "音频流不可用",
                "是否尝试普通最低清晰度流？",
                "是",
                "否");
            answer.SetResult(choice == 0);
        });
        return answer.Task;
    }

    private static async Task LoadAndRefreshAsync(IAuthService auth, ITokenRefreshService refresh)
    {
        var saved = await auth.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        if (saved is null)
        {
            return;
        }

        var result = await refresh.RefreshIfNeededAsync(saved, CancellationToken.None).ConfigureAwait(false);
        if (!result.Success || result.Session is null)
        {
            throw new InvalidOperationException(result.Error ?? "登录会话需要重新登录。");
        }

        await auth.SaveAsync(result.Session, CancellationToken.None).ConfigureAwait(false);
    }
}
