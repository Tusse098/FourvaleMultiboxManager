using System.Text;
using Fourvale.Adapter.Network;

namespace Fourvale.Adapter.Tests.Network;

internal static class TestSupport
{
    /// <summary>The shipped fourvale-adapter.json, so tests cover the real redaction config.</summary>
    public static AdapterRules Rules { get; } = AdapterRules.LoadDefault();

    public static Redactor NewRedactor() => new(Rules);
}

/// <summary>Tiny MessagePack writer for building test frames.</summary>
internal sealed class Pack
{
    private readonly List<byte> _bytes = [];

    public Pack(params byte[] prefix) => _bytes.AddRange(prefix);

    public byte[] ToArray() => [.. _bytes];

    public Pack Str(string value)
    {
        var utf8 = Encoding.UTF8.GetBytes(value);
        if (utf8.Length < 32)
        {
            _bytes.Add((byte)(0xa0 | utf8.Length));
        }
        else
        {
            _bytes.Add(0xd9);
            _bytes.Add((byte)utf8.Length);
        }

        _bytes.AddRange(utf8);
        return this;
    }

    public Pack Int(int value)
    {
        if (value is >= 0 and <= 0x7f)
        {
            _bytes.Add((byte)value);
        }
        else if (value is < 0 and >= -32)
        {
            _bytes.Add((byte)(sbyte)value);
        }
        else
        {
            _bytes.Add(0xd2);
            _bytes.AddRange(BitConverter.GetBytes(value).Reverse());
        }

        return this;
    }

    public Pack Map(int count)
    {
        _bytes.Add((byte)(0x80 | count));
        return this;
    }

    public Pack Array(int count)
    {
        _bytes.Add((byte)(0x90 | count));
        return this;
    }

    public Pack Raw(params byte[] bytes)
    {
        _bytes.AddRange(bytes);
        return this;
    }
}
