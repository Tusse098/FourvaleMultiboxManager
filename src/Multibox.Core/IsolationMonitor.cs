namespace Multibox.Core;

/// <summary>
/// Detects cross-slot attribution (spec §15 criterion 2) from the state store alone:
/// <list type="bullet">
/// <item>two slots reporting the same current character, or</item>
/// <item>a slot's character changing without the slot being reset (page reload/login).</item>
/// </list>
/// A class change keeps the character name, so it is not a violation (discovery K1).
/// Call <see cref="Check"/> after each adapter read; each incident is reported once.
/// </summary>
public sealed class IsolationMonitor(FreshnessPolicy freshness)
{
    private readonly Dictionary<SlotId, string> _identity = [];
    private readonly HashSet<string> _openIncidents = [];

    public int Violations { get; private set; }

    /// <summary>Forget a slot's identity after its page reloaded or the slot was closed: a new login may follow.</summary>
    public void Forget(SlotId slot) => _identity.Remove(slot);

    /// <returns>Descriptions of new incidents (no character names, which are personal data).</returns>
    public IReadOnlyList<string> Check(StateStore store, DateTimeOffset now)
    {
        var incidents = new List<string>();
        var current = new Dictionary<SlotId, string>();

        foreach (var slot in store.Slots)
        {
            if (freshness.Current(store.Get(slot).Character.Name, now) is { } name)
            {
                current[slot] = name.Value;
            }
        }

        foreach (var (slot, name) in current)
        {
            if (_identity.TryGetValue(slot, out var known) && known != name)
            {
                Report($"identity-change:{slot}", $"{slot} changed character without a reload", incidents);
            }

            _identity.TryAdd(slot, name);
        }

        foreach (var group in current.GroupBy(p => p.Value).Where(g => g.Count() > 1))
        {
            var slots = string.Join(", ", group.Select(p => p.Key).OrderBy(s => s.Number));
            Report($"duplicate:{slots}", $"{slots} report the same character", incidents);
        }

        return incidents;
    }

    private void Report(string key, string description, List<string> incidents)
    {
        if (_openIncidents.Add(key))
        {
            Violations++;
            incidents.Add(description);
        }
    }
}
