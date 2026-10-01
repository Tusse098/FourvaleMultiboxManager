namespace Multibox.Core;

/// <summary>
/// Decides whether a field may be shown as current. Limits are per source and come from config
/// (spec §6.1, ADR 0003): a field older than its source's limit renders as UNKNOWN.
/// </summary>
public sealed class FreshnessPolicy
{
    public const string DefaultKey = "default";

    private readonly IReadOnlyDictionary<string, TimeSpan> _limits;

    public FreshnessPolicy(IReadOnlyDictionary<string, TimeSpan> limits)
    {
        if (!limits.ContainsKey(DefaultKey))
        {
            throw new ArgumentException($"Freshness limits must include a '{DefaultKey}' entry.", nameof(limits));
        }

        if (limits.Values.Any(l => l <= TimeSpan.Zero))
        {
            throw new ArgumentException("Freshness limits must be positive.", nameof(limits));
        }

        _limits = limits;
    }

    public TimeSpan LimitFor(string source) =>
        _limits.TryGetValue(source, out var limit) ? limit : _limits[DefaultKey];

    public bool IsFresh<T>(Field<T>? field, DateTimeOffset now) =>
        field is not null && now - field.ConfirmedAt <= LimitFor(field.Source);

    /// <summary>The value if fresh, otherwise null (render as UNKNOWN).</summary>
    public Field<T>? Current<T>(Field<T>? field, DateTimeOffset now) => IsFresh(field, now) ? field : null;
}
