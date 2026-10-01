using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Multibox.Core;

namespace Multibox.App;

public sealed class FieldRow(string label) : Observable
{
    private string _value = LiveStateViewModel.Unknown;
    private string _age = "";
    private bool _isUnknown = true;

    public string Label { get; } = label;
    public string Value { get => _value; set => Set(ref _value, value); }
    public string Age { get => _age; set => Set(ref _age, value); }
    public bool IsUnknown { get => _isUnknown; set => Set(ref _isUnknown, value); }
}

/// <summary>
/// One slot as shown in its panel header and in the Live state window. Reads the state store only.
/// A field that is missing or older than its source's freshness limit shows UNKNOWN.
/// </summary>
public sealed class SlotCardViewModel : Observable
{
    private readonly FreshnessPolicy _freshness;
    private readonly FieldRow _class = new("Class"), _level = new("Level"), _hp = new("HP"), _sp = new("SP"),
        _meter = new("Action"), _inBattle = new("In battle"), _location = new("Location");

    private string _character = LiveStateViewModel.Unknown;
    private string _status = "";
    private string _connection = "";
    private string _health = "";
    private string _details = "";
    private bool _isReady;
    private bool _isProblem;
    private bool _isFocused;
    private string _classLevel = LiveStateViewModel.Unknown;
    private string _hpText = "", _spText = "", _battleText = "", _statsText = "";
    private double _hpFraction, _spFraction, _meterFraction;
    private bool _hasHpBar, _hasSpBar, _hasMeter, _isInBattle;
    private string _timerText = "";

    public SlotCardViewModel(SlotSession session, FreshnessPolicy freshness, ICommand reload, ICommand close)
    {
        Session = session;
        _freshness = freshness;
        Reload = reload;
        Close = close;
        Rows = [_class, _level, _hp, _sp, _meter, _inBattle, _location];
    }

    public SlotSession Session { get; }
    public ICommand Reload { get; }
    public ICommand Close { get; }
    public string Title => $"Slot {Session.Id.Number}";
    public ObservableCollection<FieldRow> Rows { get; }

    public string Character { get => _character; private set => Set(ref _character, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string Connection { get => _connection; private set => Set(ref _connection, value); }
    public string Health { get => _health; private set => Set(ref _health, value); }
    public string Details { get => _details; private set => Set(ref _details, value); }

    /// <summary>Action meter fresh and full: the character can act now (discovery D4).</summary>
    public bool IsReady { get => _isReady; private set => Set(ref _isReady, value); }

    /// <summary>The slot that receives keyboard input; drawn with a thick border.</summary>
    public bool IsFocused { get => _isFocused; set => Set(ref _isFocused, value); }

    // ----- Overlay values (read from the store; UNKNOWN or empty when not current) -----

    public string ClassLevel { get => _classLevel; private set => Set(ref _classLevel, value); }
    public string HpText { get => _hpText; private set => Set(ref _hpText, value); }
    public string SpText { get => _spText; private set => Set(ref _spText, value); }
    public double HpFraction { get => _hpFraction; private set => Set(ref _hpFraction, value); }
    public double SpFraction { get => _spFraction; private set => Set(ref _spFraction, value); }
    public double MeterFraction { get => _meterFraction; private set => Set(ref _meterFraction, value); }
    public bool HasHpBar { get => _hasHpBar; private set => Set(ref _hasHpBar, value); }
    public bool HasSpBar { get => _hasSpBar; private set => Set(ref _hasSpBar, value); }
    public bool HasMeter { get => _hasMeter; private set => Set(ref _hasMeter, value); }
    public bool IsInBattle { get => _isInBattle; private set => Set(ref _isInBattle, value); }

    /// <summary>Seconds until the character can act (action meter), or READY; empty outside battle.</summary>
    public string TimerText { get => _timerText; private set => Set(ref _timerText, value); }

    /// <summary>In battle: opponents left and their remaining HP; otherwise the location.</summary>
    public string BattleText { get => _battleText; private set => Set(ref _battleText, value); }

    /// <summary>Battle totals for this character since the app started.</summary>
    public string StatsText { get => _statsText; private set => Set(ref _statsText, value); }

    /// <summary>Crashed, disconnected or decoder trouble: drawn in red.</summary>
    public bool IsProblem { get => _isProblem; private set => Set(ref _isProblem, value); }

    public void Refresh(SlotState state, DateTimeOffset now)
    {
        var c = state.Character;
        Character = _freshness.Current(c.Name, now)?.Value ?? LiveStateViewModel.Unknown;
        Status = Session.Status;
        Connection = state.Connection.ToString();
        Health = state.Health.ToString();
        Details = string.Create(CultureInfo.CurrentCulture,
            $"{state.UpdatesPerSecond:0.0} upd/s · {state.UnexpectedDisconnects} disconnects · {Session.Recoveries} recoveries · {state.DecodeErrors} errors");
        IsProblem = state.Browser == BrowserState.Crashed
                    || state.Connection is ConnectionState.Disconnected or ConnectionState.Reconnecting
                    || state.Health is AdapterHealth.Degraded or AdapterHealth.Broken;

        Show(_class, c.Class, now, Pretty);
        Show(_level, c.Level, now, v => v.ToString("N0", CultureInfo.CurrentCulture));
        ShowPair(_hp, c.Hp, c.MaxHp, now);
        ShowPair(_sp, c.Sp, c.MaxSp, now);
        Show(_meter, c.ActionMeter, now, v => v >= 1 ? "READY" : v.ToString("P0", CultureInfo.CurrentCulture));
        Show(_inBattle, c.InBattle, now, v => v ? "Yes" : "No");
        Show(_location, c.Location, now, v => v);

        IsReady = _freshness.Current(c.ActionMeter, now) is { Value: >= 1 };
        RefreshOverlay(state, now);
    }

    private void RefreshOverlay(SlotState state, DateTimeOffset now)
    {
        var c = state.Character;
        var culture = CultureInfo.CurrentCulture;
        var klass = _freshness.Current(c.Class, now);
        var level = _freshness.Current(c.Level, now);
        ClassLevel = klass is null && level is null
            ? LiveStateViewModel.Unknown
            : string.Join(" · ", new[] { klass is null ? null : Pretty(klass.Value), level is null ? null : $"Lv {level.Value.ToString("N0", culture)}" }.Where(p => p is not null));

        (HpText, HpFraction, HasHpBar) = Bar(c.Hp, c.MaxHp, now);
        (SpText, SpFraction, HasSpBar) = Bar(c.Sp, c.MaxSp, now);

        var meter = _freshness.Current(c.ActionMeter, now);
        HasMeter = meter is not null;
        MeterFraction = meter?.Value ?? 0;
        var secondsLeft = SlotNavigator.SecondsUntilReady(state, _freshness, now); // same rule as Space (next to act)
        TimerText = meter is null
            ? ""
            : meter.Value >= 1
                ? "READY"
                : secondsLeft is { } left
                    ? string.Create(culture, $"{left:0.0} s")
                    : meter.Value.ToString("P0", culture);

        IsInBattle = _freshness.Current(c.InBattle, now) is { Value: true };
        var enemies = _freshness.Current(c.EnemiesAlive, now);
        var enemyHp = _freshness.Current(c.EnemyHpFraction, now);
        var location = _freshness.Current(c.Location, now);
        BattleText = IsInBattle
            ? enemies is null
                ? "In battle"
                : string.Create(culture, $"In battle · {enemies.Value} {(enemies.Value == 1 ? "enemy" : "enemies")} left{(enemyHp is null ? "" : $" · {enemyHp.Value:P0} HP")}")
            : location?.Value ?? "";

        var b = state.Battles;
        StatsText = b.Won + b.Lost == 0
            ? "No battles yet"
            : string.Create(culture, $"{b.Won} won · {b.Lost} lost · +{b.XpGained:N0} XP · +{b.SilverGained:N0} silver");
    }

    /// <summary>"value / max" with a bar when both are current; just the value (no bar) e.g. for hpSync outside battle.</summary>
    private (string Text, double Fraction, bool HasBar) Bar(Field<int>? value, Field<int>? max, DateTimeOffset now)
    {
        var v = _freshness.Current(value, now);
        var m = _freshness.Current(max, now);
        if (v is null)
        {
            return (LiveStateViewModel.Unknown, 0, false);
        }

        return m is { Value: > 0 }
            ? (string.Create(CultureInfo.CurrentCulture, $"{v.Value:N0} / {m.Value:N0}"), Math.Clamp(v.Value / (double)m.Value, 0, 1), true)
            : (v.Value.ToString("N0", CultureInfo.CurrentCulture), 0, false);
    }

    private void Show<T>(FieldRow row, Field<T>? field, DateTimeOffset now, Func<T, string> format)
    {
        var current = _freshness.Current(field, now);
        row.IsUnknown = current is null;
        row.Value = current is null ? LiveStateViewModel.Unknown : format(current.Value);
        row.Age = field is null ? "" : LiveStateViewModel.Age(field.ConfirmedAt, now) + " · " + field.Source;
    }

    /// <summary>"value / max" when both are current, otherwise just the value (e.g. HP from hpSync outside battle).</summary>
    private void ShowPair(FieldRow row, Field<int>? value, Field<int>? max, DateTimeOffset now)
    {
        var currentMax = _freshness.Current(max, now);
        Show(row, value, now, v => currentMax is null
            ? v.ToString("N0", CultureInfo.CurrentCulture)
            : string.Create(CultureInfo.CurrentCulture, $"{v:N0} / {currentMax.Value:N0}"));
    }

    private static string Pretty(string id) =>
        string.Join(' ', id.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
}

/// <summary>Live state window: one card per slot plus soak-test diagnostics (spec §15 criteria 2, 5, 7, 8).</summary>
public sealed class LiveStateViewModel : Observable
{
    public const string Unknown = "UNKNOWN";

    private string _summary = "";
    private string _notice = "";
    private string _focusText = "";
    private string _shortcutHelp = "";
    private PanelLayout _layout;
    private string _browser = "Browser: measuring…";
    private string _soakFile = "";
    private bool _hasViolations;

    public ObservableCollection<SlotCardViewModel> Cards { get; } = [];

    /// <summary>Top-bar slot picker: open a slot by clicking its number.</summary>
    public ObservableCollection<SlotPillViewModel> SlotPills { get; } = [];

    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    /// <summary>Short-lived top-bar message, e.g. "No character is in battle".</summary>
    public string Notice { get => _notice; set => Set(ref _notice, value); }
    public string FocusText { get => _focusText; set => Set(ref _focusText, value); }
    public string ShortcutHelp { get => _shortcutHelp; set => Set(ref _shortcutHelp, value); }

    public PanelLayout Layout
    {
        get => _layout;
        set
        {
            if (Set(ref _layout, value))
            {
                OnPropertyChanged(nameof(IsGridLayout));
                OnPropertyChanged(nameof(IsFocusLayout));
            }
        }
    }

    public bool IsGridLayout => Layout == PanelLayout.Grid;
    public bool IsFocusLayout => Layout == PanelLayout.Focus;
    public ICommand? SetGridLayout { get; set; }
    public ICommand? SetFocusLayout { get; set; }
    public string Browser { get => _browser; private set => Set(ref _browser, value); }
    public string SoakFile { get => _soakFile; set => Set(ref _soakFile, value); }
    public bool HasViolations { get => _hasViolations; private set => Set(ref _hasViolations, value); }

    public void RefreshDiagnostics(DateTimeOffset startedAt, DateTimeOffset now, int violations, Multibox.Hosting.BrowserProcessMetrics.Snapshot? metrics)
    {
        var uptime = now - startedAt;
        Summary = string.Create(CultureInfo.CurrentCulture,
            $"Up {(int)uptime.TotalHours}:{uptime.Minutes:00}:{uptime.Seconds:00} · {Cards.Count} slots · isolation violations: {violations}");
        HasViolations = violations > 0;
        if (metrics is not null)
        {
            Browser = string.Create(CultureInfo.CurrentCulture,
                $"Browser tree (all slots): {metrics.Processes} processes · private {metrics.PrivateBytes / (1024.0 * 1024):0} MB (working set {metrics.WorkingSetBytes / (1024.0 * 1024):0} MB) · CPU {metrics.CpuPercent:0.0}%");
        }
    }

    public static string Age(DateTimeOffset at, DateTimeOffset now)
    {
        var age = now - at;
        return age.TotalSeconds < 60
            ? string.Create(CultureInfo.CurrentCulture, $"{Math.Max(0, age.TotalSeconds):0.0} s")
            : string.Create(CultureInfo.CurrentCulture, $"{age.TotalMinutes:0} min");
    }
}
