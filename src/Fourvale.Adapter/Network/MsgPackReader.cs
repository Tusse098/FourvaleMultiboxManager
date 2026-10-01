using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;

namespace Fourvale.Adapter.Network;

/// <summary>
/// Minimal MessagePack → JSON reader for inspecting captured frames.
/// Binary and extension values become {"$bin": base64} / {"$ext": type, "data": base64}.
/// Throws <see cref="FormatException"/> on truncated or invalid input; callers must catch.
/// </summary>
public static class MsgPackReader
{
    private const int MaxDepth = 64;

    public static JsonNode? Read(ReadOnlySpan<byte> data, ref int offset) => Read(data, ref offset, 0);

    private static JsonNode? Read(ReadOnlySpan<byte> data, ref int offset, int depth)
    {
        if (depth > MaxDepth)
        {
            throw new FormatException("MessagePack nesting too deep.");
        }

        var b = Take(data, ref offset, 1)[0];

        if (b <= 0x7f) return JsonValue.Create((int)b);
        if (b >= 0xe0) return JsonValue.Create((int)(sbyte)b);
        if ((b & 0xf0) == 0x80) return ReadMap(data, ref offset, b & 0x0f, depth);
        if ((b & 0xf0) == 0x90) return ReadArray(data, ref offset, b & 0x0f, depth);
        if ((b & 0xe0) == 0xa0) return ReadString(data, ref offset, b & 0x1f);

        switch (b)
        {
            case 0xc0: return null;
            case 0xc2: return JsonValue.Create(false);
            case 0xc3: return JsonValue.Create(true);
            case 0xc4: return Bin(Take(data, ref offset, Take(data, ref offset, 1)[0]));
            case 0xc5: return Bin(Take(data, ref offset, BinaryPrimitives.ReadUInt16BigEndian(Take(data, ref offset, 2))));
            case 0xc6: return Bin(Take(data, ref offset, Length(BinaryPrimitives.ReadUInt32BigEndian(Take(data, ref offset, 4)))));
            case 0xc7: return Ext(data, ref offset, Take(data, ref offset, 1)[0]);
            case 0xc8: return Ext(data, ref offset, BinaryPrimitives.ReadUInt16BigEndian(Take(data, ref offset, 2)));
            case 0xc9: return Ext(data, ref offset, Length(BinaryPrimitives.ReadUInt32BigEndian(Take(data, ref offset, 4))));
            case 0xca: return Number(BinaryPrimitives.ReadSingleBigEndian(Take(data, ref offset, 4)));
            case 0xcb: return Number(BinaryPrimitives.ReadDoubleBigEndian(Take(data, ref offset, 8)));
            case 0xcc: return JsonValue.Create(Take(data, ref offset, 1)[0]);
            case 0xcd: return JsonValue.Create(BinaryPrimitives.ReadUInt16BigEndian(Take(data, ref offset, 2)));
            case 0xce: return JsonValue.Create(BinaryPrimitives.ReadUInt32BigEndian(Take(data, ref offset, 4)));
            case 0xcf: return JsonValue.Create(BinaryPrimitives.ReadUInt64BigEndian(Take(data, ref offset, 8)));
            case 0xd0: return JsonValue.Create((sbyte)Take(data, ref offset, 1)[0]);
            case 0xd1: return JsonValue.Create(BinaryPrimitives.ReadInt16BigEndian(Take(data, ref offset, 2)));
            case 0xd2: return JsonValue.Create(BinaryPrimitives.ReadInt32BigEndian(Take(data, ref offset, 4)));
            case 0xd3: return JsonValue.Create(BinaryPrimitives.ReadInt64BigEndian(Take(data, ref offset, 8)));
            case 0xd4: return Ext(data, ref offset, 1);
            case 0xd5: return Ext(data, ref offset, 2);
            case 0xd6: return Ext(data, ref offset, 4);
            case 0xd7: return Ext(data, ref offset, 8);
            case 0xd8: return Ext(data, ref offset, 16);
            case 0xd9: return ReadString(data, ref offset, Take(data, ref offset, 1)[0]);
            case 0xda: return ReadString(data, ref offset, BinaryPrimitives.ReadUInt16BigEndian(Take(data, ref offset, 2)));
            case 0xdb: return ReadString(data, ref offset, Length(BinaryPrimitives.ReadUInt32BigEndian(Take(data, ref offset, 4))));
            case 0xdc: return ReadArray(data, ref offset, BinaryPrimitives.ReadUInt16BigEndian(Take(data, ref offset, 2)), depth);
            case 0xdd: return ReadArray(data, ref offset, Length(BinaryPrimitives.ReadUInt32BigEndian(Take(data, ref offset, 4))), depth);
            case 0xde: return ReadMap(data, ref offset, BinaryPrimitives.ReadUInt16BigEndian(Take(data, ref offset, 2)), depth);
            case 0xdf: return ReadMap(data, ref offset, Length(BinaryPrimitives.ReadUInt32BigEndian(Take(data, ref offset, 4))), depth);
            default: throw new FormatException($"Unknown MessagePack prefix 0x{b:x2}.");
        }
    }

    private static ReadOnlySpan<byte> Take(ReadOnlySpan<byte> data, ref int offset, int count)
    {
        if (count < 0 || offset + count > data.Length)
        {
            throw new FormatException("MessagePack data is truncated.");
        }

        var slice = data.Slice(offset, count);
        offset += count;
        return slice;
    }

    private static int Length(uint value) =>
        value > int.MaxValue ? throw new FormatException("MessagePack length too large.") : (int)value;

    private static JsonNode ReadString(ReadOnlySpan<byte> data, ref int offset, int length) =>
        JsonValue.Create(Encoding.UTF8.GetString(Take(data, ref offset, length)));

    private static JsonArray ReadArray(ReadOnlySpan<byte> data, ref int offset, int count, int depth)
    {
        var array = new JsonArray();
        for (var i = 0; i < count; i++)
        {
            array.Add(Read(data, ref offset, depth + 1));
        }

        return array;
    }

    private static JsonObject ReadMap(ReadOnlySpan<byte> data, ref int offset, int count, int depth)
    {
        var obj = new JsonObject();
        for (var i = 0; i < count; i++)
        {
            var key = Read(data, ref offset, depth + 1);
            var keyText = key is JsonValue v && v.TryGetValue<string>(out var s) ? s : key?.ToJsonString() ?? "null";
            obj[keyText] = Read(data, ref offset, depth + 1);
        }

        return obj;
    }

    private static JsonObject Bin(ReadOnlySpan<byte> bytes) => new() { ["$bin"] = Convert.ToBase64String(bytes) };

    private static JsonObject? Ext(ReadOnlySpan<byte> data, ref int offset, int length)
    {
        var type = (sbyte)Take(data, ref offset, 1)[0];
        var payload = Take(data, ref offset, length);

        // msgpackr (used by the Colyseus client) encodes JavaScript `undefined` as fixext1 type 0, byte 0.
        if (type == 0 && payload.Length == 1 && payload[0] == 0)
        {
            return null;
        }

        return new JsonObject { ["$ext"] = type, ["data"] = Convert.ToBase64String(payload) };
    }

    // System.Text.Json cannot write NaN/Infinity, so those become strings.
    private static JsonNode Number(double value) =>
        double.IsFinite(value) ? JsonValue.Create(value) : JsonValue.Create(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
