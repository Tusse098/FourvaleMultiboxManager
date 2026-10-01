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
    /// The first ready slot after <paramref name="current"/> in slot order, wrapping around and ending with
    /// <paramref name="current"/> itself. Null when no slot is ready.
    /// </summary>
    public static SlotId? NextReady(IReadOnlyList<SlotId> open, SlotId? current, Func<SlotId, bool> isReady)
    {
        if (open.Count == 0)
        {
            return null;
        }

        var start = current is { } c ? IndexOf(open, c) : -1;
        for (var i = 1; i <= open.Count; i++)
        {
            var candidate = open[((start + i) % open.Count + open.Count) % open.Count];
            if (isReady(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// The slot that acts next: the first READY slot after <paramref name="current"/> (so pressing again cycles through
    /// everyone who is ready); if nobody is ready, the slot with the least time left on its action timer (ties: the first
    /// after <paramref name="current"/>). Null when no slot has a current timer (nobody in battle).
    /// </summary>
    /// <param name="secondsUntilReady">Seconds until the slot can act (0 = ready), or null when not in battle / unknown.</param>
    public static SlotId? NextToAct(IReadOnlyList<SlotId> open, SlotId? current, Func<SlotId, double?> secondsUntilReady)
    {
        if (NextReady(open, current, s => secondsUntilReady(s) is <= 0) is { } ready)
        {
            return ready;
        }

        if (open.Count == 0)
        {
            return null;
        }

        var start = current is { } c ? IndexOf(open, c) : -1;
        SlotId? best = null;
        var bestSeconds = double.MaxValue;
        for (var i = 1; i <= open.Count; i++)
        {
            var candidate = open[((start + i) % open.Count + open.Count) % open.Count];
            if (secondsUntilReady(candidate) is { } seconds && seconds < bestSeconds)
            {
                best = candidate;
                bestSeconds = seconds;
            }
        }

        return best;
    }

    /// <summary>
    /// Seconds until the slot's character can act: 0 when the action meter is full, otherwise
    /// (1 - meter) × action interval. Null when the meter or interval is not current (e.g. not in battle).
    /// </summary>
    public static double? SecondsUntilReady(SlotState state, FreshnessPolicy freshness, DateTimeOffset now)
    {
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

    /// <summary>A slot is ready when its action meter is current and full (discovery D4, spec §10.3).</summary>
    public static bool IsReady(SlotState state, FreshnessPolicy freshness, DateTimeOffset now) =>
        freshness.Current(state.Character.ActionMeter, now) is { Value: >= 1 };

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
