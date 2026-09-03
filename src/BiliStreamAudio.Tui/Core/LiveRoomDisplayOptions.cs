namespace BiliStreamAudio.Tui.Core;

/// <summary>
/// 控制直播间消息的显示方式。
/// </summary>
public sealed class LiveRoomDisplayOptions
{
    /// <summary>
    /// 以逗号、分号或换行分隔的弹幕屏蔽词。
    /// </summary>
    public string DanmakuBlockedWords { get; set; } = string.Empty;

    /// <summary>
    /// 是否显示普通弹幕。
    /// </summary>
    public bool ShowDanmaku { get; set; } = true;

    /// <summary>
    /// 是否显示醒目留言（SC）。
    /// </summary>
    public bool ShowSuperChats { get; set; } = true;

    /// <summary>
    /// 是否显示礼物消息。
    /// </summary>
    public bool ShowGifts { get; set; } = true;

    /// <summary>
    /// 是否在礼物消息中显示人民币金额。
    /// </summary>
    public bool ShowGiftAmount { get; set; } = true;

    /// <summary>
    /// 是否在弹幕用户名之前渲染粉丝勋章。
    /// </summary>
    public bool ShowFanMedals { get; set; } = true;

    /// <summary>
    /// 判断一条普通弹幕是否应显示。屏蔽词不会影响 SC、礼物或其他系统消息。
    /// </summary>
    public bool IsDanmakuVisible(string message)
    {
        if (!ShowDanmaku)
        {
            return false;
        }

        return GetDanmakuBlockedWords().All(word =>
            !message.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<string> GetDanmakuBlockedWords() => DanmakuBlockedWords.Split(
        ['\r', '\n', ',', '，', ';', '；'],
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
