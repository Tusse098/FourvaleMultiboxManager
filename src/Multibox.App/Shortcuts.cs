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
/// Shortcut settings: the defaults come from multibox.json, the player's own choices from Settings (saved in
/// app-settings.json). <c>focusSlot</c> contains <c>{n}</c>, replaced by each slot number; <c>focusSlots</c>, when set,
/// gives each slot its own key instead (index 0 = slot 1). <c>"Alt"</c> alone means tapping Alt (press and release with
/// no other key). An empty string disables a shortcut.
/// </summary>
public sealed record ShortcutConfig
{
    public string FocusSlot { get; init; } = "{n}";
    public List<string>? FocusSlots { get; init; }
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
    private readonly HashSet<KeyChord> _numPadCopies = [];

    public IReadOnlyDictionary<KeyChord, (ShortcutAction Action, int Slot)> Bindings => _map;

    public static ShortcutMap Create(ShortcutConfig config, int slotCount)
    {
        var map = new ShortcutMap();
        var slotChords = new List<(KeyChord Chord, int Slot)>();
        for (var n = 1; n <= slotCount; n++)
        {
            var text = FocusSlotText(config, n);
            if (!string.IsNullOrWhiteSpace(text))
            {
                var chord = Parse(text);
                map.Add(chord, ShortcutAction.FocusSlot, n);
                slotChords.Add((chord, n));
            }
        }

        map.AddOptional(config.NextSlot, ShortcutAction.NextSlot);
        map.AddOptional(config.PreviousSlot, ShortcutAction.PreviousSlot);
        map.AddOptional(config.ToggleFullscreen, ShortcutAction.ToggleFullscreen);

        // The numeric keypad digit does the same as a top-row digit, unless that keypad key has its own binding.
        foreach (var (chord, slot) in slotChords.Where(c => c.Chord.Key is >= Key.D1 and <= Key.D9))
        {
            var copy = chord with { Key = Key.NumPad0 + (chord.Key - Key.D0) };
            if (map._map.TryAdd(copy, (ShortcutAction.FocusSlot, slot)))
            {
                map._numPadCopies.Add(copy);
            }
        }

        return map;
    }

    /// <summary>The configured text for focusing slot <paramref name="slot"/> (1-based): its own entry, else the pattern.</summary>
    public static string FocusSlotText(ShortcutConfig config, int slot) =>
        config.FocusSlots is { } list
            ? (slot <= list.Count ? list[slot - 1] : "")
            : string.IsNullOrWhiteSpace(config.FocusSlot) ? "" : config.FocusSlot.Replace("{n}", slot.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    /// <summary>The key bound to focusing <paramref name="slot"/>, without the automatic keypad copy; empty when unbound.</summary>
    public string DescribeSlot(int slot) =>
        _map.Where(p => p.Value == (ShortcutAction.FocusSlot, slot) && !_numPadCopies.Contains(p.Key))
            .Select(p => p.Key.ToString())
            .FirstOrDefault() ?? "";

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
        _map.Where(p => p.Value.Action == action && !_numPadCopies.Contains(p.Key))
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
            throw new InvalidDataException($"{chord} is used for two shortcuts.");
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
                        throw new InvalidDataException($"Shortcut '{text}' has more than one key.");
                    }

                    key = raw.Length == 1 && char.IsDigit(raw[0])
                        ? Key.D0 + (raw[0] - '0')
                        : Enum.TryParse<Key>(raw, ignoreCase: true, out var parsed) && parsed != Key.None ? parsed
                        : throw new InvalidDataException($"Unknown key '{raw}' in shortcut '{text}'.");
                    break;
            }
        }

        if (key is null)
        {
            return modifiers == ModifierKeys.Alt
                ? KeyChord.AltTap
                : throw new InvalidDataException($"Shortcut '{text}' has no key; only Alt may be used on its own.");
        }

        if (key is >= Key.F1 and <= Key.F24)
        {
            throw new InvalidDataException($"F-keys can't be used: the game's browser uses them (discovery R3).");
        }

        if (modifiers is ModifierKeys.None or ModifierKeys.Shift && GameKeys.Contains(key.Value))
        {
            throw new InvalidDataException($"{new KeyChord(modifiers, key.Value)} is a key the game uses; add Ctrl or Alt.");
        }

        return new KeyChord(modifiers, key.Value);
    }
}
