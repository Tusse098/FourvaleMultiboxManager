using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;

namespace Multibox.App;

/// <summary>One shortcut the player can change: its action, and for <see cref="ShortcutAction.FocusSlot"/> the slot.</summary>
public readonly record struct ShortcutTarget(ShortcutAction Action, int Slot = 0)
{
    public string Label => Action switch
    {
        ShortcutAction.FocusSlot => $"Focus slot {Slot}",
        ShortcutAction.NextSlot => "Next slot",
        ShortcutAction.PreviousSlot => "Previous slot",
        ShortcutAction.ToggleFullscreen => "Fullscreen",
        _ => Action.ToString(),
    };
}

/// <summary>
/// Changes one shortcut in a <see cref="ShortcutConfig"/>. Pure functions, so the rules are testable.
/// A key that another shortcut already uses is swapped: that shortcut gets this one's old key.
/// </summary>
public static class ShortcutEditor
{
    public static IReadOnlyList<ShortcutTarget> Targets(int slotCount) =>
    [
        .. Enumerable.Range(1, slotCount).Select(n => new ShortcutTarget(ShortcutAction.FocusSlot, n)),
        new(ShortcutAction.NextSlot),
        new(ShortcutAction.PreviousSlot),
        new(ShortcutAction.ToggleFullscreen),
    ];

    /// <summary>The key text for <paramref name="target"/>; empty when it has no key.</summary>
    public static string Get(ShortcutConfig config, ShortcutTarget target) => target.Action switch
    {
        ShortcutAction.FocusSlot => ShortcutMap.FocusSlotText(config, target.Slot),
        ShortcutAction.NextSlot => config.NextSlot,
        ShortcutAction.PreviousSlot => config.PreviousSlot,
        ShortcutAction.ToggleFullscreen => config.ToggleFullscreen,
        _ => "",
    };

    /// <summary>
    /// Gives <paramref name="target"/> the key <paramref name="chord"/>, or no key when null.
    /// Returns the new config and a line for the player, or no config and the reason it was refused.
    /// </summary>
    public static (ShortcutConfig? Config, string Message) Assign(ShortcutConfig config, int slotCount, ShortcutTarget target, KeyChord? chord)
    {
        var text = chord?.ToString() ?? "";
        if (chord is not null)
        {
            try
            {
                _ = ShortcutMap.Parse(text); // F-keys and the game's own keys are refused here
            }
            catch (InvalidDataException ex)
            {
                return (null, ex.Message);
            }
        }

        var old = Get(config, target);
        var updated = Set(config, slotCount, target, text);
        var message = $"{target.Label}: {Show(text)}";
        var other = chord is { } key
            ? Targets(slotCount).Where(t => t != target && SameKey(Get(config, t), key)).Cast<ShortcutTarget?>().FirstOrDefault()
            : null;
        if (other is { } taken)
        {
            updated = Set(updated, slotCount, taken, old);
            message += $". {taken.Label} now has {Show(old)}.";
        }

        try
        {
            _ = ShortcutMap.Create(updated, slotCount);
        }
        catch (InvalidDataException ex)
        {
            return (null, ex.Message);
        }

        return (updated, message);
    }

    /// <summary>How a key text is shown to the player.</summary>
    public static string Show(string text) => string.IsNullOrWhiteSpace(text) ? "no key" : text;

    private static ShortcutConfig Set(ShortcutConfig config, int slotCount, ShortcutTarget target, string text)
    {
        switch (target.Action)
        {
            case ShortcutAction.FocusSlot:
                // From here on each slot has its own entry, so changing one never moves the others.
                var keys = Enumerable.Range(1, slotCount).Select(n => ShortcutMap.FocusSlotText(config, n)).ToList();
                keys[target.Slot - 1] = text;
                return config with { FocusSlots = keys };
            case ShortcutAction.NextSlot:
                return config with { NextSlot = text };
            case ShortcutAction.PreviousSlot:
                return config with { PreviousSlot = text };
            case ShortcutAction.ToggleFullscreen:
                return config with { ToggleFullscreen = text };
            default:
                return config;
        }
    }

    private static bool SameKey(string text, KeyChord chord)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            return ShortcutMap.Parse(text) == chord;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }
}

/// <summary>
/// Turns key presses in the Settings window into a new shortcut. Only sees keys typed into the app's own Settings
/// window (WPF key events); there is no keyboard hook (ADR 0009). Pure logic, unit-tested.
/// </summary>
public sealed class ShortcutCapture
{
    private bool _altDown;
    private bool _altUsedWithOtherKey;

    public enum Kind
    {
        /// <summary>Keep waiting (a modifier is held, or nothing complete yet).</summary>
        Waiting,
        Chord,
        Cancel,
        Clear,
    }

    public readonly record struct Result(Kind Kind, KeyChord? Chord = null);

    public Result OnKeyDown(Key key, ModifierKeys modifiers)
    {
        if (key is Key.LeftAlt or Key.RightAlt)
        {
            _altDown = true;
            return new Result(Kind.Waiting);
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None)
        {
            return new Result(Kind.Waiting);
        }

        _altUsedWithOtherKey |= _altDown;
        modifiers &= ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift;
        if (modifiers == ModifierKeys.None)
        {
            switch (key)
            {
                case Key.Escape:
                    return new Result(Kind.Cancel);
                case Key.Back or Key.Delete:
                    return new Result(Kind.Clear);
            }
        }

        return new Result(Kind.Chord, new KeyChord(modifiers, key));
    }

    /// <summary>Releasing Alt without having pressed another key with it means "tap Alt".</summary>
    public Result OnKeyUp(Key key)
    {
        if (key is not (Key.LeftAlt or Key.RightAlt))
        {
            return new Result(Kind.Waiting);
        }

        var tap = _altDown && !_altUsedWithOtherKey;
        Reset();
        return tap ? new Result(Kind.Chord, KeyChord.AltTap) : new Result(Kind.Waiting);
    }

    public void Reset()
    {
        _altDown = false;
        _altUsedWithOtherKey = false;
    }
}

/// <summary>One row in the Settings shortcut list.</summary>
public sealed class ShortcutRowViewModel : Observable
{
    private string _keys = "";
    private bool _isCapturing;

    public ShortcutRowViewModel(ShortcutTarget target, Action<ShortcutRowViewModel> edit)
    {
        Target = target;
        Edit = new RelayCommand(() => edit(this));
    }

    public ShortcutTarget Target { get; }
    public string Label => Target.Label;

    /// <summary>Starts (or, while waiting for a key, stops) changing this shortcut.</summary>
    public RelayCommand Edit { get; }

    public string Keys
    {
        get => _keys;
        set { if (Set(ref _keys, value)) OnPropertyChanged(nameof(ButtonText)); }
    }

    public bool IsCapturing
    {
        get => _isCapturing;
        set { if (Set(ref _isCapturing, value)) OnPropertyChanged(nameof(ButtonText)); }
    }

    public string ButtonText => IsCapturing ? "Press a key…" : ShortcutEditor.Show(Keys);
}

/// <summary>
/// The Settings window's shortcut editor. Changes apply at once and are saved per player
/// (app-settings.json); <see cref="ResetToDefaults"/> goes back to multibox.json's shortcuts.
/// </summary>
public sealed class ShortcutsViewModel : Observable
{
    private readonly int _slotCount;
    private readonly ShortcutConfig _defaults;
    private readonly Action<ShortcutConfig?> _apply;
    private readonly ShortcutCapture _capture = new();
    private ShortcutConfig _current;
    private ShortcutRowViewModel? _capturing;
    private string _message = "";
    private bool _isError;

    /// <param name="apply">Called with the new shortcuts, or null to go back to the defaults.</param>
    public ShortcutsViewModel(int slotCount, ShortcutConfig defaults, Action<ShortcutConfig?> apply)
    {
        _slotCount = slotCount;
        _defaults = defaults;
        _current = defaults;
        _apply = apply;
        foreach (var target in ShortcutEditor.Targets(slotCount))
        {
            Rows.Add(new ShortcutRowViewModel(target, StartCapture));
        }

        ResetToDefaults = new RelayCommand(Reset);
        Refresh();
    }

    public ObservableCollection<ShortcutRowViewModel> Rows { get; } = [];
    public RelayCommand ResetToDefaults { get; }
    public bool IsCapturing => _capturing is not null;

    public string Message { get => _message; private set => Set(ref _message, value); }
    public bool IsError { get => _isError; private set => Set(ref _isError, value); }

    /// <summary>The shortcuts in use (e.g. the player's saved ones at start).</summary>
    public void Load(ShortcutConfig current)
    {
        _current = current;
        Refresh();
    }

    public void StartCapture(ShortcutRowViewModel row)
    {
        var same = _capturing == row;
        CancelCapture();
        if (!same)
        {
            _capturing = row;
            row.IsCapturing = true;
            OnPropertyChanged(nameof(IsCapturing));
            Show("Press the new key. Esc cancels, Backspace removes the key.", error: false);
        }
    }

    public void CancelCapture()
    {
        if (_capturing is { } row)
        {
            row.IsCapturing = false;
            _capturing = null;
            _capture.Reset();
            OnPropertyChanged(nameof(IsCapturing));
            Show("", error: false);
        }
    }

    /// <summary>A key went down in the Settings window. True when it was taken for the shortcut being changed.</summary>
    public bool OnKeyDown(Key key, ModifierKeys modifiers) => _capturing is not null && Handle(_capture.OnKeyDown(key, modifiers));

    public bool OnKeyUp(Key key) => _capturing is not null && Handle(_capture.OnKeyUp(key));

    private bool Handle(ShortcutCapture.Result result)
    {
        if (_capturing is not { } row)
        {
            return false;
        }

        switch (result.Kind)
        {
            case ShortcutCapture.Kind.Cancel:
                CancelCapture();
                return true;
            case ShortcutCapture.Kind.Clear:
            case ShortcutCapture.Kind.Chord:
                var chord = result.Kind == ShortcutCapture.Kind.Chord ? result.Chord : null;
                var (config, message) = ShortcutEditor.Assign(_current, _slotCount, row.Target, chord);
                CancelCapture();
                if (config is null)
                {
                    Show(message, error: true);
                    return true;
                }

                _current = config;
                Refresh();
                _apply(config);
                Show(message, error: false);
                return true;
            default:
                return true; // a modifier on its own: keep waiting, and keep Alt from reaching the window
        }
    }

    private void Reset()
    {
        CancelCapture();
        _current = _defaults;
        Refresh();
        _apply(null);
        Show("Shortcuts reset to the defaults.", error: false);
    }

    private void Refresh()
    {
        foreach (var row in Rows)
        {
            row.Keys = ShortcutEditor.Get(_current, row.Target);
        }
    }

    private void Show(string message, bool error)
    {
        Message = message;
        IsError = error;
    }
}
