using System.Text.Json.Nodes;

namespace Fourvale.Adapter.Network;

/// <summary>
/// Turns one binary Colyseus WebSocket frame into a redacted JSON record.
/// Frame layout taken from the colyseus.js client bundled by Fourvale (observed 2026-10-01):
/// first byte is the protocol code; ROOM_DATA carries a message type followed by a MessagePack payload.
/// Never throws: undecodable frames are recorded by size only.
/// </summary>
public static class ColyseusFrameDecoder
{
    public const byte Handshake = 9;
    public const byte JoinRoom = 10;
    public const byte Error = 11;
    public const byte LeaveRoom = 12;
    public const byte RoomData = 13;
    public const byte RoomState = 14;
    public const byte RoomStatePatch = 15;
    public const byte RoomDataSchema = 16;
    public const byte RoomDataBytes = 17;

    public static string ProtocolName(byte code) => code switch
    {
        Handshake => "HANDSHAKE",
        JoinRoom => "JOIN_ROOM",
        Error => "ERROR",
        LeaveRoom => "LEAVE_ROOM",
        RoomData => "ROOM_DATA",
        RoomState => "ROOM_STATE",
        RoomStatePatch => "ROOM_STATE_PATCH",
        RoomDataSchema => "ROOM_DATA_SCHEMA",
        RoomDataBytes => "ROOM_DATA_BYTES",
        _ => $"UNKNOWN_{code}",
    };

    public sealed record Result(JsonObject Record, string Label, int Redactions, bool DecodeFailed);

    public static Result Decode(byte[] frame, Redactor redactor)
    {
        if (frame.Length == 0)
        {
            return new Result(new JsonObject { ["protocol"] = "EMPTY", ["length"] = 0 }, "EMPTY", 0, false);
        }

        var code = frame[0];
        var name = ProtocolName(code);
        var record = new JsonObject { ["protocol"] = name, ["length"] = frame.Length };
        var redactions = 0;

        try
        {
            switch (code)
            {
                case JoinRoom:
                    DecodeJoinRoom(frame, record, ref redactions);
                    return new Result(record, name, redactions, false);

                case RoomData:
                {
                    var offset = 1;
                    var type = MsgPackReader.Read(frame, ref offset);
                    var typeText = TypeText(type);
                    record["type"] = type?.DeepClone();
                    if (offset < frame.Length)
                    {
                        var message = MsgPackReader.Read(frame, ref offset);
                        record["message"] = redactor.Redact(message, redactor.IsChatType(typeText), ref redactions);
                    }

                    return new Result(record, typeText ?? name, redactions, false);
                }

                case RoomDataBytes:
                case RoomDataSchema:
                {
                    var offset = 1;
                    var type = MsgPackReader.Read(frame, ref offset);
                    var typeText = TypeText(type);
                    record["type"] = type?.DeepClone();
                    if (redactor.IsChatType(typeText))
                    {
                        // Raw bytes of a chat-like message cannot be field-redacted, so drop them.
                        record["bytes"] = Redactor.Placeholder;
                        redactions++;
                    }
                    else
                    {
                        record["bytes"] = Convert.ToBase64String(frame, offset, frame.Length - offset);
                    }

                    return new Result(record, $"{typeText ?? "?"} ({name})", redactions, false);
                }

                case Error:
                {
                    var offset = 1;
                    record["code"] = MsgPackReader.Read(frame, ref offset);
                    if (offset < frame.Length)
                    {
                        record["message"] = redactor.Redact(MsgPackReader.Read(frame, ref offset), false, ref redactions);
                    }

                    return new Result(record, name, redactions, false);
                }

                case LeaveRoom:
                    return new Result(record, name, 0, false);

                default:
                    // ROOM_STATE / ROOM_STATE_PATCH / HANDSHAKE / unknown: schema-encoded binary, kept raw for discovery.
                    record["bytes"] = Convert.ToBase64String(frame, 1, frame.Length - 1);
                    return new Result(record, name, 0, false);
            }
        }
        catch (Exception ex)
        {
            // Payload is not stored: a frame we cannot parse cannot be redacted.
            var failed = new JsonObject
            {
                ["protocol"] = name,
                ["length"] = frame.Length,
                ["decodeError"] = ex.GetType().Name,
            };
            return new Result(failed, $"{name} (undecoded)", 0, true);
        }
    }

    /// <summary>
    /// Server JOIN_ROOM: [10, len, reconnectionToken…, len, serializerId…, handshake…].
    /// The token bytes are skipped without being decoded. Client JOIN_ROOM is a 1-byte acknowledgement.
    /// </summary>
    private static void DecodeJoinRoom(byte[] frame, JsonObject record, ref int redactions)
    {
        if (frame.Length == 1)
        {
            return;
        }

        var offset = 1;
        var tokenLength = frame[offset++];
        if (offset + tokenLength > frame.Length)
        {
            throw new FormatException("JOIN_ROOM token length exceeds frame.");
        }

        offset += tokenLength;
        record["reconnectionToken"] = Redactor.Placeholder;
        redactions++;

        if (offset >= frame.Length)
        {
            return;
        }

        var serializerLength = frame[offset++];
        if (offset + serializerLength > frame.Length)
        {
            throw new FormatException("JOIN_ROOM serializer length exceeds frame.");
        }

        record["serializerId"] = System.Text.Encoding.UTF8.GetString(frame, offset, serializerLength);
        offset += serializerLength;

        if (offset < frame.Length)
        {
            // Schema handshake: type and field definitions, no player data.
            record["handshake"] = Convert.ToBase64String(frame, offset, frame.Length - offset);
        }
    }

    private static string? TypeText(JsonNode? type) => type switch
    {
        null => null,
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        _ => type.ToJsonString(),
    };
}
