using BiliStreamAudio.Tui.Core;
using BiliStreamAudio.Tui.Infrastructure;

namespace BiliStreamAudio.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"bili-settings-{Guid.NewGuid():N}.db");

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
    public void Load_returns_defaults_when_no_settings_exist()
    {
        using var store = new SettingsStore(_dbPath);

        var settings = store.Load();

        Assert.Equal(70, settings.Volume);
        Assert.True(settings.ShowDanmaku);
        Assert.True(settings.ShowSuperChats);
        Assert.True(settings.ShowGifts);
        Assert.True(settings.ShowGuards);
        Assert.True(settings.ShowGiftAmount);
        Assert.True(settings.ShowFanMedals);
        Assert.Equal(8, settings.SpectrumBandCount);
        Assert.Equal("Rainbow", settings.SpectrumColorMode);
        Assert.Empty(settings.DanmakuBlockedList);
    }

    [Fact]
    public void Save_and_Load_round_trips_all_values()
    {
        using var store = new SettingsStore(_dbPath);

        var original = new AppSettings
        {
            Volume = 35,
            ShowDanmaku = false,
            ShowSuperChats = false,
            ShowGifts = false,
            ShowGuards = false,
            ShowGiftAmount = false,
            ShowFanMedals = false,
            SpectrumBandCount = 32,
            SpectrumColorMode = "SingleColor",
            DanmakuBlockedList = ["屏蔽词A", "屏蔽词B", "屏蔽词C"]
        };
        store.Save(original);

        var loaded = store.Load();

        Assert.Equal(35, loaded.Volume);
        Assert.False(loaded.ShowDanmaku);
        Assert.False(loaded.ShowSuperChats);
        Assert.False(loaded.ShowGifts);
        Assert.False(loaded.ShowGuards);
        Assert.False(loaded.ShowGiftAmount);
        Assert.False(loaded.ShowFanMedals);
        Assert.Equal(32, loaded.SpectrumBandCount);
        Assert.Equal("SingleColor", loaded.SpectrumColorMode);
        Assert.Equal(["屏蔽词A", "屏蔽词B", "屏蔽词C"], loaded.DanmakuBlockedList);
    }

    [Fact]
    public void Settings_persist_across_store_instances()
    {
        using (var store = new SettingsStore(_dbPath))
        {
            store.Save(new AppSettings
            {
                Volume = 25,
                ShowDanmaku = false,
                SpectrumBandCount = 16,
                DanmakuBlockedList = ["测试词"]
            });
        }

        using var reopened = new SettingsStore(_dbPath);
        var loaded = reopened.Load();

        Assert.Equal(25, loaded.Volume);
        Assert.False(loaded.ShowDanmaku);
        Assert.Equal(16, loaded.SpectrumBandCount);
        Assert.Equal(["测试词"], loaded.DanmakuBlockedList);
    }

    [Fact]
    public void Save_upserts_singleton_row()
    {
        using var store = new SettingsStore(_dbPath);

        store.Save(new AppSettings { ShowDanmaku = true, SpectrumBandCount = 4 });
        store.Save(new AppSettings { ShowDanmaku = false, SpectrumBandCount = 16 });

        var loaded = store.Load();
        Assert.False(loaded.ShowDanmaku);
        Assert.Equal(16, loaded.SpectrumBandCount);
    }

    [Fact]
    public void Load_returns_defaults_after_first_call_creates_row()
    {
        using var store = new SettingsStore(_dbPath);

        var first = store.Load();
        var second = store.Load();

        Assert.Equal(first.ShowDanmaku, second.ShowDanmaku);
        Assert.Equal(first.SpectrumBandCount, second.SpectrumBandCount);
    }

    [Fact]
    public void Danmaku_blocked_list_preserves_order()
    {
        using var store = new SettingsStore(_dbPath);

        store.Save(new AppSettings
        {
            DanmakuBlockedList = ["第三", "第一", "第二"]
        });

        var loaded = store.Load();
        Assert.Equal(["第三", "第一", "第二"], loaded.DanmakuBlockedList);
    }

    [Fact]
    public void Danmaku_blocked_list_handles_empty_list()
    {
        using var store = new SettingsStore(_dbPath);

        store.Save(new AppSettings
        {
            DanmakuBlockedList = ["要删除"]
        });
        store.Save(new AppSettings
        {
            DanmakuBlockedList = []
        });

        var loaded = store.Load();
        Assert.Empty(loaded.DanmakuBlockedList);
    }

    [Fact]
    public void Danmaku_blocked_list_handles_duplicate_words()
    {
        using var store = new SettingsStore(_dbPath);

        store.Save(new AppSettings
        {
            DanmakuBlockedList = ["重复词", "重复词", "不同词"]
        });

        var loaded = store.Load();
        Assert.Equal(["重复词", "重复词", "不同词"], loaded.DanmakuBlockedList);
    }

    [Fact]
    public void Dispose_can_be_called_multiple_times()
    {
        var store = new SettingsStore(_dbPath);
        store.Load();

        store.Dispose();
        store.Dispose();
    }
}

public sealed class DanmakuBlockedListTests
{
    [Fact]
    public void SyncBlockedListFromWords_parses_comma_separated_string()
    {
        var options = new LiveRoomDisplayOptions
        {
            DanmakuBlockedWords = "词A, 词B, 词C"
        };

        options.SyncBlockedListFromWords();

        Assert.Equal(["词A", "词B", "词C"], options.DanmakuBlockedList);
    }

    [Fact]
    public void SyncBlockedListFromWords_parses_semicolon_separated_string()
    {
        var options = new LiveRoomDisplayOptions
        {
            DanmakuBlockedWords = "词A;词B;词C"
        };

        options.SyncBlockedListFromWords();

        Assert.Equal(["词A", "词B", "词C"], options.DanmakuBlockedList);
    }

    [Fact]
    public void SyncBlockedListFromWords_parses_newline_separated_string()
    {
        var options = new LiveRoomDisplayOptions
        {
            DanmakuBlockedWords = "词A\n词B\n词C"
        };

        options.SyncBlockedListFromWords();

        Assert.Equal(["词A", "词B", "词C"], options.DanmakuBlockedList);
    }

    [Fact]
    public void SyncBlockedListFromWords_trims_whitespace()
    {
        var options = new LiveRoomDisplayOptions
        {
            DanmakuBlockedWords = "  词A ,  词B  , 词C  "
        };

        options.SyncBlockedListFromWords();

        Assert.Equal(["词A", "词B", "词C"], options.DanmakuBlockedList);
    }

    [Fact]
    public void SyncBlockedListFromWords_handles_empty_string()
    {
        var options = new LiveRoomDisplayOptions
        {
            DanmakuBlockedWords = string.Empty
        };

        options.SyncBlockedListFromWords();

        Assert.Empty(options.DanmakuBlockedList);
    }

    [Fact]
    public void SyncWordsFromBlockedList_joins_list_to_string()
    {
        var options = new LiveRoomDisplayOptions();
        options.DanmakuBlockedList = ["词A", "词B", "词C"];

        options.SyncWordsFromBlockedList();

        Assert.Equal("词A, 词B, 词C", options.DanmakuBlockedWords);
    }

    [Fact]
    public void SyncWordsFromBlockedList_handles_empty_list()
    {
        var options = new LiveRoomDisplayOptions();
        options.DanmakuBlockedList = [];

        options.SyncWordsFromBlockedList();

        Assert.Equal(string.Empty, options.DanmakuBlockedWords);
    }

    [Fact]
    public void Round_trip_preserves_blocked_words()
    {
        var options = new LiveRoomDisplayOptions();
        options.DanmakuBlockedList = ["屏蔽词1", "屏蔽词2", "屏蔽词3"];

        options.SyncWordsFromBlockedList();
        options.DanmakuBlockedList.Clear();
        options.SyncBlockedListFromWords();

        Assert.Equal(["屏蔽词1", "屏蔽词2", "屏蔽词3"], options.DanmakuBlockedList);
    }

    [Fact]
    public void IsDanmakuVisible_uses_blocked_words_after_sync()
    {
        var options = new LiveRoomDisplayOptions();
        options.DanmakuBlockedList = ["广告", "刷屏"];
        options.SyncWordsFromBlockedList();

        Assert.True(options.IsDanmakuVisible("正常弹幕"));
        Assert.False(options.IsDanmakuVisible("这是广告内容"));
        Assert.False(options.IsDanmakuVisible("刷屏刷屏"));
    }

    [Fact]
    public void IsDanmakuVisible_is_case_insensitive()
    {
        var options = new LiveRoomDisplayOptions();
        options.DanmakuBlockedList = ["spam"];
        options.SyncWordsFromBlockedList();

        Assert.False(options.IsDanmakuVisible("This is SPAM content"));
        Assert.False(options.IsDanmakuVisible("Spam message"));
    }
}
