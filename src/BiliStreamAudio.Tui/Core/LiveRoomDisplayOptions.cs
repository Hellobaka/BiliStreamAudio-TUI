namespace BiliStreamAudio.Tui.Core;

public enum SpectrumColorMode
{
    SingleColor,
    Rainbow
}

/// <summary>
/// 控制直播间消息的显示方式。
/// </summary>
public sealed class LiveRoomDisplayOptions
{
    public const int MinimumSpectrumBandCount = 1;
    public const int MaximumSpectrumBandCount = 64;

    private int _spectrumBandCount = 8;

    /// <summary>
    /// 以逗号、分号或换行分隔的弹幕屏蔽词。
    /// </summary>
    public string DanmakuBlockedWords { get; set; } = string.Empty;

    /// <summary>
    /// 屏蔽词列表形式，与 <see cref="DanmakuBlockedWords"/> 双向同步。
    /// </summary>
    public List<string> DanmakuBlockedList { get; set; } = [];

    /// <summary>
    /// 从 <see cref="DanmakuBlockedWords"/> 解析屏蔽词列表。
    /// </summary>
    public void SyncBlockedListFromWords()
    {
        DanmakuBlockedList = [.. GetDanmakuBlockedWords()];
    }

    /// <summary>
    /// 将 <see cref="DanmakuBlockedList"/> 序列化回 <see cref="DanmakuBlockedWords"/>。
    /// </summary>
    public void SyncWordsFromBlockedList()
    {
        DanmakuBlockedWords = string.Join(", ", DanmakuBlockedList);
    }

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
    /// 是否显示上舰消息。
    /// </summary>
    public bool ShowGuards { get; set; } = true;

    /// <summary>
    /// 是否在礼物消息中显示人民币金额。
    /// </summary>
    public bool ShowGiftAmount { get; set; } = true;

    /// <summary>
    /// 是否在弹幕用户名之前渲染粉丝勋章。
    /// </summary>
    public bool ShowFanMedals { get; set; } = true;

    /// <summary>
    /// 状态栏频谱显示的段数。实际绘制时会根据终端宽度自动缩减。
    /// </summary>
    public int SpectrumBandCount
    {
        get => _spectrumBandCount;
        set => _spectrumBandCount = Math.Clamp(value, MinimumSpectrumBandCount, MaximumSpectrumBandCount);
    }

    /// <summary>
    /// 状态栏频谱的颜色模式。
    /// </summary>
    public SpectrumColorMode SpectrumColorMode { get; set; } = SpectrumColorMode.Rainbow;

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

    public IEnumerable<string> GetDanmakuBlockedWords() => DanmakuBlockedWords.Split(
        ['\r', '\n', ',', '，', ';', '；'],
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
