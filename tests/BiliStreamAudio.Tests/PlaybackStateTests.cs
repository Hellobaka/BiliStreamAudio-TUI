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
}
