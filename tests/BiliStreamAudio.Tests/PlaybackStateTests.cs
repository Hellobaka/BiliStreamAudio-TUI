using BiliStreamAudio.Tui.Core;

namespace BiliStreamAudio.Tests;

public sealed class PlaybackStateTests
{
    [Theory]
    [InlineData(PlaybackState.Stopped, "已停止")]
    [InlineData(PlaybackState.Buffering, "缓冲中")]
    [InlineData(PlaybackState.Playing, "播放中")]
    [InlineData(PlaybackState.Error, "播放错误")]
    public void To_display_text_returns_Chinese_playback_state(PlaybackState state, string expected)
    {
        Assert.Equal(expected, state.ToDisplayText());
    }

    [Theory]
    [InlineData("登录失败。", "登录失败。")]
    [InlineData("The remote server returned an error.", "操作失败，请稍后重试。")]
    public void Exception_display_text_uses_Chinese_message_or_a_Chinese_fallback(string message, string expected)
    {
        Assert.Equal(expected, new InvalidOperationException(message).ToDisplayText());
    }

    [Fact]
    public void Exception_display_text_translates_network_failures()
    {
        Assert.Equal(
            "网络请求失败，请检查网络连接后重试。",
            new HttpRequestException("Connection refused").ToDisplayText());
    }
}
