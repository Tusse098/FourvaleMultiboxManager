using System.Text;
using System.Text.Json.Nodes;
using Fourvale.Adapter.Network;

namespace Fourvale.Adapter.Tests.Network;

public class MsgPackReaderTests
{
    [Fact]
    public void Reads_nested_values()
    {
        var bytes = new Pack()
            .Map(3)
            .Str("hp").Int(-5)
            .Str("list").Array(2).Int(1).Str("two")
            .Str("big").Int(100000)
            .ToArray();
        var offset = 0;

        var node = MsgPackReader.Read(bytes, ref offset);

        Assert.Equal("""{"hp":-5,"list":[1,"two"],"big":100000}""", node!.ToJsonString(CaptureJson.Options));
        Assert.Equal(bytes.Length, offset);
    }

    [Fact]
    public void Reads_double_and_binary()
    {
        var bytes = new Pack().Array(2)
            .Raw(0xcb, 0x40, 0x09, 0x21, 0xfb, 0x54, 0x44, 0x2d, 0x18) // 3.141592653589793
            .Raw(0xc4, 0x02, 0xAB, 0xCD)
            .ToArray();
        var offset = 0;

        var node = MsgPackReader.Read(bytes, ref offset)!.AsArray();

        Assert.Equal(Math.PI, node[0]!.GetValue<double>());
        Assert.Equal(Convert.ToBase64String([0xAB, 0xCD]), node[1]!["$bin"]!.GetValue<string>());
    }

    [Fact]
    public void Msgpackr_undefined_becomes_null()
    {
        var bytes = new Pack().Map(1).Str("proj").Raw(0xd4, 0x00, 0x00).ToArray();
        var offset = 0;

        var node = MsgPackReader.Read(bytes, ref offset);

        Assert.Equal("""{"proj":null}""", node!.ToJsonString(CaptureJson.Options));
    }

    [Fact]
    public void Truncated_input_throws_format_exception()
    {
        var bytes = new Pack().Raw(0xd9, 0x50).Raw(Encoding.UTF8.GetBytes("short")).ToArray();
        var offset = 0;

        Assert.Throws<FormatException>(() => MsgPackReader.Read(bytes, ref offset));
    }
}

public class ColyseusFrameDecoderTests
{
    private readonly Redactor _redactor = TestSupport.NewRedactor();

    [Fact]
    public void Join_room_never_contains_the_reconnection_token()
    {
        const string token = "SECRET-RECONNECT-TOKEN-123";
        var tokenBytes = Encoding.UTF8.GetBytes(token);
        var frame = new Pack(ColyseusFrameDecoder.JoinRoom)
            .Raw((byte)tokenBytes.Length).Raw(tokenBytes)
            .Raw(6).Raw(Encoding.UTF8.GetBytes("schema"))
            .Raw(0x01, 0x02, 0x03)
            .ToArray();

        var result = ColyseusFrameDecoder.Decode(frame, _redactor);
        var json = result.Record.ToJsonString(CaptureJson.Options);

        Assert.DoesNotContain(token, json);
        Assert.DoesNotContain(Convert.ToBase64String(tokenBytes), json);
        Assert.Equal("schema", result.Record["serializerId"]!.GetValue<string>());
        Assert.Equal(Convert.ToBase64String([1, 2, 3]), result.Record["handshake"]!.GetValue<string>());
        Assert.Equal(1, result.Redactions);
    }

    [Fact]
    public void Client_join_acknowledgement_is_one_byte()
    {
        var result = ColyseusFrameDecoder.Decode([ColyseusFrameDecoder.JoinRoom], _redactor);

        Assert.False(result.DecodeFailed);
        Assert.Equal("JOIN_ROOM", result.Label);
    }

    [Fact]
    public void Room_data_chat_is_redacted()
    {
        var frame = new Pack(ColyseusFrameDecoder.RoomData)
            .Str("chatMsg")
            .Map(3).Str("scope").Str("global").Str("text").Str("hello there").Str("from").Str("PlayerB")
            .ToArray();

        var result = ColyseusFrameDecoder.Decode(frame, _redactor);
        var json = result.Record.ToJsonString(CaptureJson.Options);

        Assert.Equal("chatMsg", result.Label);
        Assert.DoesNotContain("hello there", json);
        Assert.DoesNotContain("PlayerB", json);
        Assert.Contains("global", json);
    }

    [Fact]
    public void Room_data_game_message_is_kept()
    {
        var frame = new Pack(ColyseusFrameDecoder.RoomData)
            .Int(5)
            .Map(2).Str("hp").Int(42).Str("token").Str("leak")
            .ToArray();

        var result = ColyseusFrameDecoder.Decode(frame, _redactor);
        var message = result.Record["message"]!;

        Assert.Equal("5", result.Label);
        Assert.Equal(42, message["hp"]!.GetValue<int>());
        Assert.Equal(Redactor.Placeholder, message["token"]!.GetValue<string>());
    }

    [Fact]
    public void Room_data_without_payload_is_fine()
    {
        var frame = new Pack(ColyseusFrameDecoder.RoomData).Str("chatTyping").ToArray();

        var result = ColyseusFrameDecoder.Decode(frame, _redactor);

        Assert.False(result.DecodeFailed);
        Assert.Null(result.Record["message"]);
    }

    [Fact]
    public void State_patch_is_kept_as_base64()
    {
        var result = ColyseusFrameDecoder.Decode([ColyseusFrameDecoder.RoomStatePatch, 0xFF, 0x01], _redactor);

        Assert.Equal("ROOM_STATE_PATCH", result.Label);
        Assert.Equal(Convert.ToBase64String([0xFF, 0x01]), result.Record["bytes"]!.GetValue<string>());
    }

    [Fact]
    public void Raw_bytes_of_chat_type_are_dropped()
    {
        var frame = new Pack(ColyseusFrameDecoder.RoomDataBytes).Str("chat").Raw(Encoding.UTF8.GetBytes("hi")).ToArray();

        var result = ColyseusFrameDecoder.Decode(frame, _redactor);

        Assert.Equal(Redactor.Placeholder, result.Record["bytes"]!.GetValue<string>());
    }

    [Fact]
    public void Malformed_room_data_is_recorded_by_size_only()
    {
        var frame = new Pack(ColyseusFrameDecoder.RoomData)
            .Str("chatMsg")
            .Raw(0xd9, 0x40).Raw(Encoding.UTF8.GetBytes("secret chat text, truncated"))
            .ToArray();

        var result = ColyseusFrameDecoder.Decode(frame, _redactor);
        var json = result.Record.ToJsonString(CaptureJson.Options);

        Assert.True(result.DecodeFailed);
        Assert.DoesNotContain("secret chat", json);
        Assert.Equal(frame.Length, result.Record["length"]!.GetValue<int>());
    }

    [Fact]
    public void Random_frames_never_throw()
    {
        var random = new Random(1234);
        for (var i = 0; i < 5000; i++)
        {
            var frame = new byte[random.Next(0, 64)];
            random.NextBytes(frame);
            if (frame.Length > 0)
            {
                frame[0] = (byte)random.Next(8, 19);
            }

            var result = ColyseusFrameDecoder.Decode(frame, _redactor);

            Assert.NotNull(result.Record);
            _ = result.Record.ToJsonString(CaptureJson.Options); // Must also serialise without throwing.
        }
    }
}
