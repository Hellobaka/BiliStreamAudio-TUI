using BiliStreamAudio.Tui.Core;
using BiliStreamAudio.Tui.Infrastructure;
using BiliStreamAudio.Tui.Views;

namespace BiliStreamAudio.Tests;

public sealed class StatusBarLayoutTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"bili-status-bar-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        try
        {
            File.Delete(_dbPath);
        }
        catch (IOException)
        {
            // Best-effort cleanup of the temporary database file.
        }
    }

    [Fact]
    public void Empty_legacy_layout_uses_original_single_row_defaults()
    {
        var layout = StatusBarLayout.Normalize([], []);

        Assert.Equal([StatusBarElement.AudioStatus, StatusBarElement.ShortcutHints], layout.FirstRow);
        Assert.Empty(layout.SecondRow);
    }

    [Fact]
    public void Layout_normalization_preserves_order_and_removes_global_duplicates()
    {
        var layout = StatusBarLayout.Normalize(
            [StatusBarElement.RoomTitle, StatusBarElement.AudioStatus, StatusBarElement.RoomTitle],
            [StatusBarElement.AudioStatus, StatusBarElement.Spectrum, StatusBarElement.Spectrum]);

        Assert.Equal([StatusBarElement.RoomTitle, StatusBarElement.AudioStatus], layout.FirstRow);
        Assert.Equal([StatusBarElement.Spectrum], layout.SecondRow);
    }

    [Fact]
    public void Store_loads_old_settings_with_default_layout()
    {
        using var store = new SettingsStore(_dbPath);
        store.Save(new AppSettings { StatusBarLayoutVersion = null, StatusBarFirstRow = [], StatusBarSecondRow = [] });

        var loaded = store.Load();

        Assert.Equal(StatusBarLayout.DefaultFirstRow, loaded.StatusBarFirstRow);
        Assert.Empty(loaded.StatusBarSecondRow);
        Assert.Equal(1, loaded.StatusBarLayoutVersion);
    }

    [Fact]
    public void Store_preserves_two_row_order_and_an_explicitly_empty_layout()
    {
        using (var store = new SettingsStore(_dbPath))
        {
            store.Save(new AppSettings
            {
                StatusBarLayoutVersion = 1,
                StatusBarFirstRow = [StatusBarElement.Spectrum, StatusBarElement.RoomTitle],
                StatusBarSecondRow = [StatusBarElement.GiftStatistics, StatusBarElement.WatchingDuration]
            });
        }

        using var reopened = new SettingsStore(_dbPath);
        var loaded = reopened.Load();
        Assert.Equal([StatusBarElement.Spectrum, StatusBarElement.RoomTitle], loaded.StatusBarFirstRow);
        Assert.Equal([StatusBarElement.GiftStatistics, StatusBarElement.WatchingDuration], loaded.StatusBarSecondRow);
    }

    [Fact]
    public void Explicit_empty_rows_remain_empty()
    {
        var layout = StatusBarLayout.Normalize([], [], useDefaultWhenEmpty: false);

        Assert.Empty(layout.FirstRow);
        Assert.Empty(layout.SecondRow);
    }

    [Fact]
    public void Store_preserves_an_explicitly_empty_layout()
    {
        using (var store = new SettingsStore(_dbPath))
        {
            store.Save(new AppSettings
            {
                StatusBarLayoutVersion = 1,
                StatusBarFirstRow = [],
                StatusBarSecondRow = []
            });
        }

        using var reopened = new SettingsStore(_dbPath);
        var loaded = reopened.Load();
        Assert.Empty(loaded.StatusBarFirstRow);
        Assert.Empty(loaded.StatusBarSecondRow);
    }

    [Fact]
    public void Formatter_uses_zero_room_statistics_and_placeholders_without_a_room()
    {
        var content = new StatusBarContent(25, true, PlaybackState.Stopped, false, "快捷键", null,
            new LiveRoomStatisticsSnapshot(TimeSpan.Zero, 0, 0, 0, 0m, 0, 0));

        Assert.Equal("音量 25 (静音) · 已停止", StatusBarFormatter.Format(StatusBarElement.AudioStatus, content));
        Assert.Equal("时长 00:00", StatusBarFormatter.Format(StatusBarElement.WatchingDuration, content));
        Assert.Equal("弹幕 0 · 0/分", StatusBarFormatter.Format(StatusBarElement.DanmakuStatistics, content));
        Assert.Equal("礼物 0 · ¥0", StatusBarFormatter.Format(StatusBarElement.GiftStatistics, content));
        Assert.Equal("未选择直播间", StatusBarFormatter.Format(StatusBarElement.RoomTitle, content));
    }

    [Fact]
    public void Formatter_formats_duration_amount_and_room_metadata()
    {
        var content = StatusBarContent.Preview;

        Assert.Equal("时长 12:34", StatusBarFormatter.Format(StatusBarElement.WatchingDuration, content));
        Assert.Equal("礼物 6 · ¥88.5", StatusBarFormatter.Format(StatusBarElement.GiftStatistics, content));
        Assert.Equal("示例主播", StatusBarFormatter.Format(StatusBarElement.AnchorName, content));
        Assert.Equal("12345", StatusBarFormatter.Format(StatusBarElement.RoomId, content));
    }

    [Fact]
    public void Narrow_text_is_truncated_with_ellipsis()
    {
        Assert.Equal("直播…", SpectrumStatusBarView.FitToColumns("直播间标题很长", 5));
    }
}
