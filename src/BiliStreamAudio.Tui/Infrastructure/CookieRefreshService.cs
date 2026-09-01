using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BiliStreamAudio.Tui.Core;

namespace BiliStreamAudio.Tui.Infrastructure;

public sealed class CookieRefreshService(
    AuthStorage storage,
    Func<AuthSession, BiliHttp>? httpFactory = null) : ITokenRefreshService
{
    private const string CheckEndpoint = "https://passport.bilibili.com/x/passport-login/web/cookie/info";
    private const string RefreshEndpoint = "https://passport.bilibili.com/x/passport-login/web/cookie/refresh";
    private const string ConfirmEndpoint = "https://passport.bilibili.com/x/passport-login/web/confirm/refresh";
    private const string PublicKey = """
        -----BEGIN PUBLIC KEY-----
        MIGfMA0GCSqGSIb3DQEBAQUAA4GNADCBiQKBgQDLgd2OAkcGVtoE3ThUREbio0Eg
        Uc/prcajMKXvkCKFCWhJYJcLkcM2DKKcSeFpD/j6Boy538YXnR6VhcuUJOhH2x71
        nzPjfdTcqMz7djHum0qSZA0AyCBDABUqCrfNgCiJ00Ra7GmRj+YCK1NJEuewlb40
        JNrRuoEUXpabUzGB8QIDAQAB
        -----END PUBLIC KEY-----
        """;

    private readonly Func<AuthSession, BiliHttp> _httpFactory = httpFactory
        ?? (session => new BiliHttp(session, useRawCookieHeader: false));

    public async Task<RefreshResult> RefreshIfNeededAsync(AuthSession session, CancellationToken cancellationToken)
    {
        if (!session.IsAuthenticated)
        {
            return RefreshResult.Failed("Not logged in.");
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        if (session.LastRefreshCheck == today)
        {
            return new RefreshResult(true, session, null);
        }

        try
        {
            using var http = _httpFactory(session);
            using var check = await http
                .GetJsonAsync(CheckEndpoint, cancellationToken)
                .ConfigureAwait(false);

            BiliJson.EnsureOk(check);
            var checkData = check.RootElement.GetProperty("data");
            var mustRefresh = checkData.TryGetProperty("refresh", out var refresh)
                && refresh.GetBoolean();

            if (!mustRefresh)
            {
                var checkedSession = session with
                {
                    LastRefreshCheck = today
                };
                await storage.SaveAsync(checkedSession, cancellationToken).ConfigureAwait(false);
                return new RefreshResult(true, checkedSession, null);
            }

            if (string.IsNullOrWhiteSpace(session.RefreshToken))
            {
                return RefreshResult.Failed("The refresh token is missing; open official login again.");
            }

            await storage.SaveBackupAsync(session, cancellationToken).ConfigureAwait(false);

            var path = CorrespondPath.Create(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var correspondUrl = $"https://www.bilibili.com/correspond/1/{path}";
            var html = await http
                .GetStringAsync(correspondUrl, cancellationToken)
                .ConfigureAwait(false);
            var match = Regex.Match(
                html,
                "<div[^>]+id=[\"']1-name[\"'][^>]*>(?<csrf>[^<]+)",
                RegexOptions.CultureInvariant);

            if (!match.Success)
            {
                return RefreshResult.Failed("Could not obtain refresh CSRF.");
            }

            var csrf = WebUtility.HtmlDecode(match.Groups["csrf"].Value);
            KeyValuePair<string, string>[] refreshForm =
            [
                new("csrf", session.Cookies["bili_jct"]),
                new("refresh_csrf", csrf),
                new("refresh_token", session.RefreshToken),
                new("source", "main_web")
            ];
            using var rotated = await http
                .PostFormAsync(RefreshEndpoint, refreshForm, cancellationToken)
                .ConfigureAwait(false);

            BiliJson.EnsureOk(rotated);

            var newToken = rotated.RootElement.GetProperty("data").String("refresh_token");
            var newCookies = ExtractCookies(http.CookieContainer);
            if (!newCookies.TryGetValue("bili_jct", out var newCsrf)
                || string.IsNullOrEmpty(newToken))
            {
                return RefreshResult.Failed("Refresh response did not return a complete session.");
            }

            KeyValuePair<string, string>[] confirmForm =
            [
                new("csrf", newCsrf),
                new("refresh_token", session.RefreshToken)
            ];
            using var confirmed = await http
                .PostFormAsync(ConfirmEndpoint, confirmForm, cancellationToken)
                .ConfigureAwait(false);

            BiliJson.EnsureOk(confirmed);

            var updated = session with
            {
                Cookies = newCookies,
                RefreshToken = newToken,
                LastRefreshCheck = today
            };
            await storage.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
            storage.DeleteBackup();
            return new RefreshResult(true, updated, null);
        }
        catch (Exception exception) when (exception is HttpRequestException
                                          or InvalidOperationException
                                          or JsonException
                                          or CryptographicException)
        {
            return RefreshResult.Failed(exception.Message);
        }
    }

    private static IReadOnlyDictionary<string, string> ExtractCookies(CookieContainer jar)
    {
        return jar.GetCookies(new Uri("https://www.bilibili.com"))
            .Cast<Cookie>()
            .ToDictionary(cookie => cookie.Name, cookie => cookie.Value, StringComparer.Ordinal);
    }

    internal static string CreateCorrespondPath(long unixMilliseconds)
    {
        return CorrespondPath.Create(unixMilliseconds);
    }

    private static class CorrespondPath
    {
        public static string Create(long timestamp)
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(PublicKey);

            var plaintext = $"refresh_{timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
            var encrypted = rsa.Encrypt(plaintextBytes, RSAEncryptionPadding.OaepSHA256);
            return Convert.ToHexString(encrypted).ToLowerInvariant();
        }
    }
}
