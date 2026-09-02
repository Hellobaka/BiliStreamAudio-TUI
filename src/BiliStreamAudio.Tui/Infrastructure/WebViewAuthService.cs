using System.Text.Json;
using System.Windows.Forms;
using BiliStreamAudio.Tui.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace BiliStreamAudio.Tui.Infrastructure;

public sealed class WebViewAuthService : IAuthService
{
    private readonly AuthStorage _storage;
    private readonly AccountInfoService _accountInfo;

    public WebViewAuthService(
        AuthStorage storage,
        AccountInfoService? accountInfo = null)
    {
        _storage = storage;
        _accountInfo = accountInfo ?? new AccountInfoService();
    }

    public AuthSession? Current
    {
        get; private set;
    }

    public async Task<AuthSession?> LoadAsync(CancellationToken cancellationToken)
    {
        Current = await _storage.LoadAsync(cancellationToken).ConfigureAwait(false);
        return Current;
    }

    public async Task SaveAsync(AuthSession session, CancellationToken cancellationToken)
    {
        await _storage.SaveAsync(session, cancellationToken).ConfigureAwait(false);
        Current = session;
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await _storage.ClearAsync(cancellationToken).ConfigureAwait(false);
        Current = null;
    }

    public async Task<AuthSession> LoginAsync(CancellationToken cancellationToken)
    {
        var existingSession = Current
            ?? await _storage.LoadAsync(cancellationToken).ConfigureAwait(false);
        var loginSession = await OfficialLoginWindow
            .OpenAsync(existingSession, cancellationToken)
            .ConfigureAwait(false);
        var session = await _accountInfo
            .PopulateAsync(loginSession, cancellationToken)
            .ConfigureAwait(false);
        await SaveAsync(session, cancellationToken).ConfigureAwait(false);
        return session;
    }

    private static class OfficialLoginWindow
    {
        public static Task<AuthSession> OpenAsync(
            AuthSession? existingSession,
            CancellationToken cancellationToken)
        {
            var result = new TaskCompletionSource<AuthSession>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() => Run(result, existingSession, cancellationToken))
            {
                IsBackground = true,
                Name = "BiliStreamAudio.Login"
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return result.Task;
        }

        private static void Run(
            TaskCompletionSource<AuthSession> result,
            AuthSession? existingSession,
            CancellationToken cancellationToken)
        {
            var profile = Path.Combine(
                Path.GetTempPath(),
                "BiliStreamAudio-TUI-WebView2",
                Guid.NewGuid().ToString("N"));
            using var form = new Form
            {
                Text = "Bilibili 官方登录",
                Width = 1040,
                Height = 760,
                StartPosition = FormStartPosition.CenterScreen
            };
            using var web = new WebView2 { Dock = DockStyle.Fill };
            using var actions = new Panel { Dock = DockStyle.Bottom, Height = 48 };
            using var hint = new Label
            {
                AutoSize = true,
                Left = 12,
                Top = 15,
                Text = "请在官方页面完成登录，然后点击右侧按钮"
            };
            using var complete = new Button
            {
                Text = "完成登录",
                Width = 112,
                Height = 30,
                Top = 9,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Enabled = false
            };

            complete.Left = actions.ClientSize.Width - complete.Width - 12;
            actions.Resize += (_, _) => complete.Left = actions.ClientSize.Width - complete.Width - 12;
            actions.Controls.Add(hint);
            actions.Controls.Add(complete);
            form.Controls.Add(web);
            form.Controls.Add(actions);
            actions.BringToFront();

            form.Shown += async (_, _) =>
            {
                try
                {
                    Directory.CreateDirectory(profile);
                    var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profile);
                    await web.EnsureCoreWebView2Async(environment);
                    RestoreSession(web.CoreWebView2, existingSession);
                    web.CoreWebView2.Navigate("https://passport.bilibili.com/login");
                    complete.Enabled = true;
                }
                catch (Exception exception)
                {
                    var failure = new InvalidOperationException(
                        "WebView2 初始化失败。请确认已安装 WebView2 运行环境。",
                        exception);
                    result.TrySetException(failure);
                    form.Close();
                }
            };
            complete.Click += async (_, _) => await CompleteLoginAsync(form, web, complete, result);
            form.FormClosed += (_, _) =>
            {
                if (!result.Task.IsCompleted)
                {
                    result.TrySetCanceled();
                }
            };
            using var cancellation = cancellationToken.Register(() =>
            {
                result.TrySetCanceled(cancellationToken);
                if (form.IsHandleCreated)
                {
                    form.BeginInvoke(form.Close);
                }
            });

            try
            {
                Application.EnableVisualStyles();
                Application.Run(form);
            }
            catch (Exception exception)
            {
                result.TrySetException(exception);
            }
            finally
            {
                if (!result.Task.IsCompleted)
                {
                    result.TrySetCanceled();
                }

                web.Dispose();
                form.Dispose();
                TryDeleteProfile(profile);
            }
        }

        private static void RestoreSession(CoreWebView2 webView, AuthSession? session)
        {
            if (session is null)
            {
                return;
            }

            foreach (var (name, value) in session.Cookies)
            {
                var cookie = webView.CookieManager.CreateCookie(
                    name,
                    value,
                    ".bilibili.com",
                    "/");
                cookie.IsSecure = true;
                cookie.IsHttpOnly = name.Equals("SESSDATA", StringComparison.Ordinal);
                webView.CookieManager.AddOrUpdateCookie(cookie);
            }

            RestoreRefreshTokenAfterNavigation(webView, session.RefreshToken);
        }

        private static void RestoreRefreshTokenAfterNavigation(
            CoreWebView2 webView,
            string? refreshToken)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return;
            }

            EventHandler<CoreWebView2NavigationCompletedEventArgs>? restoreToken = null;
            restoreToken = async (_, navigation) =>
            {
                webView.NavigationCompleted -= restoreToken;
                if (!navigation.IsSuccess)
                {
                    return;
                }

                var serializedToken = JsonSerializer.Serialize(refreshToken);
                await webView.ExecuteScriptAsync(
                    $"localStorage.setItem('ac_time_value', {serializedToken})");
            };
            webView.NavigationCompleted += restoreToken;
        }

        private static async Task CompleteLoginAsync(
            Form form,
            WebView2 web,
            Button complete,
            TaskCompletionSource<AuthSession> result)
        {
            if (result.Task.IsCompleted || web.CoreWebView2 is null)
            {
                return;
            }

            complete.Enabled = false;
            try
            {
                var cookies = await web.CoreWebView2.CookieManager
                    .GetCookiesAsync("https://api.bilibili.com");
                var values = cookies
                    .GroupBy(cookie => cookie.Name, StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Last().Value,
                        StringComparer.Ordinal);

                if (!values.ContainsKey("SESSDATA") || !values.ContainsKey("bili_jct"))
                {
                    throw new InvalidOperationException("尚未检测到有效的哔哩哔哩登录会话，请先完成登录。");
                }

                var raw = await web.CoreWebView2.ExecuteScriptAsync("localStorage.getItem('ac_time_value')");
                var token = JsonSerializer.Deserialize<string>(raw);
                var uid = long.TryParse(values.GetValueOrDefault("DedeUserID"), out var id) ? id : 0;
                if (result.TrySetResult(new AuthSession(values, token, uid, null)))
                {
                    form.Close();
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(form, exception.ToDisplayText(), "登录尚未完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                complete.Enabled = true;
            }
        }

        private static void TryDeleteProfile(string profile)
        {
            var root = Path.GetFullPath(
                Path.Combine(Path.GetTempPath(), "BiliStreamAudio-TUI-WebView2"))
                + Path.DirectorySeparatorChar;
            var target = Path.GetFullPath(profile);
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                || !Directory.Exists(target))
            {
                return;
            }

            var lockFile = Path.Combine(target, "lockfile");
            const int maximumAttempts = 20;
            for (var attempt = 0; attempt < maximumAttempts && File.Exists(lockFile); attempt++)
            {
                Thread.Sleep(millisecondsTimeout: 100);
            }

            // WebView2 can keep its browser process alive briefly after the control is disposed.
            // Leave the temporary folder for a later cleanup instead of throwing during shutdown.
            if (File.Exists(lockFile))
            {
                return;
            }

            try
            {
                Directory.Delete(target, recursive: true);
            }
            catch (IOException)
            {
                // A browser child process acquired another file between the lock check and deletion.
            }
            catch (UnauthorizedAccessException)
            {
                // Profile cleanup is best-effort; authentication data is never read from this folder.
            }
        }
    }
}
