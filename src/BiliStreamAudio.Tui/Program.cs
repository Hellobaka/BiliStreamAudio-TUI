using System.Collections.ObjectModel;
using System.Text;
using BiliStreamAudio.Tui.Core;
using BiliStreamAudio.Tui.Infrastructure;
using Serilog;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using GuiApplication = Terminal.Gui.App.Application;
using GuiLabel = Terminal.Gui.Views.Label;
using GuiListView = Terminal.Gui.Views.ListView;
using GuiMessageBox = Terminal.Gui.Views.MessageBox;

namespace BiliStreamAudio.Tui;

internal static class Program
{
    private const int MaximumMessageCount = 500;

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

        var window = new Window
        {
            Title = mockMode ? "BiliStreamAudio-TUI（模拟模式）" : "BiliStreamAudio-TUI",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        var header = new GuiLabel
        {
            Text = "未选择直播间 · 已停止 · 未登录",
            HotKeySpecifier = new Rune(0xffff),
            X = 1,
            Y = 0,
            Width = Dim.Fill(2)
        };
        var messageItems = new ObservableCollection<string>();
        var messages = new GuiListView
        {
            X = 1,
            Y = 2,
            Width = Dim.Fill(2),
            Height = Dim.Fill(4)
        };
        messages.SetSource(messageItems);
        var input = new TextField
        {
            Text = "",
            X = 1,
            Y = Pos.AnchorEnd(2),
            Width = Dim.Fill(2)
        };
        var footer = new GuiLabel
        {
            Text = "音量 70 · r 刷新 · m 静音 · +/- 音量 · l 登录 · Tab 焦点 · q 退出",
            HotKeySpecifier = new Rune(0xffff),
            X = 1,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(2)
        };

        window.Add(header, messages, input, footer);

        var sessionStatus = "已停止";

        void Ui(Action action) => app.Invoke(action);

        void RefreshHeader()
        {
            var roomStatus = session.Room is { } room
                ? $"{room.RoomId} · {room.Anchor} · {room.Title}"
                : "未选择直播间";
            var loginStatus = CreateLoginStatus(auth.Current);
            header.Text = $"{roomStatus} · {sessionStatus} · {loginStatus}";
        }

        void RefreshFooter()
        {
            var muteStatus = audio.IsMuted ? " (静音)" : string.Empty;
            var mockStatus = mockMode ? " · 模拟模式" : string.Empty;
            footer.Text = $"音量 {audio.Volume}{muteStatus} · {audio.State}{mockStatus} · r 刷新 · m 静音 · +/- 音量 · l 登录 · Tab 焦点 · q 退出";
            footer.SetNeedsDraw();
        }

        void AddMessage(string value)
        {
            Ui(() =>
            {
                messageItems.Add(value);
                while (messageItems.Count > MaximumMessageCount)
                {
                    messageItems.RemoveAt(0);
                }

                messages.MoveEnd(extend: false);
            });
        }

        danmaku.Received += (_, item) =>
            AddMessage($"[{item.ReceivedAt:HH:mm:ss}] {item.UserName}: {item.Message}");
        danmaku.StatusChanged += (_, status) => Ui(() =>
        {
            sessionStatus = status;
            RefreshHeader();
        });
        audio.StateChanged += (_, _) => Ui(RefreshFooter);
        session.RoomChanged += (_, _) => Ui(RefreshHeader);
        session.StatusChanged += (_, status) => Ui(() =>
        {
            sessionStatus = status;
            RefreshHeader();
        });

        RefreshFooter();
        if (mockMode)
        {
            AddMessage("模拟模式已启用：所有直播间、音频、登录和弹幕操作均不会发送网络请求。");
        }

        input.KeyDown += (_, key) =>
        {
            if (key != Key.Enter)
            {
                return;
            }

            var value = input.Text.ToString() ?? string.Empty;
            input.Text = string.Empty;

            if (long.TryParse(value, out var roomId))
            {
                _ = RunUiTask(
                    () => session.SwitchAsync(roomId, CancellationToken.None),
                    AddMessage);
            }
            else if (session.Room is { } room && auth.Current is not null)
            {
                _ = RunUiTask(
                    () => SendDanmakuAsync(room.RoomId, value, auth, tokenRefresh, sender),
                    AddMessage);
            }
            else
            {
                AddMessage("请输入房间号；登录后可发送弹幕。");
            }

            key.Handled = true;
        };

        app.Keyboard.KeyDown += (_, key) =>
        {
            // Application-level handling runs before the focused TextField consumes printable keys.
            // Once the user has started typing, letters remain normal input.
            if (!string.IsNullOrEmpty(input.Text))
            {
                return;
            }

            if (IsKey(key, 'q'))
            {
                app.RequestStop(window);
                key.Handled = true;
            }
            else if (IsKey(key, 'r'))
            {
                _ = RunUiTask(
                    () => session.RefreshAsync(CancellationToken.None),
                    AddMessage);
                key.Handled = true;
            }
            else if (IsKey(key, 'm'))
            {
                audio.ToggleMute();
                RefreshFooter();
                key.Handled = true;
            }
            else if (key == (Key)'+')
            {
                audio.SetVolume(audio.Volume + 5);
                RefreshFooter();
                key.Handled = true;
            }
            else if (key == (Key)'-')
            {
                audio.SetVolume(audio.Volume - 5);
                RefreshFooter();
                key.Handled = true;
            }
            else if (IsKey(key, 'l'))
            {
                _ = LoginAsync(
                    auth,
                    AddMessage,
                    () => Ui(RefreshHeader));
                key.Handled = true;
            }
        };

        _ = RunUiTask(
            () => LoadAndRefreshAsync(auth, tokenRefresh),
            AddMessage,
            () => Ui(RefreshHeader));
        app.Run(window);

        session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        http?.Dispose();
    }

    private static async Task RunUiTask(
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

    private static async Task LoginAsync(
        IAuthService auth,
        Action<string> notify,
        Action onSuccess)
    {
        try
        {
            await auth.LoginAsync(CancellationToken.None).ConfigureAwait(false);
            notify($"已登录：{auth.Current?.UserName}");
            onSuccess();
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Official login window failed");
            notify($"登录失败：{exception.Message}");
        }
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

    private static bool IsKey(Key key, char value)
    {
        return key == (Key)char.ToLowerInvariant(value)
            || key == (Key)char.ToUpperInvariant(value);
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

    private static async Task SendDanmakuAsync(
        long roomId,
        string text,
        IAuthService auth,
        ITokenRefreshService refresh,
        IDanmakuSender sender)
    {
        var current = auth.Current ?? throw new InvalidOperationException("请先登录。");
        var result = await refresh.RefreshIfNeededAsync(current, CancellationToken.None).ConfigureAwait(false);
        if (!result.Success || result.Session is null)
        {
            throw new InvalidOperationException(result.Error ?? "登录会话需要重新登录。");
        }

        await auth.SaveAsync(result.Session, CancellationToken.None).ConfigureAwait(false);
        await sender.SendAsync(roomId, text, result.Session, CancellationToken.None).ConfigureAwait(false);
    }
}
