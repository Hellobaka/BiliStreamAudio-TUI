namespace BiliStreamAudio.Tui.Core;

/// <summary>
/// 粉丝勋章在终端中的显示规则。
/// </summary>
public static class FanMedalPresentation
{
    public static string GetDisplayText(int level, string name) => $" {level}|{name} ";

    public static string GetBackgroundColor(int level) => level switch
    {
        < 5 => "#5D968F",
        < 9 => "#5D7B9E",
        < 13 => "#8D7CA6",
        < 17 => "#BD6686",
        < 21 => "#C79D23",
        < 25 => "#499388",
        < 29 => "#5B79DE",
        < 33 => "#7465C0",
        < 37 => "#BF5583",
        _ => "#FEA858"
    };
}
