using System.Globalization;
using System.IO;
using System.Windows.Input;

namespace Multibox.App;

public enum ShortcutAction
{
    FocusSlot,
    NextSlot,
    PreviousSlot,
    ToggleFullscreen,
}

/// <summary>
/// Shortcut settings from multibox.json. <c>focusSlot</c> contains <c>{n}</c>, replaced by each slot number.
/// <c>"Alt"</c> alone means tapping Alt (press and release with no other key). An empty string disables a shortcut.
/// </summary>
public sealed class ShortcutConfig
{
    public string FocusSlot { get; init; } = "{n}";
    public string NextSlot { get; init; } = "Alt";
    public string PreviousSlot { get; init; } = "";
    public string ToggleFullscreen { get; init; } = "Alt+Enter";
}

public readonly record struct KeyChord(ModifierKeys Modifiers, Key Key)
{
    /// <summary>Tapping Alt on its own.</summary>
    public static KeyChord AltTap { get; } = new(ModifierKeys.Alt, Key.None);

    public bool IsAltTap => this == AltTap;
    public bool IsPlainKey => Modifiers == ModifierKeys.None;

    public override string ToString()
    {
        if (IsAltTap)
        {
            return "Alt";
        }

        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        parts.Add(Key switch
        {
            >= Key.D0 and <= Key.D9 => ((int)(Key - Key.D0)).ToString(CultureInfo.InvariantCulture),
            Key.Enter => "Enter", // Key.Enter and Key.Return share a value; .NET would print "Return"
            _ => Key.ToString(),
        });
        return string.Join('+', parts);
    }
}

/// <summary>
/// App shortcuts (spec §9), validated so they cannot take the game's own keys:
/// <list type="bullet">
/// <item>keys the game uses (discovery Q10: Q, I, C, M, Enter, Esc, Tab, ←, →) are rejected without a Ctrl/Alt modifier;</item>
/// <item>F-keys are rejected (discovery R3: they belong to the browser layer);</item>
/// <item>duplicates are rejected.</item>
/// </list>
/// Plain-key shortcuts (e.g. <c>1</c>) are not applied while a text field in the game has focus,
/// so chat and login typing work (see <see cref="IsTypingSensitive"/>).
/// </summary>
public sealed class ShortcutMap
{
    /// <summary>Keys Fourvale uses itself (discovery Q10), never allowed as plain-key shortcuts.</summary>
    public static IReadOnlySet<Key> GameKeys { get; } = new HashSet<Key> { Key.Q, Key.I, Key.C, Key.M, Key.Enter, Key.Escape, Key.Tab, Key.Left, Key.Right };

    private readonly Dictionary<KeyChord, (ShortcutAction Action, int Slot)> _map = [];

    public IReadOnlyDictionary<KeyChord, (ShortcutAction Action, int Slot)> Bindings => _map;

    public static ShortcutMap Create(ShortcutConfig config, int slotCount)
    {
        var map = new ShortcutMap();
        if (!string.IsNullOrWhiteSpace(config.FocusSlot))
        {
            for (var n = 1; n <= slotCount; n++)
            {
                var chord = Parse(config.FocusSlot.Replace("{n}", n.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
                map.Add(chord, ShortcutAction.FocusSlot, n);

                // The numeric keypad digit does the same as the top-row digit.
                if (chord.Key is >= Key.D1 and <= Key.D9)
                {
                    map.Add(chord with { Key = Key.NumPad0 + (chord.Key - Key.D0) }, ShortcutAction.FocusSlot, n);
                }
            }
        }

        map.AddOptional(config.NextSlot, ShortcutAction.NextSlot);
        map.AddOptional(config.PreviousSlot, ShortcutAction.PreviousSlot);
        map.AddOptional(config.ToggleFullscreen, ShortcutAction.ToggleFullscreen);
        return map;
    }

    /// <summary>The bound action for a key press (not an Alt tap), or null to let the key through to the game.</summary>
    public (ShortcutAction Action, int Slot)? Match(ModifierKeys modifiers, Key key) =>
        key == Key.None
            ? null
            : _map.TryGetValue(new KeyChord(modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift), key), out var binding)
                ? binding
                : null;

    /// <summary>The action bound to tapping Alt alone, if any.</summary>
    public (ShortcutAction Action, int Slot)? MatchAltTap() => _map.TryGetValue(KeyChord.AltTap, out var binding) ? binding : null;

    /// <summary>Plain keys are typed text when a game text field has focus, so they must pass through then.</summary>
    public static bool IsTypingSensitive(ModifierKeys modifiers) =>
        (modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == ModifierKeys.None;

    public string Describe(ShortcutAction action) =>
        _map.Where(p => p.Value.Action == action && p.Key.Key is not (>= Key.NumPad0 and <= Key.NumPad9))
            .Select(p => p.Key.ToString())
            .FirstOrDefault() ?? "";

    private void AddOptional(string text, ShortcutAction action)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            Add(Parse(text), action, 0);
        }
    }

    private void Add(KeyChord chord, ShortcutAction action, int slot)
    {
        if (!_map.TryAdd(chord, (action, slot)))
        {
            throw new InvalidDataException($"multibox.json: shortcut {chord} is bound twice.");
        }
    }

    public static KeyChord Parse(string text)
    {
        var modifiers = ModifierKeys.None;
        Key? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= ModifierKeys.Control; break;
                case "alt": modifiers |= ModifierKeys.Alt; break;
                case "shift": modifiers |= ModifierKeys.Shift; break;
                default:
                    if (key is not null)
                    {
                        throw new InvalidDataException($"multibox.json: shortcut '{text}' has more than one key.");
                    }

                    key = raw.Length == 1 && char.IsDigit(raw[0])
                        ? Key.D0 + (raw[0] - '0')
                        : Enum.TryParse<Key>(raw, ignoreCase: true, out var parsed) && parsed != Key.None ? parsed
                        : throw new InvalidDataException($"multibox.json: unknown key '{raw}' in shortcut '{text}'.");
                    break;
            }
        }

        if (key is null)
        {
            return modifiers == ModifierKeys.Alt
                ? KeyChord.AltTap
                : throw new InvalidDataException($"multibox.json: shortcut '{text}' has no key (only \"Alt\" may stand alone).");
        }

        if (key is >= Key.F1 and <= Key.F24)
        {
            throw new InvalidDataException($"multibox.json: shortcut '{text}' uses an F-key; F-keys are not allowed (discovery R3).");
        }

        if (modifiers is ModifierKeys.None or ModifierKeys.Shift && GameKeys.Contains(key.Value))
        {
            throw new InvalidDataException($"multibox.json: shortcut '{text}' would take a key the game uses; add Ctrl or Alt.");
        }

        return new KeyChord(modifiers, key.Value);
    }
}
