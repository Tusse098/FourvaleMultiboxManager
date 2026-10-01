using Fourvale.Adapter.Colyseus;

namespace Fourvale.Adapter.Tests.Colyseus;

public class RoomTrackerTests
{
    private static readonly byte[] Handshake = Convert.FromBase64String(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "fixtures", "colyseus", "handshake-2026-10-01.b64")).Trim());

    // Town root (type 3): mapId, players: map<Player(type 2)>. Player fields: id, name, classId, level(int16), …
    internal static byte[] TownState(string sessionId, string classId, short level) => new Bytes()
        .Raw(128 | 0).Str("bellroot_a4")
        .Raw(128 | 1).Number(1)
        .Switch(1).Raw(128).Number(0).Str(sessionId).Number(2)
        .Switch(2).Raw(128 | 2).Str(classId).Raw(128 | 3).Raw(BitConverter.GetBytes(level))
        .ToArray();

    [Fact]
    public void Own_entry_is_found_when_matchmake_arrives_after_the_socket()
    {
        var tracker = new RoomTracker();
        tracker.OnSocketOpened("sock1", "wss://api.fourvale.com/proc/room42?sessionId=<REDACTED>");
        tracker.OnMatchmake("room42", "town", "me1");
        tracker.OnHandshake("sock1", Handshake);
        tracker.OnFullState("sock1", TownState("me1", "scout", 7));

        var room = Assert.IsType<TrackedRoom>(tracker.Current);
        var own = Assert.IsType<SchemaObject>(room.OwnEntry());

        Assert.Equal("town", room.Name);
        Assert.Equal("scout", own.Get("classId"));
        Assert.Equal(7L, own.Get("level"));
    }

    [Fact]
    public void Latest_room_with_state_is_current_and_closed_rooms_disappear()
    {
        var tracker = new RoomTracker();
        tracker.OnMatchmake("r1", "town", "a");
        tracker.OnSocketOpened("s1", "wss://h/p/r1");
        tracker.OnHandshake("s1", Handshake);
        tracker.OnFullState("s1", TownState("a", "scout", 1));
        tracker.OnMatchmake("r2", "town", "b");
        tracker.OnSocketOpened("s2", "wss://h/p/r2");
        tracker.OnHandshake("s2", Handshake);
        tracker.OnFullState("s2", TownState("b", "arctic_soldier", 1));

        Assert.Equal("r2", tracker.Current!.RoomId);

        tracker.OnSocketClosed("s2");
        Assert.Equal("r1", tracker.Current!.RoomId);
    }

    [Fact]
    public void Malformed_patch_is_counted_and_drops_state_without_throwing()
    {
        var tracker = new RoomTracker();
        tracker.OnMatchmake("r1", "town", "me1");
        tracker.OnSocketOpened("s1", "wss://h/p/r1");
        tracker.OnHandshake("s1", Handshake);
        tracker.OnFullState("s1", TownState("me1", "scout", 7));

        var changes = tracker.OnPatch("s1", new Bytes().Switch(99).ToArray());

        Assert.Empty(changes);
        Assert.Equal(1, tracker.TotalErrors);
        Assert.Null(tracker.Current); // stale state is not shown as current
    }
}
