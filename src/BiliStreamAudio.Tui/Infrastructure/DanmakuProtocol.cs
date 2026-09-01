using System.Buffers.Binary;
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

    public static IReadOnlyList<DanmakuEvent> Parse(ReadOnlySpan<byte> packet)
    {
        var events = new List<DanmakuEvent>();
        ParseFrames(packet, events);
        return events;
    }

    private static void ParseFrames(ReadOnlySpan<byte> bytes, List<DanmakuEvent> events)
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

    private static void ParseJson(ReadOnlySpan<byte> body, List<DanmakuEvent> events)
    {
        try
        {
            using var json = JsonDocument.Parse(body.ToArray());
            var command = json.RootElement.String("cmd").Split(':')[0];
            if (command != "DANMU_MSG")
            {
                return;
            }

            var info = json.RootElement.GetProperty("info");
            var message = info[1].GetString() ?? string.Empty;
            var user = info[2][1].GetString() ?? "匿名用户";
            events.Add(new DanmakuEvent(user, message, DateTimeOffset.Now));
        }
        catch (JsonException)
        {
            // Ignore unknown or malformed platform events.
        }
    }
}
