using System.Text;
using Fourvale.Adapter.Colyseus;

namespace Fourvale.Adapter.Tests.Colyseus;

/// <summary>Builds schema v2 bytes by hand for tests (little-endian, MessagePack-style prefixes).</summary>
internal sealed class Bytes
{
    private readonly List<byte> _bytes = [];

    public byte[] ToArray() => [.. _bytes];

    public Bytes Raw(params byte[] values)
    {
        _bytes.AddRange(values);
        return this;
    }

    public Bytes Switch(int refId) => Raw(255).Number(refId);

    public Bytes Number(int value)
    {
        if (value is >= 0 and < 128) return Raw((byte)value);
        if (value is < 0 and >= -32) return Raw((byte)(sbyte)value);
        return Raw(0xd2).Raw(BitConverter.GetBytes(value)); // int32, little-endian on x86/x64
    }

    public Bytes Str(string value)
    {
        var utf8 = Encoding.UTF8.GetBytes(value);
        return (utf8.Length < 32 ? Raw((byte)(0xa0 | utf8.Length)) : Raw(0xd9, (byte)utf8.Length)).Raw(utf8);
    }

    public Bytes Int32(int value) => Raw(BitConverter.GetBytes(value));
}

public class StateDecoderTests
{
    // Player { name: string, hp: int32, classId: string }   State { players: map<Player>, phase: string }
    private static readonly SchemaContext Context = new(
        new Dictionary<int, SchemaType>
        {
            [0] = new(0,
            [
                new("name", FieldKind.Primitive, "string", null),
                new("hp", FieldKind.Primitive, "int32", null),
                new("classId", FieldKind.Primitive, "string", null),
            ]),
            [1] = new(1,
            [
                new("players", FieldKind.Map, null, 0),
                new("phase", FieldKind.Primitive, "string", null),
            ]),
            [2] = new(2,
            [
                new("name", FieldKind.Primitive, "string", null),
                new("hp", FieldKind.Primitive, "int32", null),
                new("classId", FieldKind.Primitive, "string", null),
                new("boss", FieldKind.Primitive, "boolean", null),
            ]),
        },
        rootTypeId: 1);

    private const byte Add = 128;
    private const byte Delete = 64;

    private static byte[] FullState() => new Bytes()
        .Raw(Add | 0).Number(1)                 // players -> refId 1
        .Raw(Add | 1).Str("idle")               // phase
        .Switch(1)
        .Raw(Add).Number(0).Str("s1").Number(2) // players["s1"] -> refId 2
        .Switch(2)
        .Raw(Add | 0).Str("PlayerA")
        .Raw(Add | 1).Int32(100)
        .Raw(Add | 2).Str("scout")
        .ToArray();

    [Fact]
    public void Full_state_builds_the_tree()
    {
        var decoder = new StateDecoder(Context);

        decoder.Apply(FullState());

        Assert.Equal("""{"players":{"s1":{"name":"PlayerA","hp":100,"classId":"scout"}},"phase":"idle"}""", decoder.ToJson().ToJsonString());
    }

    [Fact]
    public void Patch_reports_changes_with_paths_and_previous_values()
    {
        var decoder = new StateDecoder(Context);
        decoder.Apply(FullState());

        var changes = decoder.Apply(new Bytes().Switch(2).Raw(1).Int32(90).Raw(2).Str("arctic_soldier").ToArray());

        Assert.Collection(changes,
            c => Assert.Equal(("players.s1.hp", (object?)90L, (object?)100L), (c.Path, c.Value, c.Previous)),
            c => Assert.Equal(("players.s1.classId", (object?)"arctic_soldier", (object?)"scout"), (c.Path, c.Value, c.Previous)));
    }

    [Fact]
    public void Map_delete_and_clear_remove_items()
    {
        var decoder = new StateDecoder(Context);
        decoder.Apply(FullState());
        decoder.Apply(new Bytes().Switch(1).Raw(Add).Number(1).Str("s2").Number(3).Switch(3).Raw(Add | 0).Str("PlayerB").ToArray());

        var deleted = decoder.Apply(new Bytes().Switch(1).Raw(Delete).Number(0).ToArray());
        Assert.Equal("players.s1", Assert.Single(deleted).Path);
        Assert.Equal("""{"s2":{"name":"PlayerB","hp":null,"classId":null}}""", decoder.ToJson()["players"]!.ToJsonString());

        decoder.Apply(new Bytes().Switch(1).Raw(10).ToArray());
        Assert.Equal("{}", decoder.ToJson()["players"]!.ToJsonString());
    }

    [Fact]
    public void Type_id_selects_a_subtype()
    {
        var decoder = new StateDecoder(Context);
        decoder.Apply(FullState());

        decoder.Apply(new Bytes().Switch(1).Raw(Add).Number(1).Str("boss1").Number(5).Raw(213).Number(2)
            .Switch(5).Raw(Add | 3).Raw(1).ToArray());

        Assert.Equal(true, (bool?)decoder.ToJson()["players"]!["boss1"]!["boss"]);
    }

    [Fact]
    public void Unknown_field_index_is_skipped_to_the_next_known_structure()
    {
        var decoder = new StateDecoder(Context);
        decoder.Apply(FullState());

        // Field index 9 does not exist on Player; recovery resumes at the switch to refId 2.
        var changes = decoder.Apply(new Bytes().Switch(2).Raw(9).Raw(0x11, 0x22).Switch(2).Raw(1).Int32(55).ToArray());

        Assert.Equal(1, decoder.DefinitionMismatches);
        Assert.Equal("players.s1.hp", Assert.Single(changes).Path);
    }

    [Fact]
    public void Unknown_ref_id_throws_format_exception()
    {
        var decoder = new StateDecoder(Context);

        Assert.Throws<FormatException>(() => decoder.Apply(new Bytes().Switch(42).Raw(0).ToArray()));
    }

    [Fact]
    public void Truncated_patch_throws_format_exception()
    {
        var decoder = new StateDecoder(Context);
        decoder.Apply(FullState());

        Assert.Throws<FormatException>(() => decoder.Apply(new Bytes().Switch(2).Raw(1).Raw(0x5a, 0x00).ToArray()));
    }

    [Fact]
    public void Random_input_only_ever_throws_format_exception()
    {
        var random = new Random(4321);
        for (var i = 0; i < 5000; i++)
        {
            var decoder = new StateDecoder(Context);
            decoder.Apply(FullState());
            var bytes = new byte[random.Next(0, 48)];
            random.NextBytes(bytes);

            try
            {
                decoder.Apply(bytes);
                _ = decoder.ToJson().ToJsonString();
            }
            catch (FormatException)
            {
                // Expected for malformed input; anything else fails the test.
            }
        }
    }
}

public class SchemaReaderTests
{
    [Theory]
    [InlineData(new byte[] { 0x05 }, 5L)]
    [InlineData(new byte[] { 0xff }, -1L)]
    [InlineData(new byte[] { 0xcd, 0x34, 0x12 }, 0x1234L)]           // uint16, little-endian
    [InlineData(new byte[] { 0xd1, 0xfe, 0xff }, -2L)]               // int16
    [InlineData(new byte[] { 0xce, 0x78, 0x56, 0x34, 0x12 }, 0x12345678L)]
    public void Numbers_are_little_endian(byte[] bytes, long expected)
    {
        var reader = new SchemaReader(bytes);

        Assert.Equal(expected, reader.ReadNumber());
    }

    [Fact]
    public void Float64_and_typed_primitives()
    {
        var bytes = new Bytes().Raw(0xcb).Raw(BitConverter.GetBytes(0.5)).Raw(0xfe, 0xff).Raw(0x01).ToArray();
        var reader = new SchemaReader(bytes);

        Assert.Equal(0.5, reader.ReadNumber());
        Assert.Equal(-2L, reader.ReadPrimitive("int16"));
        Assert.Equal(true, reader.ReadPrimitive("boolean"));
        Assert.False(reader.HasMore);
    }

    [Fact]
    public void Long_strings_use_str8()
    {
        var text = new string('a', 40);
        var reader = new SchemaReader(new Bytes().Str(text).ToArray());

        Assert.Equal(text, reader.ReadString());
    }
}
