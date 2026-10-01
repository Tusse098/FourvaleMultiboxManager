using System.Buffers.Binary;
using System.Text;

namespace Fourvale.Adapter.Colyseus;

/// <summary>
/// Primitive decoding for @colyseus/schema v2, mirroring the client bundled by Fourvale
/// (observed 2026-10-01). Fixed-width values are little-endian; <c>number</c> and <c>string</c>
/// use MessagePack-style prefixes with little-endian payloads.
/// All reads are bounds-checked and throw <see cref="FormatException"/> on bad input.
/// </summary>
public ref struct SchemaReader(ReadOnlySpan<byte> data, int offset = 0)
{
    private readonly ReadOnlySpan<byte> _data = data;

    public int Offset { get; set; } = offset;
    public readonly int Length => _data.Length;
    public readonly bool HasMore => Offset < _data.Length;

    public readonly byte Peek() => Offset < _data.Length ? _data[Offset] : throw Truncated();

    public readonly byte PeekAt(int position) =>
        position >= 0 && position < _data.Length ? _data[position] : throw Truncated();

    public byte ReadByte() => Take(1)[0];

    /// <summary>Reads an index at <paramref name="position"/> without moving the reader.</summary>
    public readonly bool TryPeekIndexAt(int position, out int value)
    {
        var probe = new SchemaReader(_data, position);
        try
        {
            value = probe.ReadIndex();
            return true;
        }
        catch (FormatException)
        {
            value = 0;
            return false;
        }
    }

    /// <summary>Reads a schema <c>number</c>: integers as <see cref="long"/>, floats as <see cref="double"/>.</summary>
    public object ReadNumber()
    {
        var prefix = ReadByte();
        if (prefix < 0x80) return (long)prefix;
        if (prefix > 0xdf) return (long)(sbyte)prefix;

        return prefix switch
        {
            0xca => (double)BinaryPrimitives.ReadSingleLittleEndian(Take(4)),
            0xcb => BinaryPrimitives.ReadDoubleLittleEndian(Take(8)),
            0xcc => (long)Take(1)[0],
            0xcd => (long)BinaryPrimitives.ReadUInt16LittleEndian(Take(2)),
            0xce => (long)BinaryPrimitives.ReadUInt32LittleEndian(Take(4)),
            0xcf => ToNumber(BinaryPrimitives.ReadUInt64LittleEndian(Take(8))),
            0xd0 => (long)(sbyte)Take(1)[0],
            0xd1 => (long)BinaryPrimitives.ReadInt16LittleEndian(Take(2)),
            0xd2 => (long)BinaryPrimitives.ReadInt32LittleEndian(Take(4)),
            0xd3 => BinaryPrimitives.ReadInt64LittleEndian(Take(8)),
            _ => throw new FormatException($"Invalid schema number prefix 0x{prefix:x2} at offset {Offset - 1}."),
        };
    }

    /// <summary>Reads a number that must be a non-negative integer (refIds, indexes, type ids).</summary>
    public int ReadIndex()
    {
        var value = ReadNumber();
        return value is long l && l is >= 0 and <= int.MaxValue
            ? (int)l
            : throw new FormatException($"Expected an index, got {value} at offset {Offset}.");
    }

    public string ReadString()
    {
        var prefix = ReadByte();
        int length;
        if (prefix < 0xc0)
        {
            length = prefix & 0x1f;
        }
        else
        {
            length = prefix switch
            {
                0xd9 => Take(1)[0],
                0xda => BinaryPrimitives.ReadUInt16LittleEndian(Take(2)),
                0xdb => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(Take(4))),
                _ => throw new FormatException($"Invalid schema string prefix 0x{prefix:x2} at offset {Offset - 1}."),
            };
        }

        return Encoding.UTF8.GetString(Take(length));
    }

    /// <summary>Reads a primitive field by its schema type name.</summary>
    public object ReadPrimitive(string type) => type switch
    {
        "string" => ReadString(),
        "number" => ReadNumber(),
        "boolean" => ReadByte() > 0,
        "int8" => (long)(sbyte)ReadByte(),
        "uint8" => (long)ReadByte(),
        "int16" => (long)BinaryPrimitives.ReadInt16LittleEndian(Take(2)),
        "uint16" => (long)BinaryPrimitives.ReadUInt16LittleEndian(Take(2)),
        "int32" => (long)BinaryPrimitives.ReadInt32LittleEndian(Take(4)),
        "uint32" => (long)BinaryPrimitives.ReadUInt32LittleEndian(Take(4)),
        "int64" => ReadInt64(),
        "uint64" => ReadUInt64(),
        "float32" => (double)BinaryPrimitives.ReadSingleLittleEndian(Take(4)),
        "float64" => BinaryPrimitives.ReadDoubleLittleEndian(Take(8)),
        _ => throw new FormatException($"Unknown primitive type '{type}'."),
    };

    // The JS client builds 64-bit values as low uint32 + high int32/uint32 * 2^32.
    private long ReadInt64()
    {
        var low = BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        var high = BinaryPrimitives.ReadInt32LittleEndian(Take(4));
        return ((long)high << 32) | low;
    }

    private object ReadUInt64() => ToNumber(BinaryPrimitives.ReadUInt64LittleEndian(Take(8)));

    private static object ToNumber(ulong value) => value <= long.MaxValue ? (long)value : (double)value;

    private ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || Offset + count > _data.Length)
        {
            throw Truncated();
        }

        var slice = _data.Slice(Offset, count);
        Offset += count;
        return slice;
    }

    private readonly FormatException Truncated() => new($"Schema data truncated at offset {Offset}.");
}
