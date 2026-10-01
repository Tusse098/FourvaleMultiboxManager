using System.Text.Json.Nodes;
using Fourvale.Adapter.Colyseus;
using Fourvale.Adapter.Network;
using Multibox.Core;

namespace Fourvale.Adapter;

/// <summary>
/// The adapter for one slot: fed with redacted <see cref="CaptureEvent"/>s from that slot's
/// <see cref="NetworkObserver"/> (so it never sees raw frames or tokens), it follows the slot's rooms
/// and writes confirmed fields into the slot's <see cref="SlotState"/> (ADR 0003).
/// One instance per slot; never shared, so data cannot be attributed to another slot.
/// </summary>
public sealed class FourvaleSession
{
    /// <summary>Fields read from the live room state; re-confirmed on every read while the room is open.</summary>
    public const string RoomStateSource = "room-state";

    /// <summary>HP/SP from the <c>hpSync</c> message, sent once per room join.</summary>
    public const string HpSyncSource = "hpSync";

    private readonly HashSet<string> _leaving = [];
    private int _unexpectedSinceRead;
    private bool _lastCloseUnexpected;
    private readonly List<(bool Won, long Xp, long Silver, DateTimeOffset At)> _finishedBattles = [];
    private string? _battleStatsOwner;
    private int _patchesSinceRead;
    private int _errorsAtLastRead;
    private DateTimeOffset? _lastRead;

    public RoomTracker Rooms { get; } = new();

    /// <summary>Last <c>hpSync</c> message (HP/SP sent on every room join, the only HP source outside battle).</summary>
    public (long Hp, long Sp, DateTimeOffset At)? LastHpSync { get; private set; }

    /// <summary>Records that could not be used at all (missing fields, bad base64).</summary>
    public int BadRecords { get; private set; }

    public void OnCaptured(CaptureEvent e)
    {
        var record = e.Record;
        if ((string?)record["protocol"] is "ROOM_STATE_PATCH" or "ROOM_STATE")
        {
            _patchesSinceRead++;
        }

        try
        {
            switch ((string?)record["kind"])
            {
                case "http" when ((string?)record["url"])?.Contains("/matchmake/", StringComparison.Ordinal) == true:
                    var body = record["body"];
                    if ((string?)body?["room"]?["roomId"] is { } roomId)
                    {
                        Rooms.OnMatchmake(roomId, (string?)body["room"]?["name"] ?? "?", (string?)body["sessionId"] ?? "");
                    }

                    break;

                case "ws_open":
                    Rooms.OnSocketOpened((string)record["socket"]!, (string?)record["url"] ?? "");
                    break;

                case "ws" when (string?)record["dir"] == "out" && (string?)record["protocol"] == "LEAVE_ROOM":
                    _leaving.Add((string)record["socket"]!);
                    break;

                case "ws_close":
                    OnSocketClosed((string)record["socket"]!);
                    break;

                case "ws" when (string?)record["dir"] == "in":
                    OnIncomingFrame(record);
                    break;
            }
        }
        catch (Exception)
        {
            // A malformed record must never disturb the session (CLAUDE.md: catch and count, never throw into the session).
            BadRecords++;
        }
    }

    /// <summary>
    /// A room socket that closes without the game leaving the room first was closed by the server or the network.
    /// Sockets not tracked (e.g. from before a reload) are ignored.
    /// </summary>
    private void OnSocketClosed(string socket)
    {
        var tracked = Rooms.Rooms.Any(r => r.SocketId == socket);
        var leaving = _leaving.Remove(socket);
        Rooms.OnSocketClosed(socket);

        if (tracked && !leaving)
        {
            _unexpectedSinceRead++;
            _lastCloseUnexpected = true;
        }
    }

    public void Reset()
    {
        _leaving.Clear();
        _lastCloseUnexpected = false;
        Rooms.Reset();
        LastHpSync = null;
        _patchesSinceRead = 0;
    }

    public int Errors => Rooms.TotalErrors + BadRecords;

    /// <summary>
    /// Confirms every field that a live source currently backs. Fields with no live source keep their
    /// old confirmation time and go UNKNOWN when it exceeds the freshness limit.
    /// </summary>
    public void ReadInto(SlotState state, DateTimeOffset now)
    {
        var character = state.Character;
        var room = Rooms.Current;
        var own = room?.OwnEntry();

        if (own is not null)
        {
            Field<T> Live<T>(T value) => new(value, now, RoomStateSource);

            if (own.Get("name") is string name) character.Name = Live(name);
            if (own.Get("classId") is string classId) character.Class = Live(classId);
            if (ToInt(own.Get("level")) is { } level) character.Level = Live(level);

            var map = own.Get("mapId") as string ?? room!.State?.Root.Get("mapId") as string;
            if (map is not null) character.Location = Live(map);

            var inBattle = room!.Name == "battle" || own.Get("inBattle") is true;
            character.InBattle = Live(inBattle);

            // Battle combatants carry live HP/SP and the action meter (discovery D3, D4).
            if (ToInt(own.Get("maxHp")) is { } maxHp && ToInt(own.Get("hp")) is { } hp)
            {
                character.Hp = Live(hp);
                character.MaxHp = Live(maxHp);
            }

            if (ToInt(own.Get("maxSp")) is { } maxSp && ToInt(own.Get("sp")) is { } sp)
            {
                character.Sp = Live(sp);
                character.MaxSp = Live(maxSp);
            }

            if (own.Get("attackRateMs") is { } interval and (long or double))
            {
                character.ActionIntervalMs = Live(Convert.ToDouble(interval, System.Globalization.CultureInfo.InvariantCulture));
            }

            if (own.Get("actionMeter") is { } meter and (long or double))
            {
                character.ActionMeter = Live(Math.Clamp(Convert.ToDouble(meter, System.Globalization.CultureInfo.InvariantCulture), 0, 1));
            }
        }

        ReadBattle(state, room, now);

        // Outside battle, hpSync (on room join) is the only HP source; it never overrides newer room-state HP.
        if (LastHpSync is { } sync && (character.Hp is null || sync.At > character.Hp.ConfirmedAt))
        {
            character.Hp = new Field<int>((int)sync.Hp, sync.At, HpSyncSource);
            character.Sp = new Field<int>((int)sync.Sp, sync.At, HpSyncSource);
        }

        if (room is not null)
        {
            _lastCloseUnexpected = false; // room state is flowing again
        }

        var anyOpen = Rooms.Rooms.Any();
        state.Connection = room is not null ? ConnectionState.Connected
            : anyOpen && _lastCloseUnexpected ? ConnectionState.Reconnecting
            : anyOpen ? ConnectionState.Connected
            : _lastCloseUnexpected ? ConnectionState.Disconnected
            : ConnectionState.Unknown;
        state.UnexpectedDisconnects += _unexpectedSinceRead;
        _unexpectedSinceRead = 0;

        var newErrors = Errors - _errorsAtLastRead;
        _errorsAtLastRead = Errors;
        state.DecodeErrors = Errors;
        state.Health = room is null
            ? (newErrors > 0 && _patchesSinceRead == 0 ? AdapterHealth.Broken : AdapterHealth.Waiting)
            : newErrors > 0 ? AdapterHealth.Degraded : AdapterHealth.Ok;

        var elapsed = _lastRead is { } last ? (now - last).TotalSeconds : 0;
        state.UpdatesPerSecond = elapsed > 0 ? _patchesSinceRead / elapsed : 0;
        if (_patchesSinceRead > 0)
        {
            state.LastUpdate = now;
        }

        _patchesSinceRead = 0;
        _lastRead = now;
    }

    /// <summary>
    /// Battle totals (from battleOver) and, inside a battle room, the opponents still alive and their remaining HP.
    /// Totals start again when a different character shows up in the slot.
    /// </summary>
    private void ReadBattle(SlotState state, TrackedRoom? room, DateTimeOffset now)
    {
        if (state.Character.Name is { } name && name.ConfirmedAt == now)
        {
            if (_battleStatsOwner is not null && _battleStatsOwner != name.Value)
            {
                state.Battles = new BattleStats();
            }

            _battleStatsOwner = name.Value;
        }

        foreach (var battle in _finishedBattles)
        {
            if (battle.Won) state.Battles.Won++; else state.Battles.Lost++;
            state.Battles.XpGained += battle.Xp;
            state.Battles.SilverGained += battle.Silver;
            state.Battles.LastBattleAt = battle.At;
        }

        _finishedBattles.Clear();

        if (room?.Name != "battle" || room.State?.Root.Get("combatants") is not StateCollection combatants)
        {
            return;
        }

        var alive = 0;
        long hp = 0, maxHp = 0;
        foreach (var combatant in combatants.Values.OfType<SchemaObject>())
        {
            if (combatant.Get("isPlayer") is true)
            {
                continue;
            }

            maxHp += ToInt(combatant.Get("maxHp")) ?? 0;
            if (combatant.Get("alive") is true)
            {
                alive++;
                hp += Math.Max(0, ToInt(combatant.Get("hp")) ?? 0);
            }
        }

        state.Character.EnemiesAlive = new Field<int>(alive, now, RoomStateSource);
        if (maxHp > 0)
        {
            state.Character.EnemyHpFraction = new Field<double>(Math.Clamp(hp / (double)maxHp, 0, 1), now, RoomStateSource);
        }
    }

    private static int? ToInt(object? value) => value switch
    {
        long l when l is >= int.MinValue and <= int.MaxValue => (int)l,
        double d when double.IsFinite(d) => (int)Math.Round(d),
        _ => null,
    };

    private void OnIncomingFrame(JsonObject record)
    {
        var socket = (string)record["socket"]!;
        switch ((string?)record["protocol"])
        {
            case "JOIN_ROOM" when record["handshake"] is not null:
                Rooms.OnHandshake(socket, Convert.FromBase64String((string)record["handshake"]!));
                break;

            case "ROOM_STATE" when record["bytes"] is not null:
                Rooms.OnFullState(socket, Convert.FromBase64String((string)record["bytes"]!));
                break;

            case "ROOM_STATE_PATCH" when record["bytes"] is not null:
                Rooms.OnPatch(socket, Convert.FromBase64String((string)record["bytes"]!));
                break;

            // Battle result for this slot's character (discovery C3): {win, defeated, expEach, silverEach}.
            case "ROOM_DATA" when (string?)record["type"] == "battleOver":
                var result = record["message"];
                if (result is JsonObject)
                {
                    _finishedBattles.Add((
                        (bool?)result["win"] ?? false,
                        ToLong(result["expEach"]) ?? 0,
                        ToLong(result["silverEach"]) ?? 0,
                        DateTimeOffset.UtcNow));
                }

                break;

            case "ROOM_DATA" when (string?)record["type"] == "hpSync":
                var message = record["message"];
                if (ToLong(message?["hp"]) is { } hp)
                {
                    LastHpSync = (hp, ToLong(message?["sp"]) ?? 0, DateTimeOffset.UtcNow);
                }

                break;
        }
    }

    // Decoded MessagePack numbers arrive as int, uint, long or double JSON values depending on their size.
    private static long? ToLong(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<long>(out var l)) return l;
        if (value.TryGetValue<int>(out var i)) return i;
        if (value.TryGetValue<uint>(out var u)) return u;
        if (value.TryGetValue<short>(out var s)) return s;
        if (value.TryGetValue<ushort>(out var us)) return us;
        if (value.TryGetValue<byte>(out var b)) return b;
        if (value.TryGetValue<sbyte>(out var sb)) return sb;
        if (value.TryGetValue<ulong>(out var ul) && ul <= long.MaxValue) return (long)ul;
        if (value.TryGetValue<double>(out var d) && double.IsFinite(d)) return (long)Math.Round(d);
        return null;
    }
}
