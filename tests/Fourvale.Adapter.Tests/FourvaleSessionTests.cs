using System.Text;
using System.Text.Json.Nodes;
using Fourvale.Adapter;
using Fourvale.Adapter.Network;

namespace Fourvale.Adapter.Tests;

public class FourvaleSessionTests
{
    private static readonly string Handshake = File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "fixtures", "colyseus", "handshake-2026-10-01.b64")).Trim();

    // Town root: mapId, players{ "<sessionId>": { classId, level } } in schema v2 bytes.
    private static string TownState(string sessionId, string classId, short level)
    {
        var bytes = new List<byte> { 128, 0xa0 | 11 };
        bytes.AddRange(Encoding.UTF8.GetBytes("bellroot_a4"));
        bytes.AddRange([129, 1, 255, 1, 128, 0, (byte)(0xa0 | sessionId.Length)]);
        bytes.AddRange(Encoding.UTF8.GetBytes(sessionId));
        bytes.AddRange([2, 255, 2, 130, (byte)(0xa0 | classId.Length)]);
        bytes.AddRange(Encoding.UTF8.GetBytes(classId));
        bytes.Add(131);
        bytes.AddRange(BitConverter.GetBytes(level));
        return Convert.ToBase64String(bytes.ToArray());
    }

    private static CaptureEvent Event(JsonObject record) => new(CaptureDirection.In, "test", record, 0, false, false);

    private static void Join(FourvaleSession live, string socket, string roomId, string sessionId, string classId, short level)
    {
        live.OnCaptured(Event(new JsonObject { ["kind"] = "ws_open", ["socket"] = socket, ["url"] = $"wss://api.fourvale.com/p/{roomId}?sessionId=<REDACTED>" }));
        live.OnCaptured(Event(new JsonObject
        {
            ["kind"] = "http",
            ["url"] = "https://api.fourvale.com/matchmake/joinOrCreate/town",
            ["body"] = new JsonObject { ["room"] = new JsonObject { ["roomId"] = roomId, ["name"] = "town" }, ["sessionId"] = sessionId },
        }));
        live.OnCaptured(Event(new JsonObject { ["kind"] = "ws", ["dir"] = "in", ["socket"] = socket, ["protocol"] = "JOIN_ROOM", ["handshake"] = Handshake }));
        live.OnCaptured(Event(new JsonObject { ["kind"] = "ws", ["dir"] = "in", ["socket"] = socket, ["protocol"] = "ROOM_STATE", ["bytes"] = TownState(sessionId, classId, level) }));
    }

    [Fact]
    public void Class_change_is_picked_up_from_the_next_room()
    {
        var live = new FourvaleSession();

        Join(live, "s1", "r1", "me1", "evergreen_soldier", 268);
        Assert.Equal("evergreen_soldier", live.Rooms.Current!.OwnEntry()!.Get("classId"));

        // Fourvale rejoins the town room after a class switch (discovery K1).
        live.OnCaptured(Event(new JsonObject { ["kind"] = "ws_close", ["socket"] = "s1" }));
        Join(live, "s2", "r2", "me2", "arctic_soldier", 1);

        var own = live.Rooms.Current!.OwnEntry()!;
        Assert.Equal("arctic_soldier", own.Get("classId"));
        Assert.Equal(1L, own.Get("level"));
    }

    [Fact]
    public void Hp_sync_is_remembered_and_bad_records_are_ignored()
    {
        var live = new FourvaleSession();

        live.OnCaptured(Event(new JsonObject { ["kind"] = "ws", ["dir"] = "in", ["socket"] = "x", ["protocol"] = "ROOM_DATA", ["type"] = "hpSync", ["message"] = new JsonObject { ["hp"] = 80, ["sp"] = 40 } }));
        live.OnCaptured(Event(new JsonObject { ["kind"] = "ws", ["dir"] = "in", ["socket"] = "x", ["protocol"] = "ROOM_STATE_PATCH", ["bytes"] = "not base64!" }));
        live.OnCaptured(Event(new JsonObject { ["kind"] = "ws_open" }));

        Assert.Equal((80L, 40L), (live.LastHpSync!.Value.Hp, live.LastHpSync.Value.Sp));
        Assert.Equal(2, live.BadRecords);
    }
}
