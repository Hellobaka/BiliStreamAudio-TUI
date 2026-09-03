namespace BiliStreamAudio.Tui.Core;

/// <summary>
/// 控制直播间消息的显示方式。
/// </summary>
public sealed class LiveRoomDisplayOptions
{
    /// <summary>
    /// 是否在礼物消息中显示人民币金额。
    /// </summary>
    public bool ShowGiftAmount { get; set; } = true;
}
