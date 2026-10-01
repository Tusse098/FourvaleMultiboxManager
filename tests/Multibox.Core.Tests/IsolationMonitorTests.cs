using Multibox.Core;

namespace Multibox.Core.Tests;

public class IsolationMonitorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly FreshnessPolicy Freshness = new(new Dictionary<string, TimeSpan> { [FreshnessPolicy.DefaultKey] = TimeSpan.FromSeconds(5) });

    private static void SetName(StateStore store, int slot, string name, DateTimeOffset at) =>
        store.Update(new SlotId(slot), s => s.Character.Name = new Field<string>(name, at, "test"));

    [Fact]
    public void Distinct_characters_are_fine()
    {
        var store = new StateStore();
        var monitor = new IsolationMonitor(Freshness);
        SetName(store, 1, "PlayerA", T0);
        SetName(store, 2, "PlayerB", T0);

        Assert.Empty(monitor.Check(store, T0));
        Assert.Equal(0, monitor.Violations);
    }

    [Fact]
    public void Two_slots_with_the_same_character_is_a_violation_reported_once()
    {
        var store = new StateStore();
        var monitor = new IsolationMonitor(Freshness);
        SetName(store, 1, "PlayerA", T0);
        SetName(store, 3, "PlayerA", T0);

        var first = monitor.Check(store, T0);
        var again = monitor.Check(store, T0.AddSeconds(1));

        Assert.Equal("slot1, slot3 report the same character", Assert.Single(first));
        Assert.Empty(again);
        Assert.Equal(1, monitor.Violations);
    }

    [Fact]
    public void Character_change_without_reload_is_a_violation()
    {
        var store = new StateStore();
        var monitor = new IsolationMonitor(Freshness);
        SetName(store, 1, "PlayerA", T0);
        monitor.Check(store, T0);

        SetName(store, 1, "PlayerB", T0.AddSeconds(1));

        Assert.Single(monitor.Check(store, T0.AddSeconds(1)));
    }

    [Fact]
    public void Character_change_after_reload_is_allowed()
    {
        var store = new StateStore();
        var monitor = new IsolationMonitor(Freshness);
        SetName(store, 1, "PlayerA", T0);
        monitor.Check(store, T0);

        store.Reset(new SlotId(1));
        monitor.Forget(new SlotId(1));
        SetName(store, 1, "PlayerB", T0.AddSeconds(1));

        Assert.Empty(monitor.Check(store, T0.AddSeconds(1)));
    }

    [Fact]
    public void Stale_names_are_ignored()
    {
        var store = new StateStore();
        var monitor = new IsolationMonitor(Freshness);
        SetName(store, 1, "PlayerA", T0);
        SetName(store, 2, "PlayerA", T0.AddSeconds(-30)); // slot 2 logged out long ago; its stale name is not "current"

        Assert.Empty(monitor.Check(store, T0));
    }

    [Fact]
    public void Reset_keeps_session_counters()
    {
        var store = new StateStore();
        store.Update(new SlotId(1), s => { s.UnexpectedDisconnects = 2; s.Browser = BrowserState.Ready; });

        store.Reset(new SlotId(1));

        Assert.Equal((2, BrowserState.Ready), (store.Get(new SlotId(1)).UnexpectedDisconnects, store.Get(new SlotId(1)).Browser));
    }
}
