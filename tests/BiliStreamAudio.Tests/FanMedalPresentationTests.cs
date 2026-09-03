using BiliStreamAudio.Tui.Core;

namespace BiliStreamAudio.Tests;

public sealed class FanMedalPresentationTests
{
    [Theory]
    [InlineData(0, "#5D968F")]
    [InlineData(4, "#5D968F")]
    [InlineData(5, "#5D7B9E")]
    [InlineData(9, "#8D7CA6")]
    [InlineData(13, "#BD6686")]
    [InlineData(17, "#C79D23")]
    [InlineData(21, "#499388")]
    [InlineData(25, "#5B79DE")]
    [InlineData(29, "#7465C0")]
    [InlineData(33, "#BF5583")]
    [InlineData(37, "#FEA858")]
    public void Background_color_follows_fan_medal_level_boundaries(int level, string expected)
    {
        Assert.Equal(expected, FanMedalPresentation.GetBackgroundColor(level));
    }

    [Fact]
    public void Fan_medals_are_rendered_by_default()
    {
        Assert.True(new LiveRoomDisplayOptions().ShowFanMedals);
    }

    [Fact]
    public void Live_messages_are_displayed_by_default()
    {
        var options = new LiveRoomDisplayOptions();

        Assert.True(options.ShowDanmaku);
        Assert.True(options.ShowSuperChats);
        Assert.True(options.ShowGifts);
        Assert.True(options.IsDanmakuVisible("普通弹幕"));
    }

    [Fact]
    public void Blocked_words_hide_only_matching_danmaku()
    {
        var options = new LiveRoomDisplayOptions
        {
            DanmakuBlockedWords = "广告，剧透\nspam"
        };

        Assert.False(options.IsDanmakuVisible("这是广告"));
        Assert.False(options.IsDanmakuVisible("包含 SPAM 的内容"));
        Assert.True(options.IsDanmakuVisible("正常聊天"));
    }

    [Fact]
    public void Hiding_danmaku_overrides_blocked_word_configuration()
    {
        var options = new LiveRoomDisplayOptions
        {
            ShowDanmaku = false,
            DanmakuBlockedWords = "广告"
        };

        Assert.False(options.IsDanmakuVisible("正常聊天"));
    }

    [Fact]
    public void Display_text_places_the_level_before_the_name()
    {
        Assert.Equal(" 17|测试团 ", FanMedalPresentation.GetDisplayText(17, "测试团"));
    }
}
