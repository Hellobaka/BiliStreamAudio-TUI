using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BiliStreamAudio.Tui.Core;

namespace BiliStreamAudio.Tui.Infrastructure;

public static class DanmakuProtocol
{
    public const int HeaderLength = 16;

    private const int MessageOperation = 5;
    private const short ZlibVersion = 2;
    private const short BrotliVersion = 3;

    public static byte[] Frame(int operation, ReadOnlySpan<byte> body, short version = 1)
    {
        var result = new byte[HeaderLength + body.Length];
        BinaryPrimitives.WriteInt32BigEndian(result, result.Length);
        BinaryPrimitives.WriteInt16BigEndian(result.AsSpan(4), HeaderLength);
        BinaryPrimitives.WriteInt16BigEndian(result.AsSpan(6), version);
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(8), operation);
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(12), 1);
        body.CopyTo(result.AsSpan(HeaderLength));
        return result;
    }

    public static IReadOnlyList<LiveEvent> Parse(ReadOnlySpan<byte> packet)
    {
        var events = new List<LiveEvent>();
        ParseFrames(packet, events);
        return events;
    }

    private static void ParseFrames(ReadOnlySpan<byte> bytes, List<LiveEvent> events)
    {
        var offset = 0;
        while (offset + HeaderLength <= bytes.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(bytes[offset..]);
            var header = BinaryPrimitives.ReadInt16BigEndian(bytes[(offset + 4)..]);
            var version = BinaryPrimitives.ReadInt16BigEndian(bytes[(offset + 6)..]);
            var operation = BinaryPrimitives.ReadInt32BigEndian(bytes[(offset + 8)..]);

            var invalidLength = length < header
                || header < HeaderLength
                || length > bytes.Length - offset;
            if (invalidLength)
            {
                break;
            }

            var body = bytes.Slice(offset + header, length - header);
            if (version is ZlibVersion or BrotliVersion)
            {
                ParseFrames(Decompress(body, version == BrotliVersion), events);
            }
            else if (operation == MessageOperation && body.Length > 0)
            {
                ParseJson(body, events);
            }

            offset += length;
        }
    }

    private static byte[] Decompress(ReadOnlySpan<byte> input, bool brotli)
    {
        using var source = new MemoryStream(input.ToArray());
        using Stream compressed = brotli
            ? new BrotliStream(source, CompressionMode.Decompress)
            : new ZLibStream(source, CompressionMode.Decompress);
        using var target = new MemoryStream();

        compressed.CopyTo(target);
        return target.ToArray();
    }

    private static void ParseJson(ReadOnlySpan<byte> body, List<LiveEvent> events)
    {
        try
        {
            using var json = JsonDocument.Parse(body.ToArray());
            var root = json.RootElement;
            var command = String(root, "cmd").Split(':')[0];
            var receivedAt = DateTimeOffset.Now;
            LiveEvent? item = command switch
            {
                "DANMU_MSG" => ParseDanmaku(root, command, receivedAt),
                "SEND_GIFT" => ParseGift(root, command, receivedAt),
                "COMBO_SEND" => ParseGiftCombo(root, command, receivedAt),
                "SUPER_CHAT_MESSAGE" or "SUPER_CHAT_MESSAGE_JPN" =>
                    ParseSuperChat(root, command, receivedAt),
                "SUPER_CHAT_MESSAGE_DELETE" => ParseSuperChatDelete(root, command, receivedAt),
                "GUARD_BUY" => ParseGuardPurchase(root, command, receivedAt),
                _ => null
            };

            if (item is not null)
            {
                events.Add(item);
            }
        }
        catch (JsonException)
        {
            // Ignore unknown or malformed platform events.
        }
    }

    private static DanmakuEvent? ParseDanmaku(
        JsonElement root,
        string command,
        DateTimeOffset receivedAt)
    {
        if (!TryProperty(root, "info", out var info)
            || !TryArrayItem(info, 1, out var messageElement))
        {
            return null;
        }

        var message = ScalarString(messageElement);
        var userId = 0L;
        var userName = string.Empty;
        if (TryArrayItem(info, 2, out var legacyUser))
        {
            if (TryArrayItem(legacyUser, 0, out var userIdElement))
            {
                userId = ScalarInt64(userIdElement);
            }
            if (TryArrayItem(legacyUser, 1, out var userNameElement))
            {
                userName = ScalarString(userNameElement);
            }
        }

        JsonElement nestedUser = default;
        if (TryDanmakuUser(info, out nestedUser))
        {
            var nestedUserId = Int64(nestedUser, "uid");
            if (nestedUserId != 0)
            {
                userId = nestedUserId;
            }

            if (TryObject(nestedUser, "base", out var userBase))
            {
                var nestedUserName = String(userBase, "name");
                if (!string.IsNullOrEmpty(nestedUserName))
                {
                    userName = nestedUserName;
                }
            }
        }

        var sentAt = UnixMilliseconds(root, "send_time", receivedAt);
        if (sentAt == receivedAt
            && TryArrayItem(info, 0, out var danmakuInfo)
            && TryArrayItem(danmakuInfo, 4, out var sentAtElement)
            && TryUnixMilliseconds(ScalarInt64(sentAtElement), out var parsedSentAt))
        {
            sentAt = parsedSentAt;
        }

        return new DanmakuEvent(
            string.IsNullOrEmpty(userName) ? "匿名用户" : userName,
            message,
            sentAt,
            command,
            userId,
            ParseFanMedal(info, nestedUser));
    }

    private static FanMedal? ParseFanMedal(JsonElement info, JsonElement nestedUser)
    {
        var id = 0L;
        var name = string.Empty;
        var level = 0;
        var anchorUserId = 0L;
        var anchorName = string.Empty;
        var anchorRoomId = 0L;
        var isLighted = false;
        var guardLevel = 0;
        var color = 0;
        var colorStart = 0;
        var colorEnd = 0;
        var colorBorder = 0;
        var guardIcon = string.Empty;
        var honorIcon = string.Empty;
        var colorStartV2 = string.Empty;
        var colorEndV2 = string.Empty;
        var colorBorderV2 = string.Empty;
        var textColorV2 = string.Empty;
        var levelColorV2 = string.Empty;

        var hasLegacyMedal = TryArrayItem(info, 3, out var legacyMedal)
            && legacyMedal.ValueKind == JsonValueKind.Array
            && legacyMedal.GetArrayLength() > 0;
        if (hasLegacyMedal)
        {
            level = ArrayInt32(legacyMedal, 0);
            name = ArrayString(legacyMedal, 1);
            anchorName = ArrayString(legacyMedal, 2);
            anchorRoomId = ArrayInt64(legacyMedal, 3);
            color = ArrayInt32(legacyMedal, 4);
            colorBorder = ArrayInt32(legacyMedal, 7);
            colorStart = ArrayInt32(legacyMedal, 8);
            colorEnd = ArrayInt32(legacyMedal, 9);
            guardLevel = ArrayInt32(legacyMedal, 10);
            isLighted = ArrayInt32(legacyMedal, 11) != 0;
            anchorUserId = ArrayInt64(legacyMedal, 12);
        }

        JsonElement nestedMedal = default;
        var hasNestedMedal = nestedUser.ValueKind == JsonValueKind.Object
            && TryObject(nestedUser, "medal", out nestedMedal);
        if (hasNestedMedal)
        {
            id = Int64(nestedMedal, "id");
            name = Prefer(String(nestedMedal, "name"), name);
            level = Int32(nestedMedal, "level");
            anchorUserId = Int64(nestedMedal, "ruid");
            isLighted = Int32(nestedMedal, "is_light") != 0;
            guardLevel = Int32(nestedMedal, "guard_level");

            var nestedColor = Int32(nestedMedal, "color");
            color = nestedColor == 0 ? color : nestedColor;
            colorStart = Int32(nestedMedal, "color_start");
            colorEnd = Int32(nestedMedal, "color_end");
            colorBorder = Int32(nestedMedal, "color_border");
            guardIcon = String(nestedMedal, "guard_icon");
            honorIcon = String(nestedMedal, "honor_icon");
            colorStartV2 = String(nestedMedal, "v2_medal_color_start");
            colorEndV2 = String(nestedMedal, "v2_medal_color_end");
            colorBorderV2 = String(nestedMedal, "v2_medal_color_border");
            textColorV2 = String(nestedMedal, "v2_medal_color_text");
            levelColorV2 = String(nestedMedal, "v2_medal_color_level");
        }

        if (!hasLegacyMedal && !hasNestedMedal)
        {
            return null;
        }

        return new FanMedal(
            id,
            name,
            level,
            anchorUserId,
            anchorName,
            anchorRoomId,
            isLighted,
            guardLevel,
            color,
            colorStart,
            colorEnd,
            colorBorder,
            guardIcon,
            honorIcon,
            colorStartV2,
            colorEndV2,
            colorBorderV2,
            textColorV2,
            levelColorV2);
    }

    private static bool TryDanmakuUser(JsonElement info, out JsonElement user)
    {
        if (TryArrayItem(info, 0, out var danmakuInfo)
            && TryArrayItem(danmakuInfo, 15, out var supplementaryInfo)
            && TryObject(supplementaryInfo, "user", out user))
        {
            return true;
        }

        user = default;
        return false;
    }

    private static GiftEvent? ParseGift(
        JsonElement root,
        string command,
        DateTimeOffset receivedAt)
    {
        if (!TryObject(root, "data", out var data))
        {
            return null;
        }

        var eventId = String(data, "tid");
        if (string.IsNullOrEmpty(eventId))
        {
            eventId = String(data, "rnd");
        }

        return new GiftEvent(
            Int64(data, "uid"),
            String(data, "uname"),
            Int64(data, "giftId"),
            String(data, "giftName"),
            Int32(data, "num"),
            Int64(data, "price"),
            Int64(data, "total_coin"),
            String(data, "coin_type"),
            eventId,
            String(data, "batch_combo_id"),
            UnixTime(data, "timestamp", receivedAt),
            command);
    }

    private static GiftComboEvent? ParseGiftCombo(
        JsonElement root,
        string command,
        DateTimeOffset receivedAt)
    {
        if (!TryObject(root, "data", out var data))
        {
            return null;
        }

        var totalCount = Int32(data, "total_num");
        if (totalCount == 0)
        {
            totalCount = Int32(data, "combo_num");
        }
        if (totalCount == 0)
        {
            totalCount = Int32(data, "batch_combo_num");
        }

        return new GiftComboEvent(
            Int64(data, "uid"),
            String(data, "uname"),
            Int64(data, "gift_id"),
            String(data, "gift_name"),
            totalCount,
            Int64(data, "combo_total_coin"),
            String(data, "combo_id"),
            String(data, "batch_combo_id"),
            receivedAt,
            command);
    }

    private static SuperChatEvent? ParseSuperChat(
        JsonElement root,
        string command,
        DateTimeOffset receivedAt)
    {
        if (!TryObject(root, "data", out var data))
        {
            return null;
        }

        var startsAt = UnixTime(data, "start_time", UnixTime(data, "ts", receivedAt));
        var endSeconds = Int64(data, "end_time");
        DateTimeOffset? endsAt = TryUnixTime(endSeconds, out var parsedEnd) ? parsedEnd : null;
        var userName = TryObject(data, "user_info", out var userInfo)
            ? String(userInfo, "uname")
            : string.Empty;

        return new SuperChatEvent(
            String(data, "id"),
            Int64(data, "uid"),
            userName,
            String(data, "message"),
            String(data, "message_trans"),
            String(data, "message_jpn"),
            Int32(data, "price"),
            startsAt,
            endsAt,
            Int32(data, "time"),
            command);
    }

    private static SuperChatDeleteEvent? ParseSuperChatDelete(
        JsonElement root,
        string command,
        DateTimeOffset receivedAt)
    {
        if (!TryObject(root, "data", out var data)
            || !TryProperty(data, "ids", out var idsElement)
            || idsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var ids = idsElement
            .EnumerateArray()
            .Select(ScalarString)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToArray();
        return new SuperChatDeleteEvent(ids, receivedAt, command);
    }

    private static GuardPurchaseEvent? ParseGuardPurchase(
        JsonElement root,
        string command,
        DateTimeOffset receivedAt)
    {
        if (!TryObject(root, "data", out var data))
        {
            return null;
        }

        return new GuardPurchaseEvent(
            Int64(data, "uid"),
            String(data, "username"),
            Int32(data, "guard_level"),
            Int32(data, "num"),
            Int64(data, "price"),
            Int64(data, "gift_id"),
            String(data, "gift_name"),
            UnixTime(data, "start_time", receivedAt),
            command);
    }

    private static bool TryProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryObject(JsonElement element, string name, out JsonElement value)
    {
        return TryProperty(element, name, out value)
            && value.ValueKind == JsonValueKind.Object;
    }

    private static bool TryArrayItem(JsonElement element, int index, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Array && element.GetArrayLength() > index)
        {
            value = element[index];
            return true;
        }

        value = default;
        return false;
    }

    private static string ArrayString(JsonElement element, int index)
    {
        return TryArrayItem(element, index, out var value)
            ? ScalarString(value)
            : string.Empty;
    }

    private static long ArrayInt64(JsonElement element, int index)
    {
        return TryArrayItem(element, index, out var value) ? ScalarInt64(value) : 0;
    }

    private static int ArrayInt32(JsonElement element, int index)
    {
        var value = ArrayInt64(element, index);
        return value is >= int.MinValue and <= int.MaxValue ? (int)value : 0;
    }

    private static string String(JsonElement element, string name)
    {
        return TryProperty(element, name, out var value) ? ScalarString(value) : string.Empty;
    }

    private static string ScalarString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number => value.GetRawText(),
        _ => string.Empty
    };

    private static string Prefer(string value, string fallback) =>
        string.IsNullOrEmpty(value) ? fallback : value;

    private static long ScalarInt64(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
                ? number
                : 0;
    }

    private static long Int64(JsonElement element, string name)
    {
        return TryProperty(element, name, out var value) ? ScalarInt64(value) : 0;
    }

    private static int Int32(JsonElement element, string name)
    {
        var value = Int64(element, name);
        return value is >= int.MinValue and <= int.MaxValue ? (int)value : 0;
    }

    private static DateTimeOffset UnixTime(
        JsonElement element,
        string name,
        DateTimeOffset fallback)
    {
        return TryUnixTime(Int64(element, name), out var result) ? result : fallback;
    }

    private static DateTimeOffset UnixMilliseconds(
        JsonElement element,
        string name,
        DateTimeOffset fallback)
    {
        return TryUnixMilliseconds(Int64(element, name), out var result) ? result : fallback;
    }

    private static bool TryUnixMilliseconds(long milliseconds, out DateTimeOffset result)
    {
        if (milliseconds > 0
            && milliseconds >= DateTimeOffset.MinValue.ToUnixTimeMilliseconds()
            && milliseconds <= DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())
        {
            result = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).ToLocalTime();
            return true;
        }

        result = default;
        return false;
    }

    private static bool TryUnixTime(long seconds, out DateTimeOffset result)
    {
        if (seconds >= DateTimeOffset.MinValue.ToUnixTimeSeconds()
            && seconds <= DateTimeOffset.MaxValue.ToUnixTimeSeconds())
        {
            result = DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime();
            return seconds > 0;
        }

        result = default;
        return false;
    }
}
