using Multibox.Core;

namespace Multibox.Core.Tests;

public class SlotNavigatorTests
{
    private static readonly SlotId S1 = new(1), S2 = new(2), S3 = new(3), S5 = new(5);
    private static readonly IReadOnlyList<SlotId> Open = [S1, S2, S5];

    [Fact]
    public void Cycle_moves_through_open_slots_and_wraps()
    {
        Assert.Equal(S2, SlotNavigator.Cycle(Open, S1, +1));
        Assert.Equal(S5, SlotNavigator.Cycle(Open, S2, +1));
        Assert.Equal(S1, SlotNavigator.Cycle(Open, S5, +1));
        Assert.Equal(S5, SlotNavigator.Cycle(Open, S1, -1));
    }

    [Fact]
    public void Cycle_from_nothing_or_a_closed_slot_starts_at_an_end()
    {
        Assert.Equal(S1, SlotNavigator.Cycle(Open, null, +1));
        Assert.Equal(S5, SlotNavigator.Cycle(Open, S3, -1));
        Assert.Null(SlotNavigator.Cycle([], S1, +1));
    }

    [Fact]
    public void Next_ready_skips_busy_slots_and_starts_after_the_current_one()
    {
        var ready = new HashSet<SlotId> { S1, S5 };

        Assert.Equal(S5, SlotNavigator.NextReady(Open, S1, ready.Contains));
        Assert.Equal(S1, SlotNavigator.NextReady(Open, S5, ready.Contains));
        Assert.Equal(S5, SlotNavigator.NextReady(Open, S2, ready.Contains));
    }

    [Fact]
    public void Next_ready_returns_the_current_slot_when_it_is_the_only_ready_one()
    {
        Assert.Equal(S2, SlotNavigator.NextReady(Open, S2, s => s == S2));
    }

    [Fact]
    public void Next_ready_is_null_when_nobody_is_ready()
    {
        Assert.Null(SlotNavigator.NextReady(Open, S1, _ => false));
    }

    [Fact]
    public void Ready_needs_a_fresh_full_action_meter()
    {
        var now = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var freshness = new FreshnessPolicy(new Dictionary<string, TimeSpan> { [FreshnessPolicy.DefaultKey] = TimeSpan.FromSeconds(5) });
        var state = new SlotState(S1);

        state.Character.ActionMeter = new Field<double>(0.9, now, "room-state");
        Assert.False(SlotNavigator.IsReady(state, freshness, now));

        state.Character.ActionMeter = new Field<double>(1.0, now, "room-state");
        Assert.True(SlotNavigator.IsReady(state, freshness, now));
        Assert.False(SlotNavigator.IsReady(state, freshness, now.AddSeconds(10))); // stale: not ready
    }

    [Fact]
    public void Next_to_act_prefers_ready_slots_and_cycles_through_them()
    {
        var seconds = new Dictionary<SlotId, double?> { [S1] = 0, [S2] = 1.5, [S5] = 0 };

        Assert.Equal(S5, SlotNavigator.NextToAct(Open, S1, s => seconds[s]));
        Assert.Equal(S1, SlotNavigator.NextToAct(Open, S5, s => seconds[s]));
    }

    [Fact]
    public void Next_to_act_without_anyone_ready_is_the_lowest_timer()
    {
        var seconds = new Dictionary<SlotId, double?> { [S1] = 2.0, [S2] = 0.4, [S5] = 1.1 };

        Assert.Equal(S2, SlotNavigator.NextToAct(Open, S1, s => seconds[s]));
        Assert.Equal(S2, SlotNavigator.NextToAct(Open, S2, s => seconds[s])); // already on the soonest: stay
    }

    [Fact]
    public void Next_to_act_ignores_slots_out_of_battle_and_breaks_ties_after_the_current_slot()
    {
        var seconds = new Dictionary<SlotId, double?> { [S1] = 0.8, [S2] = null, [S5] = 0.8 };

        Assert.Equal(S5, SlotNavigator.NextToAct(Open, S2, s => seconds[s]));
        Assert.Equal(S1, SlotNavigator.NextToAct(Open, S5, s => seconds[s]));
        Assert.Null(SlotNavigator.NextToAct(Open, S1, _ => null)); // nobody in battle
    }

    [Fact]
    public void Seconds_until_ready_come_from_meter_and_interval()
    {
        var now = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var freshness = new FreshnessPolicy(new Dictionary<string, TimeSpan> { [FreshnessPolicy.DefaultKey] = TimeSpan.FromSeconds(5) });
        var state = new SlotState(S1);

        Assert.Null(SlotNavigator.SecondsUntilReady(state, freshness, now)); // no timer: not in battle

        state.Character.ActionMeter = new Field<double>(0.6, now, "room-state");
        state.Character.ActionIntervalMs = new Field<double>(3500, now, "room-state");
        Assert.Equal(1.4, SlotNavigator.SecondsUntilReady(state, freshness, now)!.Value, 6);

        state.Character.ActionMeter = new Field<double>(1, now, "room-state");
        Assert.Equal(0, SlotNavigator.SecondsUntilReady(state, freshness, now));

        state.Character.InBattle = new Field<bool>(false, now, "room-state");
        Assert.Null(SlotNavigator.SecondsUntilReady(state, freshness, now)); // battle over: the old meter is not a timer
    }

    [Fact]
    public void Next_to_act_after_ready_slots_goes_to_slots_waiting_after_a_battle_then_the_lowest_timer()
    {
        var seconds = new Dictionary<SlotId, double?> { [S1] = 0.8, [S2] = null, [S5] = 2.0 };
        var waiting = new HashSet<SlotId> { S2 };

        Assert.Equal(S2, SlotNavigator.NextToAct(Open, S1, s => seconds[s], waiting.Contains));
        Assert.Equal(S2, SlotNavigator.NextToAct(Open, S5, s => seconds[s], waiting.Contains));

        seconds[S5] = 0; // someone in battle is READY: that comes first
        Assert.Equal(S5, SlotNavigator.NextToAct(Open, S1, s => seconds[s], waiting.Contains));

        seconds[S5] = 2.0;
        Assert.Equal(S1, SlotNavigator.NextToAct(Open, S5, s => seconds[s], _ => false)); // nobody waiting: lowest timer
    }

    [Fact]
    public void Waiting_after_battle_needs_a_finished_battle_and_a_current_out_of_battle_state()
    {
        var now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        var freshness = new FreshnessPolicy(new Dictionary<string, TimeSpan> { [FreshnessPolicy.DefaultKey] = TimeSpan.FromSeconds(5) });
        var state = new SlotState(S1);
        state.Character.InBattle = new Field<bool>(false, now, "room-state");

        Assert.False(SlotNavigator.IsWaitingAfterBattle(state, freshness, now)); // idle in town, never fought: not waiting

        state.Battles.LastBattleAt = now.AddSeconds(-3);
        Assert.True(SlotNavigator.IsWaitingAfterBattle(state, freshness, now));

        state.Character.InBattle = new Field<bool>(true, now, "room-state");
        Assert.False(SlotNavigator.IsWaitingAfterBattle(state, freshness, now)); // next fight started

        state.Character.InBattle = new Field<bool>(false, now.AddSeconds(-10), "room-state");
        Assert.False(SlotNavigator.IsWaitingAfterBattle(state, freshness, now)); // stale: unknown, not waiting
    }
}
