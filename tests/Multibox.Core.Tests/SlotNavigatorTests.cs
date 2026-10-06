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
}
