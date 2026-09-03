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

    /// <summary>
    /// 是否在弹幕用户名之前渲染粉丝勋章。
    /// </summary>
    public bool ShowFanMedals { get; set; } = true;
}
