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
    public void Display_text_places_the_level_before_the_name()
    {
        Assert.Equal(" 17|测试团 ", FanMedalPresentation.GetDisplayText(17, "测试团"));
    }
}
