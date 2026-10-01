using System.Text.Json.Nodes;
using Fourvale.Adapter;
using Fourvale.Adapter.Network;
using Fourvale.Adapter.Tests.Colyseus;
using Multibox.Core;

namespace Fourvale.Adapter.Tests;

/// <summary>The adapter's output: confirmed Core fields, freshness by source, health, and slot isolation.</summary>
public class FourvaleSessionReadTests
{
    private static readonly byte[] TownHandshake = Convert.FromBase64String(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "fixtures", "colyseus", "handshake-2026-10-01.b64")).Trim());

    // All rooms share one type list; only Reflection.rootType (byte 3: "0x81 <rootType>") differs. Battle root = type 1.
    private static readonly byte[] BattleHandshake = WithRoot(TownHandshake, 1);

    private static readonly FreshnessPolicy Freshness = new(new Dictionary<string, TimeSpan>
    {
        [FreshnessPolicy.DefaultKey] = TimeSpan.FromSeconds(5),
        [FourvaleSession.RoomStateSource] = TimeSpan.FromSeconds(5),
        [FourvaleSession.HpSyncSource] = TimeSpan.FromMinutes(10),
    });

    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static byte[] WithRoot(byte[] handshake, byte root)
    {
        Assert.Equal(new byte[] { 0x80, 0x01, 0x81 }, handshake[..3]);
        var copy = (byte[])handshake.Clone();
        copy[3] = root;
        return copy;
    }

    // Battle root: combatants (map<Combatant>). Combatant fields: classId 3, level 13 (int32), hp 14, maxHp 15, actionMeter 31 (number).
    private static byte[] BattleState(string sessionId, int hp, int maxHp, double meter) => new Bytes()
        .Raw(128 | 0).Number(1)
        .Switch(1).Raw(128).Number(0).Str(sessionId).Number(2)
        .Switch(2)
        .Raw(128 | 3).Str("arctic_soldier")
        .Raw(128 | 13).Int32(1)
        .Raw(128 | 14).Int32(hp)
        .Raw(128 | 15).Int32(maxHp)
        .Raw(128 | 31).Raw(0xcb).Raw(BitConverter.GetBytes(meter))
        .Raw(128 | 32).Number(3500) // attackRateMs
        .ToArray();

    private static CaptureEvent Event(JsonObject record) => new(CaptureDirection.In, "test", record, 0, false, false);

    private static void Join(FourvaleSession session, string socket, string roomId, string roomName, string sessionId, byte[] handshake, byte[] state)
    {
        session.OnCaptured(Event(new JsonObject { ["kind"] = "ws_open", ["socket"] = socket, ["url"] = $"wss://api.fourvale.com/p/{roomId}" }));
        session.OnCaptured(Event(new JsonObject
        {
            ["kind"] = "http",
            ["url"] = $"https://api.fourvale.com/matchmake/joinOrCreate/{roomName}",
            ["body"] = new JsonObject { ["room"] = new JsonObject { ["roomId"] = roomId, ["name"] = roomName }, ["sessionId"] = sessionId },
        }));
        session.OnCaptured(Event(new JsonObject { ["kind"] = "ws", ["dir"] = "in", ["socket"] = socket, ["protocol"] = "JOIN_ROOM", ["handshake"] = Convert.ToBase64String(handshake) }));
        session.OnCaptured(Event(new JsonObject { ["kind"] = "ws", ["dir"] = "in", ["socket"] = socket, ["protocol"] = "ROOM_STATE", ["bytes"] = Convert.ToBase64String(state) }));
    }

    private static void HpSync(FourvaleSession session, int hp, int sp) =>
        session.OnCaptured(Event(new JsonObject
        {
            ["kind"] = "ws", ["dir"] = "in", ["socket"] = "s1", ["protocol"] = "ROOM_DATA", ["type"] = "hpSync",
            ["message"] = new JsonObject { ["hp"] = hp, ["sp"] = sp },
        }));

    [Fact]
    public void Town_fields_are_confirmed_from_room_state_and_hp_from_hp_sync()
    {
        var session = new FourvaleSession();
        var state = new SlotState(new SlotId(1));
        Join(session, "s1", "r1", "town", "me1", TownHandshake, RoomTrackerTests.TownState("me1", "evergreen_soldier", 268));
        HpSync(session, 19967, 1113);

        session.ReadInto(state, T0);

        var c = state.Character;
        Assert.Equal(("evergreen_soldier", T0, FourvaleSession.RoomStateSource), (c.Class!.Value, c.Class.ConfirmedAt, c.Class.Source));
        Assert.Equal(268, c.Level!.Value);
        Assert.Equal("bellroot_a4", c.Location!.Value);
        Assert.False(c.InBattle!.Value);
        Assert.Equal((19967, FourvaleSession.HpSyncSource), (c.Hp!.Value, c.Hp.Source));
        Assert.Null(c.MaxHp); // no max HP outside battle (discovery D6)
        Assert.Equal(AdapterHealth.Ok, state.Health);
    }

    [Fact]
    public void Battle_fields_come_from_the_own_combatant()
    {
        var session = new FourvaleSession();
        var state = new SlotState(new SlotId(1));
        Join(session, "s1", "b1", "battle", "me1", BattleHandshake, BattleState("me1", hp: 60, maxHp: 80, meter: 1.0));

        session.ReadInto(state, T0);

        var c = state.Character;
        Assert.Equal((60, 80), (c.Hp!.Value, c.MaxHp!.Value));
        Assert.Equal(FourvaleSession.RoomStateSource, c.Hp.Source);
        Assert.Equal(1.0, c.ActionMeter!.Value);
        Assert.Equal(3500, c.ActionIntervalMs!.Value);
        Assert.True(c.InBattle!.Value);
        Assert.Equal("arctic_soldier", c.Class!.Value);
    }

    [Fact]
    public void Fields_go_unknown_when_the_room_closes()
    {
        var session = new FourvaleSession();
        var state = new SlotState(new SlotId(1));
        Join(session, "s1", "r1", "town", "me1", TownHandshake, RoomTrackerTests.TownState("me1", "scout", 7));
        session.ReadInto(state, T0);

        session.OnCaptured(Event(new JsonObject { ["kind"] = "ws_close", ["socket"] = "s1" }));
        session.ReadInto(state, T0.AddSeconds(3));
        Assert.True(Freshness.IsFresh(state.Character.Class, T0.AddSeconds(3))); // last confirmation 3 s ago

        session.ReadInto(state, T0.AddSeconds(6));
        Assert.False(Freshness.IsFresh(state.Character.Class, T0.AddSeconds(6))); // not re-confirmed: UNKNOWN
        Assert.Equal(AdapterHealth.Waiting, state.Health);
    }

    [Fact]
    public void Open_room_keeps_fields_fresh_without_new_patches()
    {
        var session = new FourvaleSession();
        var state = new SlotState(new SlotId(1));
        Join(session, "s1", "r1", "town", "me1", TownHandshake, RoomTrackerTests.TownState("me1", "scout", 7));

        session.ReadInto(state, T0);
        session.ReadInto(state, T0.AddMinutes(2)); // standing still in town: no patches, room still open

        Assert.True(Freshness.IsFresh(state.Character.Level, T0.AddMinutes(2)));
    }

    [Fact]
    public void Old_hp_sync_never_overrides_newer_battle_hp()
    {
        var session = new FourvaleSession();
        var state = new SlotState(new SlotId(1));
        HpSync(session, 80, 40);
        Join(session, "s1", "b1", "battle", "me1", BattleHandshake, BattleState("me1", hp: 55, maxHp: 80, meter: 0.5));

        session.ReadInto(state, DateTimeOffset.UtcNow.AddSeconds(1));

        Assert.Equal((55, FourvaleSession.RoomStateSource), (state.Character.Hp!.Value, state.Character.Hp.Source));
    }

    [Fact]
    public void Undecodable_patch_marks_the_slot_degraded_or_waiting_not_crashed()
    {
        var session = new FourvaleSession();
        var state = new SlotState(new SlotId(1));
        Join(session, "s1", "r1", "town", "me1", TownHandshake, RoomTrackerTests.TownState("me1", "scout", 7));
        session.ReadInto(state, T0);

        session.OnCaptured(Event(new JsonObject { ["kind"] = "ws", ["dir"] = "in", ["socket"] = "s1", ["protocol"] = "ROOM_STATE_PATCH", ["bytes"] = Convert.ToBase64String(new Bytes().Switch(77).ToArray()) }));
        session.ReadInto(state, T0.AddSeconds(1));

        Assert.Equal(1, state.DecodeErrors);
        Assert.NotEqual(AdapterHealth.Ok, state.Health);
    }

    [Fact]
    public void Two_sessions_write_only_to_their_own_slot()
    {
        var store = new StateStore();
        var slot1 = new SlotId(1);
        var slot2 = new SlotId(2);
        var session1 = new FourvaleSession();
        var session2 = new FourvaleSession();
        Join(session1, "s1", "r1", "town", "me1", TownHandshake, RoomTrackerTests.TownState("me1", "scout", 7));

        store.Update(slot1, s => session1.ReadInto(s, T0));
        store.Update(slot2, s => session2.ReadInto(s, T0));

        Assert.Equal("scout", store.Get(slot1).Character.Class!.Value);
        Assert.Null(store.Get(slot2).Character.Class);
        Assert.Equal(AdapterHealth.Waiting, store.Get(slot2).Health);
    }

    private static void Close(FourvaleSession session, string socket, bool leftFirst)
    {
        if (leftFirst)
        {
            session.OnCaptured(new CaptureEvent(CaptureDirection.Out, "LEAVE_ROOM",
                new JsonObject { ["kind"] = "ws", ["dir"] = "out", ["socket"] = socket, ["protocol"] = "LEAVE_ROOM" }, 0, false, false));
        }

        session.OnCaptured(Event(new JsonObject { ["kind"] = "ws_close", ["socket"] = socket }));
    }

    [Fact]
    public void Leaving_a_room_is_not_a_disconnect()
    {
        var session = new FourvaleSession();
        var state = new SlotState(new SlotId(1));
        Join(session, "s1", "r1", "town", "me1", TownHandshake, RoomTrackerTests.TownState("me1", "scout", 7));

        Close(session, "s1", leftFirst: true);
        session.ReadInto(state, T0);

        Assert.Equal(0, state.UnexpectedDisconnects);
        Assert.Equal(ConnectionState.Unknown, state.Connection);
    }

    [Fact]
    public void Server_close_counts_as_disconnect_and_rejoin_shows_reconnecting_then_connected()
    {
        var session = new FourvaleSession();
        var state = new SlotState(new SlotId(1));
        Join(session, "s1", "r1", "town", "me1", TownHandshake, RoomTrackerTests.TownState("me1", "scout", 7));
        session.ReadInto(state, T0);
        Assert.Equal(ConnectionState.Connected, state.Connection);

        Close(session, "s1", leftFirst: false);
        session.ReadInto(state, T0.AddSeconds(1));
        Assert.Equal((1, ConnectionState.Disconnected), (state.UnexpectedDisconnects, state.Connection));

        // The game reconnects: socket open, no state yet.
        session.OnCaptured(Event(new JsonObject { ["kind"] = "ws_open", ["socket"] = "s2", ["url"] = "wss://api.fourvale.com/p/r2" }));
        session.ReadInto(state, T0.AddSeconds(2));
        Assert.Equal(ConnectionState.Reconnecting, state.Connection); // open again, but no room state yet

        Join(session, "s3", "r3", "town", "me3", TownHandshake, RoomTrackerTests.TownState("me3", "scout", 7));
        session.ReadInto(state, T0.AddSeconds(3));
        Assert.Equal((1, ConnectionState.Connected), (state.UnexpectedDisconnects, state.Connection));
    }

    [Fact]
    public void Sockets_closing_after_a_reload_are_not_counted()
    {
        var session = new FourvaleSession();
        var state = new SlotState(new SlotId(1));
        Join(session, "s1", "r1", "town", "me1", TownHandshake, RoomTrackerTests.TownState("me1", "scout", 7));

        session.Reset(); // page reload
        Close(session, "s1", leftFirst: false);
        session.ReadInto(state, T0);

        Assert.Equal(0, state.UnexpectedDisconnects);
    }

    // Combatant field indexes from the real handshake: isPlayer 2, hp 14, maxHp 15, alive 33.
    private static byte[] BattleWithEnemies(string sessionId) => new Bytes()
        .Raw(128 | 0).Number(1)
        .Switch(1)
        .Raw(128).Number(0).Str(sessionId).Number(2)
        .Raw(128).Number(1).Str("enemy_0").Number(3)
        .Raw(128).Number(2).Str("enemy_1").Number(4)
        .Switch(2).Raw(128 | 2).Raw(1).Raw(128 | 14).Int32(80).Raw(128 | 15).Int32(80).Raw(128 | 33).Raw(1)
        .Switch(3).Raw(128 | 2).Raw(0).Raw(128 | 14).Int32(30).Raw(128 | 15).Int32(100).Raw(128 | 33).Raw(1)
        .Switch(4).Raw(128 | 2).Raw(0).Raw(128 | 14).Int32(0).Raw(128 | 15).Int32(100).Raw(128 | 33).Raw(0)
        .ToArray();

    private static void BattleOver(FourvaleSession session, bool win, int xp, int silver) =>
        session.OnCaptured(Event(new JsonObject
        {
            ["kind"] = "ws", ["dir"] = "in", ["socket"] = "b1", ["protocol"] = "ROOM_DATA", ["type"] = "battleOver",
            ["message"] = new JsonObject { ["win"] = win, ["defeated"] = !win, ["expEach"] = xp, ["silverEach"] = silver },
        }));

    [Fact]
    public void Enemies_alive_and_their_remaining_hp_come_from_the_battle_room()
    {
        var session = new FourvaleSession();
        var state = new SlotState(new SlotId(1));
        Join(session, "b1", "r1", "battle", "me1", BattleHandshake, BattleWithEnemies("me1"));

        session.ReadInto(state, T0);

        Assert.Equal(1, state.Character.EnemiesAlive!.Value);              // own combatant is not an enemy
        Assert.Equal(30 / 200.0, state.Character.EnemyHpFraction!.Value, 6); // 30 of 100+100 left
    }

    [Fact]
    public void Battle_results_add_up_per_slot()
    {
        var session = new FourvaleSession();
        var state = new SlotState(new SlotId(1));

        BattleOver(session, win: true, xp: 1099, silver: 276);
        BattleOver(session, win: true, xp: 1755, silver: 364);
        BattleOver(session, win: false, xp: 0, silver: 0);
        session.ReadInto(state, T0);
        session.ReadInto(state, T0.AddSeconds(1)); // counted once

        Assert.Equal((2, 1, 2854L, 640L), (state.Battles.Won, state.Battles.Lost, state.Battles.XpGained, state.Battles.SilverGained));
    }

    // Town root with a named own player: players[sessionId] = { name (1), classId (2), level (3, int16) }.
    private static byte[] NamedTownState(string sessionId, string name) => new Bytes()
        .Raw(128 | 0).Str("bellroot_a4")
        .Raw(128 | 1).Number(1)
        .Switch(1).Raw(128).Number(0).Str(sessionId).Number(2)
        .Switch(2).Raw(128 | 1).Str(name).Raw(128 | 2).Str("scout").Raw(128 | 3).Raw(BitConverter.GetBytes((short)7))
        .ToArray();

    [Fact]
    public void Battle_totals_start_again_for_a_different_character_but_survive_a_reload()
    {
        var store = new StateStore();
        var slot = new SlotId(1);
        var session = new FourvaleSession();
        Join(session, "s1", "r1", "town", "me1", TownHandshake, NamedTownState("me1", "PlayerA"));
        store.Update(slot, s => session.ReadInto(s, T0));
        BattleOver(session, true, 100, 10);
        store.Update(slot, s => session.ReadInto(s, T0.AddSeconds(1)));

        store.Reset(slot); // page reload: totals kept
        Assert.Equal(1, store.Get(slot).Battles.Won);

        // Same character after the reload: still counted together.
        session.Reset();
        Join(session, "s2", "r2", "town", "me2", TownHandshake, NamedTownState("me2", "PlayerA"));
        store.Update(slot, s => session.ReadInto(s, T0.AddSeconds(2)));
        Assert.Equal(1, store.Get(slot).Battles.Won);

        // A different character logs in on this slot: totals start again.
        session.Reset();
        Join(session, "s3", "r3", "town", "me3", TownHandshake, NamedTownState("me3", "PlayerB"));
        store.Update(slot, s => session.ReadInto(s, T0.AddSeconds(3)));
        Assert.Equal(0, store.Get(slot).Battles.Won);
    }
}
