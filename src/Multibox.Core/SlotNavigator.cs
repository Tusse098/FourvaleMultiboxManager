namespace Multibox.Core;

/// <summary>
/// Which slot gets focus next. Pure functions over the open slots in slot order, so the rules are testable.
/// Only moves focus; never sends anything to the game (spec §3.2 Tier A).
/// </summary>
public static class SlotNavigator
{
    /// <summary>The next (or previous) open slot after <paramref name="current"/>, wrapping around.</summary>
    public static SlotId? Cycle(IReadOnlyList<SlotId> open, SlotId? current, int direction)
    {
        if (open.Count == 0)
        {
            return null;
        }

        var index = current is { } c ? IndexOf(open, c) : -1;
        if (index < 0)
        {
            return direction >= 0 ? open[0] : open[^1];
        }

        var step = direction >= 0 ? 1 : -1;
        return open[(index + step + open.Count) % open.Count];
    }

    /// <summary>
    /// Seconds until the slot's character can act: 0 when the action meter is full, otherwise
    /// (1 - meter) × action interval. Null when the meter or interval is not current (e.g. not in battle).
    /// </summary>
    public static double? SecondsUntilReady(SlotState state, FreshnessPolicy freshness, DateTimeOffset now)
    {
        // Out of battle the last meter value lingers until it goes stale; it is not a timer any more.
        if (freshness.Current(state.Character.InBattle, now) is { Value: false })
        {
            return null;
        }

        if (freshness.Current(state.Character.ActionMeter, now) is not { } meter)
        {
            return null;
        }

        if (meter.Value >= 1)
        {
            return 0;
        }

        return freshness.Current(state.Character.ActionIntervalMs, now) is { Value: > 0 } interval
            ? (1 - meter.Value) * interval.Value / 1000
            : null;
    }

    private static int IndexOf(IReadOnlyList<SlotId> open, SlotId slot)
    {
        for (var i = 0; i < open.Count; i++)
        {
            if (open[i] == slot)
            {
                return i;
            }
        }

        return -1;
    }
}
