using BiliStreamAudio.Tui.Core;

namespace BiliStreamAudio.Tui.Infrastructure;

public sealed class AccountInfoService(
    Func<AuthSession, BiliHttp>? httpFactory = null)
{
    private const string NavigationEndpoint = "https://api.bilibili.com/x/web-interface/nav";

    private readonly Func<AuthSession, BiliHttp> _httpFactory =
        httpFactory ?? (session => new BiliHttp(session));

    public async Task<AuthSession> PopulateAsync(
        AuthSession session,
        CancellationToken cancellationToken)
    {
        using var http = _httpFactory(session);
        using var response = await http
            .GetJsonAsync(NavigationEndpoint, cancellationToken)
            .ConfigureAwait(false);

        BiliJson.EnsureOk(response);
        var data = response.RootElement.GetProperty("data");
        var userName = data.String("uname");
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new InvalidOperationException("登录成功，但未能获取账户用户名。");
        }

        var returnedUserId = data.Int64("mid");
        return session with
        {
            UserId = returnedUserId != 0 ? returnedUserId : session.UserId,
            UserName = userName
        };
    }
}
