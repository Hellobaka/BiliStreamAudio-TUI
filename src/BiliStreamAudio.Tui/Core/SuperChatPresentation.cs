namespace BiliStreamAudio.Tui.Core;

public enum SuperChatTier
{
    LightBlue,
    Cyan,
    Gold,
    Red
}

public static class SuperChatPresentation
{
    public static SuperChatTier GetTier(int priceCny) => priceCny switch
    {
        <= 30 => SuperChatTier.LightBlue,
        <= 100 => SuperChatTier.Cyan,
        <= 1000 => SuperChatTier.Gold,
        _ => SuperChatTier.Red
    };

    public static TimeSpan GetLifetime(int priceCny) =>
        TimeSpan.FromSeconds(Math.Max(0, priceCny) * 2d);

    public static double GetRemainingFraction(
        DateTimeOffset now,
        DateTimeOffset startsAt,
        DateTimeOffset expiresAt)
    {
        var lifetime = expiresAt - startsAt;
        if (lifetime <= TimeSpan.Zero)
        {
            return 0;
        }

        return Math.Clamp((expiresAt - now).TotalSeconds / lifetime.TotalSeconds, 0, 1);
    }
}
