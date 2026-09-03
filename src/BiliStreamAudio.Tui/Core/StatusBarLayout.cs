namespace BiliStreamAudio.Tui.Core;

/// <summary>可在状态栏中排序的内容。每个内容最多只能出现在一个行中。</summary>
public enum StatusBarElement
{
    AudioStatus,
    ShortcutHints,
    WatchingDuration,
    DanmakuStatistics,
    GiftStatistics,
    SuperChatCount,
    GuardCount,
    Spectrum,
    RoomTitle,
    AnchorName,
    RoomId
}

public static class StatusBarLayout
{
    public static readonly IReadOnlyList<StatusBarElement> AllElements =
        Enum.GetValues<StatusBarElement>();

    public static readonly IReadOnlyList<StatusBarElement> DefaultFirstRow =
        [StatusBarElement.AudioStatus, StatusBarElement.ShortcutHints];

    public static (List<StatusBarElement> FirstRow, List<StatusBarElement> SecondRow) Normalize(
        IEnumerable<StatusBarElement>? firstRow,
        IEnumerable<StatusBarElement>? secondRow,
        bool useDefaultWhenEmpty = true)
    {
        var first = firstRow?.Where(IsDefined).Distinct().ToList() ?? [];
        var second = secondRow?.Where(IsDefined).Where(element => !first.Contains(element)).Distinct().ToList() ?? [];

        // Earlier versions did not contain layout fields. Preserve their one-line status bar.
        if (useDefaultWhenEmpty && first.Count == 0 && second.Count == 0)
        {
            first = [.. DefaultFirstRow];
        }

        return (first, second);
    }

    public static string GetDisplayName(StatusBarElement element) => element switch
    {
        StatusBarElement.AudioStatus => "音频流状态",
        StatusBarElement.ShortcutHints => "热键说明",
        StatusBarElement.WatchingDuration => "播放时长",
        StatusBarElement.DanmakuStatistics => "弹幕数量/流速",
        StatusBarElement.GiftStatistics => "礼物数量/金额",
        StatusBarElement.SuperChatCount => "SC 数量",
        StatusBarElement.GuardCount => "舰长数量",
        StatusBarElement.Spectrum => "频谱",
        StatusBarElement.RoomTitle => "直播间标题",
        StatusBarElement.AnchorName => "主播用户名",
        StatusBarElement.RoomId => "房间号",
        _ => element.ToString()
    };

    private static bool IsDefined(StatusBarElement element) => Enum.IsDefined(element);
}

/// <summary>状态栏渲染所需的、与 TUI 无关的当前值。</summary>
public sealed record StatusBarContent(
    int Volume,
    bool IsMuted,
    PlaybackState PlaybackState,
    bool IsMockMode,
    string ShortcutHints,
    LiveRoom? Room,
    LiveRoomStatisticsSnapshot Statistics)
{
    public static StatusBarContent Preview { get; } = new(
        70,
        false,
        PlaybackState.Playing,
        false,
        "Esc 关闭直播间 · r 刷新 · m 静音",
        new LiveRoom(12345, 12345, 67890, "示例直播间标题", "示例主播", true),
        new LiveRoomStatisticsSnapshot(TimeSpan.FromMinutes(12) + TimeSpan.FromSeconds(34), 128, 18, 6, 88.5m, 2, 3));
}

public static class StatusBarFormatter
{
    public static string Format(StatusBarElement element, StatusBarContent content) => element switch
    {
        StatusBarElement.AudioStatus => $"音量 {content.Volume}{(content.IsMuted ? " (静音)" : string.Empty)} · {content.PlaybackState.ToDisplayText()}{(content.IsMockMode ? " · 模拟模式" : string.Empty)}",
        StatusBarElement.ShortcutHints => content.ShortcutHints,
        StatusBarElement.WatchingDuration => $"时长 {FormatDuration(content.Statistics.WatchingDuration)}",
        StatusBarElement.DanmakuStatistics => $"弹幕 {content.Statistics.DanmakuCount} · {content.Statistics.DanmakuRatePerMinute}/分",
        StatusBarElement.GiftStatistics => $"礼物 {content.Statistics.GiftCount} · ¥{content.Statistics.GiftAmountCny:0.##}",
        StatusBarElement.SuperChatCount => $"SC {content.Statistics.SuperChatCount}",
        StatusBarElement.GuardCount => $"舰长 {content.Statistics.GuardCount}",
        StatusBarElement.RoomTitle => $"{content.Room?.Title ?? "未选择直播间"}",
        StatusBarElement.AnchorName => $"{content.Room?.Anchor ?? "--"}",
        StatusBarElement.RoomId => $"房间 {content.Room?.RoomId.ToString() ?? "--"}",
        StatusBarElement.Spectrum => "频谱",
        _ => string.Empty
    };

    public static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}"
            : $"{duration.Minutes:D2}:{duration.Seconds:D2}";
}
