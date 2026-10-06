using System.Text.Json;
using System.Windows.Input;
using Multibox.App;

namespace Multibox.App.Tests;

public class ShortcutEditorTests
{
    private static readonly ShortcutConfig Defaults = new();
    private static readonly ShortcutTarget Slot1 = new(ShortcutAction.FocusSlot, 1);
    private static readonly ShortcutTarget Slot2 = new(ShortcutAction.FocusSlot, 2);
    private static readonly ShortcutTarget Next = new(ShortcutAction.NextSlot);

    [Fact]
    public void Changing_one_slot_key_keeps_the_others()
    {
        var (config, _) = ShortcutEditor.Assign(Defaults, 5, Slot1, new KeyChord(ModifierKeys.None, Key.Z));

        Assert.NotNull(config);
        Assert.Equal(["Z", "2", "3", "4", "5"], config.FocusSlots);
        var map = ShortcutMap.Create(config, 5);
        Assert.Equal((ShortcutAction.FocusSlot, 1), map.Match(ModifierKeys.None, Key.Z));
        Assert.Null(map.Match(ModifierKeys.None, Key.D1)); // 1 no longer focuses slot 1
        Assert.Null(map.Match(ModifierKeys.None, Key.NumPad1)); // nor its keypad copy
        Assert.Equal((ShortcutAction.FocusSlot, 2), map.Match(ModifierKeys.None, Key.NumPad2));
    }

    [Fact]
    public void A_key_in_use_is_swapped()
    {
        var (config, message) = ShortcutEditor.Assign(Defaults, 5, Slot1, new KeyChord(ModifierKeys.None, Key.D2));

        Assert.NotNull(config);
        Assert.Equal("2", ShortcutEditor.Get(config, Slot1));
        Assert.Equal("1", ShortcutEditor.Get(config, Slot2));
        Assert.Contains("Focus slot 2 now has 1", message);
    }

    [Fact]
    public void Alt_tap_can_move_from_next_slot_to_a_slot()
    {
        var (config, _) = ShortcutEditor.Assign(Defaults, 5, Slot1, KeyChord.AltTap);

        Assert.NotNull(config);
        var map = ShortcutMap.Create(config, 5);
        Assert.Equal((ShortcutAction.FocusSlot, 1), map.MatchAltTap());
        Assert.Equal((ShortcutAction.NextSlot, 0), map.Match(ModifierKeys.None, Key.D1)); // swapped
    }

    [Theory]
    [InlineData(ModifierKeys.None, Key.Q)]     // the game's quest key
    [InlineData(ModifierKeys.None, Key.F2)]    // F-keys belong to the browser
    [InlineData(ModifierKeys.Shift, Key.Tab)]
    public void Refused_keys_leave_the_shortcuts_unchanged(ModifierKeys modifiers, Key key)
    {
        var (config, message) = ShortcutEditor.Assign(Defaults, 5, Next, new KeyChord(modifiers, key));

        Assert.Null(config);
        Assert.False(string.IsNullOrWhiteSpace(message));
    }

    [Fact]
    public void Game_keys_are_fine_with_ctrl()
    {
        var (config, _) = ShortcutEditor.Assign(Defaults, 5, Next, new KeyChord(ModifierKeys.Control, Key.Q));

        Assert.NotNull(config);
        Assert.Equal((ShortcutAction.NextSlot, 0), ShortcutMap.Create(config, 5).Match(ModifierKeys.Control, Key.Q));
    }

    [Fact]
    public void Clearing_removes_the_key()
    {
        var (config, message) = ShortcutEditor.Assign(Defaults, 5, Next, null);

        Assert.NotNull(config);
        Assert.Null(ShortcutMap.Create(config, 5).MatchAltTap());
        Assert.Equal("Next slot: no key", message);
    }

    [Fact]
    public void An_explicit_keypad_binding_wins_over_the_automatic_keypad_copy()
    {
        var (config, _) = ShortcutEditor.Assign(Defaults, 5, Next, new KeyChord(ModifierKeys.None, Key.NumPad1));

        Assert.NotNull(config);
        var map = ShortcutMap.Create(config, 5);
        Assert.Equal((ShortcutAction.NextSlot, 0), map.Match(ModifierKeys.None, Key.NumPad1));
        Assert.Equal((ShortcutAction.FocusSlot, 1), map.Match(ModifierKeys.None, Key.D1));
        Assert.Equal("1", map.DescribeSlot(1));
    }

    [Theory]
    [InlineData(ModifierKeys.None, Key.D7)]
    [InlineData(ModifierKeys.None, Key.NumPad4)]
    [InlineData(ModifierKeys.Control | ModifierKeys.Shift, Key.A)]
    [InlineData(ModifierKeys.Alt, Key.Enter)]
    [InlineData(ModifierKeys.None, Key.OemComma)]
    [InlineData(ModifierKeys.None, Key.Space)]
    public void Saved_key_text_reads_back_as_the_same_key(ModifierKeys modifiers, Key key)
    {
        var chord = new KeyChord(modifiers, key);

        Assert.Equal(chord, ShortcutMap.Parse(chord.ToString()));
    }

    [Fact]
    public void Custom_shortcuts_survive_the_settings_file()
    {
        var (config, _) = ShortcutEditor.Assign(Defaults, 5, Slot2, new KeyChord(ModifierKeys.Control, Key.X));
        var saved = new AppSettings([1, 2], PanelLayout.Grid, 1) { Shortcuts = config };

        var loaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(saved));

        Assert.NotNull(loaded?.Shortcuts);
        Assert.Equal((ShortcutAction.FocusSlot, 2), ShortcutMap.Create(loaded.Shortcuts, 5).Match(ModifierKeys.Control, Key.X));
    }
}

public class ShortcutCaptureTests
{
    [Fact]
    public void A_key_with_modifiers_is_captured_once_it_is_pressed()
    {
        var capture = new ShortcutCapture();

        Assert.Equal(ShortcutCapture.Kind.Waiting, capture.OnKeyDown(Key.LeftCtrl, ModifierKeys.Control).Kind);
        var result = capture.OnKeyDown(Key.D3, ModifierKeys.Control);

        Assert.Equal(new ShortcutCapture.Result(ShortcutCapture.Kind.Chord, new KeyChord(ModifierKeys.Control, Key.D3)), result);
    }

    [Fact]
    public void Tapping_alt_alone_is_captured_on_release()
    {
        var capture = new ShortcutCapture();

        Assert.Equal(ShortcutCapture.Kind.Waiting, capture.OnKeyDown(Key.LeftAlt, ModifierKeys.Alt).Kind);
        Assert.Equal(new ShortcutCapture.Result(ShortcutCapture.Kind.Chord, KeyChord.AltTap), capture.OnKeyUp(Key.LeftAlt));
    }

    [Fact]
    public void Alt_with_another_key_is_not_an_alt_tap()
    {
        var capture = new ShortcutCapture();
        capture.OnKeyDown(Key.LeftAlt, ModifierKeys.Alt);

        var chord = capture.OnKeyDown(Key.Z, ModifierKeys.Alt);

        Assert.Equal(new KeyChord(ModifierKeys.Alt, Key.Z), chord.Chord);
        Assert.Equal(ShortcutCapture.Kind.Waiting, capture.OnKeyUp(Key.LeftAlt).Kind);
    }

    [Fact]
    public void Escape_cancels_and_backspace_clears()
    {
        var capture = new ShortcutCapture();

        Assert.Equal(ShortcutCapture.Kind.Cancel, capture.OnKeyDown(Key.Escape, ModifierKeys.None).Kind);
        Assert.Equal(ShortcutCapture.Kind.Clear, capture.OnKeyDown(Key.Back, ModifierKeys.None).Kind);
        Assert.Equal(ShortcutCapture.Kind.Clear, capture.OnKeyDown(Key.Delete, ModifierKeys.None).Kind);
        Assert.Equal(ShortcutCapture.Kind.Chord, capture.OnKeyDown(Key.Escape, ModifierKeys.Alt).Kind); // Alt+Esc is a key
    }

    [Fact]
    public void Editor_applies_a_captured_key_and_reports_refusals()
    {
        ShortcutConfig? applied = null;
        var applyCount = 0;
        var editor = new ShortcutsViewModel(5, new ShortcutConfig(), c => { applied = c; applyCount++; });
        var slot1 = editor.Rows[0];

        Assert.False(editor.OnKeyDown(Key.Z, ModifierKeys.None)); // not capturing: the key is not taken
        editor.StartCapture(slot1);
        Assert.True(editor.OnKeyDown(Key.Q, ModifierKeys.None));  // a game key: refused
        Assert.True(editor.IsError);
        Assert.Equal(0, applyCount);

        editor.StartCapture(slot1);
        Assert.True(editor.OnKeyDown(Key.Z, ModifierKeys.None));
        Assert.False(editor.IsError);
        Assert.Equal("Z", slot1.Keys);
        Assert.Equal("Z", applied?.FocusSlots?[0]);

        editor.ResetToDefaults.Execute(null);
        Assert.Null(applied);
        Assert.Equal("1", slot1.Keys);
    }
}
