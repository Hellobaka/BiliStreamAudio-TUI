using System.IO.Compression;
using System.Text;
using BiliStreamAudio.Tui.Core;
using BiliStreamAudio.Tui.Infrastructure;

namespace BiliStreamAudio.Tests;

public sealed class ProtocolTests
{
    [Fact]
    public void Stream_url_joins_host_path_and_extra_once()
    {
        var url = StreamUrl.Build("https://cdn.example.test/", "/live/audio.m3u8", "?token=abc");
        Assert.Equal("https://cdn.example.test/live/audio.m3u8?token=abc", url?.ToString());
    }

    [Fact]
    public void Stream_url_deduplicates_query_separators_from_api_parts()
    {
        var url = StreamUrl.Build(
            "https://cdn.example.test/",
            "/live/audio.m3u8?",
            "?expires=123&sign=abc");

        Assert.Equal(
            "https://cdn.example.test/live/audio.m3u8?expires=123&sign=abc",
            url?.ToString());
    }

    [Fact]
    public void Wbi_signature_is_canonical_and_filters_reserved_characters()
    {
        KeyValuePair<string, string>[] parameters =
        [
            new("b", "hello!'()*"),
            new("a", "1")
        ];
        var signature = WbiSigner.Sign(
            parameters,
            "abcdefghijklmnopqrstuvwxyz123456",
            "ZYXWVUTSRQPONMLKJIHGFEDCBA654321",
            1_700_000_000);

        Assert.StartsWith("a=1&b=hello&wts=1700000000&w_rid=", signature, StringComparison.Ordinal);
        Assert.Equal(32, signature.Split("w_rid=")[1].Length);
    }

    [Fact]
    public void Vlc_media_options_include_http_playback_context()
    {
        var stream = new StreamDescriptor(
            new Uri("https://cdn.example.test/live.m3u8"),
            "http_hls",
            "fmp4",
            80,
            true,
            26044264);
        var options = VlcRequestOptions.Create(stream);

        Assert.Contains(options, option => option.StartsWith(":http-user-agent=Mozilla/5.0", StringComparison.Ordinal));
        Assert.Contains(":http-referrer=https://live.bilibili.com/26044264", options);
        Assert.Contains(":http-forward-cookies", options);
        Assert.Contains(":network-caching=500", options);
        Assert.Contains(":adaptive-livedelay=2000", options);
        Assert.Contains(":adaptive-maxbuffer=2000", options);
        Assert.Contains(":adaptive-lowlatency=1", options);
    }

    [Fact]
    public void Playback_readiness_waits_for_an_audio_track_and_running_clock()
    {
        var readiness = new PlaybackReadiness();

        Assert.Equal(PlaybackState.Buffering, readiness.OnBuffering(0));
        Assert.Null(readiness.OnPlaying());
        Assert.Null(readiness.OnAudioTrackSelected());
        Assert.Equal(PlaybackState.Playing, readiness.OnTimeChanged(0));
        Assert.Equal(PlaybackState.Playing, readiness.OnBuffering(0));
    }

    [Fact]
    public void Vlc_log_sanitizer_removes_cookies_and_signed_queries()
    {
        var cookieLog = VlcLogSanitizer.Sanitize(
            "Sending Cookie SESSDATA=secret; bili_jct=csrf-secret");
        var urlLog = VlcLogSanitizer.Sanitize(
            "GET https://cdn.example.test/live.m3u8?token=secret&expires=123 HTTP/1.1");
        var relativeRequestLog = VlcLogSanitizer.Sanitize(
            "GET /live/audio.m3u8??token=secret&expires=123 HTTP/1.1");

        Assert.DoesNotContain("secret", cookieLog, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", urlLog, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", relativeRequestLog, StringComparison.Ordinal);
        Assert.Contains("Cookie: <redacted>", cookieLog, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://cdn.example.test/live.m3u8?<redacted>", urlLog, StringComparison.Ordinal);
        Assert.Contains("GET /live/audio.m3u8?<redacted>", relativeRequestLog, StringComparison.Ordinal);
    }

    [Fact]
    public void Parses_zlib_wrapped_danmaku_frame()
    {
        var json = Encoding.UTF8.GetBytes("{\"cmd\":\"DANMU_MSG\",\"info\":[[],\"你好\",[1,\"Alice\"]]}");
        var inner = DanmakuProtocol.Frame(5, json);
        using var target = new MemoryStream();
        using (var zlib = new ZLibStream(target, CompressionLevel.SmallestSize, true))
        {
            zlib.Write(inner);
        }

        var events = DanmakuProtocol.Parse(DanmakuProtocol.Frame(5, target.ToArray(), 2));
        var item = Assert.IsType<DanmakuEvent>(Assert.Single(events));
        Assert.Equal("Alice", item.UserName);
        Assert.Equal("你好", item.Message);
    }

    [Fact]
    public void Parses_brotli_wrapped_danmaku_frame()
    {
        var json = Encoding.UTF8.GetBytes("{\"cmd\":\"DANMU_MSG:4:0:2:2:2:0\",\"info\":[[],\"hi\",[1,\"Bob\"]]}");
        var inner = DanmakuProtocol.Frame(5, json);
        using var target = new MemoryStream();
        using (var brotli = new BrotliStream(target, CompressionLevel.SmallestSize, true))
        {
            brotli.Write(inner);
        }

        var events = DanmakuProtocol.Parse(DanmakuProtocol.Frame(5, target.ToArray(), 3));
        Assert.Equal("Bob", Assert.IsType<DanmakuEvent>(Assert.Single(events)).UserName);
    }

    [Fact]
    public void Parses_danmaku_fan_medal_from_legacy_and_nested_user_data()
    {
        var json = Encoding.UTF8.GetBytes(
            """
            {
              "cmd": "DANMU_MSG",
              "send_time": "1750000000456",
              "info": [
                [
                  0, 1, 25, 16777215, 1750000000123, -1, 0, "", 0, 0, 0, "", 0, "", "",
                  {
                    "user": {
                      "uid": "6088969",
                      "base": { "name": "新版用户名" },
                      "medal": {
                        "id": 1279130,
                        "name": "果咩吖",
                        "level": 29,
                        "ruid": "3546569288714792",
                        "is_light": 1,
                        "guard_level": 1,
                        "color": 0,
                        "color_start": 2951253,
                        "color_end": 10329087,
                        "color_border": 16771156,
                        "guard_icon": "https://example.test/guard.png",
                        "honor_icon": "",
                        "v2_medal_color_start": "#9660E5CC",
                        "v2_medal_color_end": "#9660E5CC",
                        "v2_medal_color_border": "#D47AFFFF",
                        "v2_medal_color_text": "#FFFFFFFF",
                        "v2_medal_color_level": "#6C00A099"
                      }
                    }
                  }
                ],
                "测试弹幕",
                [6088968, "旧版用户名"],
                [29, "果咩吖", "果宝Official", 31180317, 2951253, "", 0, 16771156, 2951253, 10329087, 1, 1, 3546569288714792]
              ]
            }
            """);

        var danmaku = Assert.IsType<DanmakuEvent>(Assert.Single(
            DanmakuProtocol.Parse(DanmakuProtocol.Frame(5, json))));
        var medal = Assert.IsType<FanMedal>(danmaku.Medal);

        Assert.Equal(6088969, danmaku.UserId);
        Assert.Equal("新版用户名", danmaku.UserName);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1750000000456), danmaku.ReceivedAt);
        Assert.Equal(1279130, medal.Id);
        Assert.Equal("果咩吖", medal.Name);
        Assert.Equal(29, medal.Level);
        Assert.Equal(3546569288714792, medal.AnchorUserId);
        Assert.Equal("果宝Official", medal.AnchorName);
        Assert.Equal(31180317, medal.AnchorRoomId);
        Assert.True(medal.IsLighted);
        Assert.Equal(1, medal.GuardLevel);
        Assert.Equal(2951253, medal.Color);
        Assert.Equal(16771156, medal.ColorBorder);
        Assert.Equal("#9660E5CC", medal.ColorStartV2);
        Assert.Equal("https://example.test/guard.png", medal.GuardIcon);
    }

    [Fact]
    public void Danmaku_without_a_worn_fan_medal_has_null_medal()
    {
        var json = Encoding.UTF8.GetBytes(
            """
            {
              "cmd": "DANMU_MSG",
              "info": [
                [0, 1, 25, 16777215, 1750000000123],
                "无勋章弹幕",
                [42, "普通用户"],
                []
              ]
            }
            """);

        var danmaku = Assert.IsType<DanmakuEvent>(Assert.Single(
            DanmakuProtocol.Parse(DanmakuProtocol.Frame(5, json))));

        Assert.Equal(42, danmaku.UserId);
        Assert.Null(danmaku.Medal);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1750000000123), danmaku.ReceivedAt);
    }

    [Fact]
    public void Parses_paid_gift_with_string_or_number_fields()
    {
        var json = Encoding.UTF8.GetBytes(
            """
            {
              "cmd": "SEND_GIFT",
              "data": {
                "uid": "510149209",
                "uname": "送礼用户",
                "giftId": 31036,
                "giftName": "小花花",
                "num": "2",
                "price": 100,
                "total_coin": "200",
                "coin_type": "gold",
                "tid": "1673622464121900003",
                "batch_combo_id": "batch:gift:1",
                "timestamp": 1673622464
              }
            }
            """);

        var gift = Assert.IsType<GiftEvent>(Assert.Single(
            DanmakuProtocol.Parse(DanmakuProtocol.Frame(5, json))));

        Assert.Equal(510149209, gift.UserId);
        Assert.Equal("小花花", gift.GiftName);
        Assert.Equal(2, gift.Count);
        Assert.Equal("1673622464121900003", gift.EventId);
        Assert.True(gift.IsPaid);
        Assert.Equal(0.2m, gift.AmountCny);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1673622464), gift.ReceivedAt);
    }

    [Fact]
    public void Parses_gift_combo_as_cumulative_notification()
    {
        var json = Encoding.UTF8.GetBytes(
            """
            {
              "cmd": "COMBO_SEND",
              "data": {
                "uid": 42,
                "uname": "连击用户",
                "gift_id": 31036,
                "gift_name": "小花花",
                "combo_num": "3",
                "combo_total_coin": 300,
                "combo_id": "gift:combo:1",
                "batch_combo_id": "batch:combo:1"
              }
            }
            """);

        var combo = Assert.IsType<GiftComboEvent>(Assert.Single(
            DanmakuProtocol.Parse(DanmakuProtocol.Frame(5, json))));

        Assert.Equal(3, combo.TotalCount);
        Assert.Equal(300, combo.TotalCoin);
        Assert.Equal("gift:combo:1", combo.ComboId);
    }

    [Fact]
    public void Parses_japanese_super_chat_and_preserves_source_command()
    {
        var json = Encoding.UTF8.GetBytes(
            """
            {
              "cmd": "SUPER_CHAT_MESSAGE_JPN",
              "data": {
                "id": "3790747",
                "uid": "394060741",
                "message": "原文",
                "message_trans": "译文",
                "message_jpn": "日本語",
                "price": 30,
                "time": 60,
                "start_time": 1650363318,
                "end_time": 1650363378,
                "user_info": { "uname": "SC用户" }
              }
            }
            """);

        var superChat = Assert.IsType<SuperChatEvent>(Assert.Single(
            DanmakuProtocol.Parse(DanmakuProtocol.Frame(5, json))));

        Assert.Equal("SUPER_CHAT_MESSAGE_JPN", superChat.Type);
        Assert.Equal("3790747", superChat.Id);
        Assert.Equal(394060741, superChat.UserId);
        Assert.Equal("SC用户", superChat.UserName);
        Assert.Equal("日本語", superChat.JapaneseMessage);
        Assert.Equal(30, superChat.PriceCny);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1650363378), superChat.EndsAt);
    }

    [Fact]
    public void Parses_super_chat_delete_with_mixed_id_types()
    {
        var json = Encoding.UTF8.GetBytes(
            """
            {
              "cmd": "SUPER_CHAT_MESSAGE_DELETE",
              "data": { "ids": [3897503, "3897504"] }
            }
            """);

        var deleted = Assert.IsType<SuperChatDeleteEvent>(Assert.Single(
            DanmakuProtocol.Parse(DanmakuProtocol.Frame(5, json))));

        Assert.Equal(["3897503", "3897504"], deleted.Ids);
    }

    [Fact]
    public void Parses_guard_purchase_and_converts_gold_coin_price()
    {
        var json = Encoding.UTF8.GetBytes(
            """
            {
              "cmd": "GUARD_BUY",
              "data": {
                "uid": 14225357,
                "username": "舰长用户",
                "guard_level": 3,
                "num": 1,
                "price": 198000,
                "gift_id": 10003,
                "gift_name": "舰长",
                "start_time": 1677069035
              }
            }
            """);

        var guard = Assert.IsType<GuardPurchaseEvent>(Assert.Single(
            DanmakuProtocol.Parse(DanmakuProtocol.Frame(5, json))));

        Assert.Equal(3, guard.GuardLevel);
        Assert.Equal("舰长", guard.GiftName);
        Assert.Equal(198m, guard.AmountCny);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1677069035), guard.ReceivedAt);
    }
}
