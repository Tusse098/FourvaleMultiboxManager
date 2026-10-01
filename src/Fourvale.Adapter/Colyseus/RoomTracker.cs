namespace Fourvale.Adapter.Colyseus;

/// <summary>
/// Follows the Colyseus rooms of one session (one slot): which socket belongs to which room,
/// this session's own seat (sessionId) in it, and the decoded room state.
/// Never throws on bad input: errors are counted and the room's state is dropped until the next full state.
/// </summary>
public sealed class RoomTracker
{
    private readonly Dictionary<string, (string Name, string SessionId)> _seats = [];
    private readonly Dictionary<string, TrackedRoom> _rooms = [];

    /// <summary>The most recently opened room that has decoded state.</summary>
    public TrackedRoom? Current =>
        _rooms.Values.Where(r => r.State is not null).OrderByDescending(r => r.Sequence).FirstOrDefault();

    public IEnumerable<TrackedRoom> Rooms => _rooms.Values;
    public int TotalErrors { get; private set; }

    private long _sequence;

    /// <summary>Matchmake response: the room this session was seated in, and its own sessionId there.</summary>
    public void OnMatchmake(string roomId, string roomName, string sessionId)
    {
        _seats[roomId] = (roomName, sessionId);

        // The response is often observed just after its socket opened.
        foreach (var room in _rooms.Values.Where(r => r.RoomId == roomId))
        {
            room.Seat(roomName, sessionId);
        }
    }

    /// <summary>Socket URL is <c>wss://host/&lt;processId&gt;/&lt;roomId&gt;?…</c>.</summary>
    public TrackedRoom OnSocketOpened(string socketId, string url)
    {
        var roomId = Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? ""
            : "";
        var room = new TrackedRoom(socketId, roomId, ++_sequence);
        if (_seats.TryGetValue(roomId, out var seat))
        {
            room.Seat(seat.Name, seat.SessionId);
        }

        _rooms[socketId] = room;
        return room;
    }

    public void OnSocketClosed(string socketId) => _rooms.Remove(socketId);

    public void OnHandshake(string socketId, ReadOnlySpan<byte> handshake)
    {
        if (!_rooms.TryGetValue(socketId, out var room))
        {
            return;
        }

        try
        {
            room.Context = SchemaContext.FromHandshake(handshake);
        }
        catch (FormatException)
        {
            Fail(room);
        }
    }

    public void OnFullState(string socketId, ReadOnlySpan<byte> state)
    {
        if (!_rooms.TryGetValue(socketId, out var room) || room.Context is null)
        {
            return;
        }

        try
        {
            room.State = new StateDecoder(room.Context);
            room.Apply(state);
        }
        catch (FormatException)
        {
            Fail(room);
        }
    }

    public IReadOnlyList<StateChange> OnPatch(string socketId, ReadOnlySpan<byte> patch)
    {
        if (!_rooms.TryGetValue(socketId, out var room) || room.State is null)
        {
            return [];
        }

        try
        {
            return room.Apply(patch);
        }
        catch (FormatException)
        {
            Fail(room);
            return [];
        }
    }

    public void Reset()
    {
        _rooms.Clear();
        _seats.Clear();
    }

    // State may be half-applied: drop it until the next full state (spec §7.2: the adapter never throws into the session).
    private void Fail(TrackedRoom room)
    {
        room.Errors++;
        room.State = null;
        TotalErrors++;
    }
}

public sealed class TrackedRoom(string socketId, string roomId, long sequence)
{
    public string SocketId { get; } = socketId;
    public string RoomId { get; } = roomId;
    internal long Sequence { get; } = sequence;
    public string Name { get; private set; } = "?";
    public string OwnSessionId { get; private set; } = "";
    public SchemaContext? Context { get; internal set; }
    public StateDecoder? State { get; internal set; }
    public int Errors { get; internal set; }
    public int Patches { get; private set; }
    public DateTimeOffset LastUpdate { get; private set; }

    internal void Seat(string name, string sessionId)
    {
        Name = name;
        OwnSessionId = sessionId;
    }

    internal IReadOnlyList<StateChange> Apply(ReadOnlySpan<byte> bytes)
    {
        var changes = State!.Apply(bytes);
        Patches++;
        LastUpdate = DateTimeOffset.UtcNow;
        return changes;
    }

    /// <summary>This session's own entry: the item keyed by its sessionId in any map on the room root.</summary>
    public SchemaObject? OwnEntry()
    {
        if (State is null || OwnSessionId.Length == 0)
        {
            return null;
        }

        foreach (var (field, value) in State.Root.Fields())
        {
            if (field.Kind == FieldKind.Map && value is StateCollection map && map.GetByKey(OwnSessionId) is SchemaObject own)
            {
                return own;
            }
        }

        return null;
    }
}
