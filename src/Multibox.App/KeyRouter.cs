using System.Windows.Input;

namespace Multibox.App;

/// <summary>
/// Decides, for each key event in the app window (WPF Preview key events), whether it is an app shortcut
/// (swallowed, never reaches the game) or a game key (passed through untouched). Pure logic, unit-tested.
/// <list type="bullet">
/// <item>One physical press triggers at most one action: auto-repeat is swallowed without acting.</item>
/// <item>Plain-key shortcuts are passed through while a game text field has focus (chat, login).</item>
/// <item>A lone Alt tap triggers its action on release; Alt is never swallowed, so Alt+Tab, Alt+F4 keep working.</item>
/// </list>
/// Only moves focus within the app; it never generates input for the game (spec §3.2 Tier A).
/// </summary>
public sealed class KeyRouter(ShortcutMap shortcuts)
{
    private readonly HashSet<Key> _swallowedDown = [];
    private bool _altDown;
    private bool _altUsedWithOtherKey;

    public readonly record struct Decision(bool Swallow, (ShortcutAction Action, int Slot)? Action, string? PassedBecause = null);

    private static readonly Decision PassThrough = new(false, null);

    /// <param name="key">The key (left/right modifier keys included as their own keys).</param>
    /// <param name="isDown">Key down (true) or up (false).</param>
    /// <param name="modifiers">Ctrl/Alt/Shift currently held.</param>
    /// <param name="typing">A text field in the focused game has focus.</param>
    /// <param name="isRepeat">Windows reports the key was already down (auto-repeat while held).</param>
    public Decision OnKey(Key key, bool isDown, ModifierKeys modifiers, bool typing, bool isRepeat = false)
    {
        if (key is Key.LeftAlt or Key.RightAlt)
        {
            return OnAlt(isDown);
        }

        if (isDown && _altDown)
        {
            _altUsedWithOtherKey = true; // Alt+something: not an Alt tap.
        }

        if (!isDown)
        {
            // Swallow the release only if we swallowed the press, so the game never sees half a key.
            return _swallowedDown.Remove(key) ? new Decision(true, null) : PassThrough;
        }

        if (shortcuts.Match(modifiers, key) is not { } binding)
        {
            return PassThrough;
        }

        if (typing && ShortcutMap.IsTypingSensitive(modifiers))
        {
            return new Decision(false, null, "typing"); // "1" or Space typed into chat or the login form.
        }

        // Auto-repeat comes from Windows' own flag, so a missed key release can never block a later, real press.
        _swallowedDown.Add(key);
        return isRepeat ? new Decision(true, null) : new Decision(true, binding);
    }

    /// <summary>Clears held-key tracking, e.g. when the app loses the foreground mid-press.</summary>
    public void Reset()
    {
        _swallowedDown.Clear();
        _altDown = false;
        _altUsedWithOtherKey = false;
    }

    private Decision OnAlt(bool isDown)
    {
        if (isDown)
        {
            if (!_altDown)
            {
                _altDown = true;
                _altUsedWithOtherKey = false;
            }

            return PassThrough; // Held Alt repeats; never swallowed.
        }

        var tap = _altDown && !_altUsedWithOtherKey;
        _altDown = false;
        return new Decision(false, tap ? shortcuts.MatchAltTap() : null);
    }
}
