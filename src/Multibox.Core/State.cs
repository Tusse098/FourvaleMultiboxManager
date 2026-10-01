namespace Multibox.Core;

/// <summary>Identifies an account slot. All state is keyed by SlotId end to end (CLAUDE.md).</summary>
public readonly record struct SlotId(int Number)
{
    public override string ToString() => $"slot{Number}";
}

/// <summary>
/// A game value with the time it was last <b>confirmed</b> from a live source and the name of that source.
/// Whether it may still be shown as current is decided by <see cref="FreshnessPolicy"/>.
/// </summary>
public sealed record Field<T>(T Value, DateTimeOffset ConfirmedAt, string Source);

/// <summary>Adapter status per slot (spec §7.2).</summary>
public enum AdapterHealth
{
    /// <summary>No game data yet (loading, logging in, between rooms).</summary>
    Waiting,
    Ok,

    /// <summary>Data is arriving but some of it could not be decoded.</summary>
    Degraded,

    /// <summary>Data is arriving and none of it can be decoded (e.g. the game changed its format).</summary>
    Broken,
}

/// <summary>State of a slot's game view (spec §6.1).</summary>
public enum BrowserState
{
    Loading,
    Ready,
    Crashed,
    Navigating,
}

/// <summary>State of a slot's connection to the game server (spec §6.1).</summary>
public enum ConnectionState
{
    Unknown,
    Connected,

    /// <summary>The game is rejoining a room after the server closed the previous one.</summary>
    Reconnecting,
    Disconnected,
}

/// <summary>Character fields confirmed in Phase 1 (docs/discovery.md matrix, High confidence). Null = never seen.</summary>
public sealed class CharacterState
{
    public Field<string>? Name { get; set; }
    public Field<string>? Class { get; set; }
    public Field<int>? Level { get; set; }
    public Field<int>? Hp { get; set; }
    public Field<int>? MaxHp { get; set; }
    public Field<int>? Sp { get; set; }
    public Field<int>? MaxSp { get; set; }

    /// <summary>0..1; 1 means the character can act now.</summary>
    public Field<double>? ActionMeter { get; set; }

    /// <summary>Time for the action meter to fill from 0 to 1, in milliseconds (discovery D4: attackRateMs).</summary>
    public Field<double>? ActionIntervalMs { get; set; }
    public Field<bool>? InBattle { get; set; }
    public Field<string>? Location { get; set; }

    /// <summary>Opponents still alive in the current battle (battle room state, discovery D1/D3).</summary>
    public Field<int>? EnemiesAlive { get; set; }

    /// <summary>Remaining HP of all opponents in the current battle as a share of their total (0..1).</summary>
    public Field<double>? EnemyHpFraction { get; set; }
}

/// <summary>
/// Battle results for the character in a slot since the app started (from <c>battleOver</c>, discovery C3).
/// Kept across page reloads; started again when a different character appears in the slot.
/// </summary>
public sealed class BattleStats
{
    public int Won { get; set; }
    public int Lost { get; set; }
    public long XpGained { get; set; }
    public long SilverGained { get; set; }
    public DateTimeOffset? LastBattleAt { get; set; }
}

public sealed class SlotState(SlotId id)
{
    public SlotId Id { get; } = id;
    public CharacterState Character { get; } = new();
    public BrowserState Browser { get; set; } = BrowserState.Loading;
    public ConnectionState Connection { get; set; } = ConnectionState.Unknown;

    /// <summary>Room sockets closed by the server or network (not by the game leaving a room). Kept across page reloads.</summary>
    public int UnexpectedDisconnects { get; set; }

    /// <summary>Battle totals this session. Kept across page reloads (see <see cref="StateStore.Reset"/>).</summary>
    public BattleStats Battles { get; set; } = new();

    public AdapterHealth Health { get; set; } = AdapterHealth.Waiting;
    public int DecodeErrors { get; set; }
    public double UpdatesPerSecond { get; set; }
    public DateTimeOffset? LastUpdate { get; set; }
}

/// <summary>
/// Holds every slot's state. Used from the UI thread only; adapters write through <see cref="Update"/>,
/// view models read through <see cref="Get"/> and <see cref="Changed"/>.
/// </summary>
public sealed class StateStore
{
    private readonly Dictionary<SlotId, SlotState> _slots = [];

    public event Action<SlotId>? Changed;

    public IReadOnlyCollection<SlotId> Slots => _slots.Keys;

    public SlotState Get(SlotId id)
    {
        if (!_slots.TryGetValue(id, out var state))
        {
            state = new SlotState(id);
            _slots[id] = state;
        }

        return state;
    }

    public void Update(SlotId id, Action<SlotState> change)
    {
        change(Get(id));
        Changed?.Invoke(id);
    }

    /// <summary>
    /// Forgets a slot's game data, e.g. when its page reloads. Session counters (disconnects) and the
    /// browser state survive, so a reload never hides earlier problems.
    /// </summary>
    public void Reset(SlotId id)
    {
        var old = Get(id);
        _slots[id] = new SlotState(id)
        {
            Browser = old.Browser,
            UnexpectedDisconnects = old.UnexpectedDisconnects,
            Battles = old.Battles,
        };
        Changed?.Invoke(id);
    }

    public void Remove(SlotId id)
    {
        if (_slots.Remove(id))
        {
            Changed?.Invoke(id);
        }
    }
}
