using System.IO;
using System.Windows.Input;
using Multibox.App;

namespace Multibox.App.Tests;

public class ShortcutMapTests
{
    private static readonly ShortcutMap Defaults = ShortcutMap.Create(new ShortcutConfig(), slotCount: 5);

    [Theory]
    [InlineData(ModifierKeys.None, Key.D1, ShortcutAction.FocusSlot, 1)]
    [InlineData(ModifierKeys.None, Key.D5, ShortcutAction.FocusSlot, 5)]
    [InlineData(ModifierKeys.None, Key.NumPad3, ShortcutAction.FocusSlot, 3)]
    [InlineData(ModifierKeys.None, Key.Space, ShortcutAction.NextReady, 0)]
    [InlineData(ModifierKeys.Alt, Key.Enter, ShortcutAction.ToggleFullscreen, 0)]
    public void Default_bindings(ModifierKeys modifiers, Key key, ShortcutAction action, int slot)
    {
        Assert.Equal((action, slot), Defaults.Match(modifiers, key));
    }

    [Fact]
    public void Tapping_alt_moves_to_the_next_slot_by_default()
    {
        Assert.Equal((ShortcutAction.NextSlot, 0), Defaults.MatchAltTap());
        Assert.Null(Defaults.Match(ModifierKeys.Alt, Key.None));
    }

    [Theory]
    [InlineData(ModifierKeys.None, Key.Q)]       // game: quests
    [InlineData(ModifierKeys.None, Key.Tab)]     // game: chat tabs
    [InlineData(ModifierKeys.None, Key.Enter)]   // game: chat
    [InlineData(ModifierKeys.None, Key.D6)]      // only 5 slots
    [InlineData(ModifierKeys.Control, Key.D1)]   // Ctrl+1 is no longer bound
    [InlineData(ModifierKeys.Alt, Key.Tab)]      // Windows' own Alt+Tab
    public void Game_keys_and_unbound_chords_pass_through(ModifierKeys modifiers, Key key)
    {
        Assert.Null(Defaults.Match(modifiers, key));
    }

    [Theory]
    [InlineData(ModifierKeys.None, true)]
    [InlineData(ModifierKeys.Shift, true)]
    [InlineData(ModifierKeys.Alt, false)]
    [InlineData(ModifierKeys.Control, false)]
    public void Plain_keys_are_typing_sensitive(ModifierKeys modifiers, bool sensitive)
    {
        Assert.Equal(sensitive, ShortcutMap.IsTypingSensitive(modifiers));
    }

    [Theory]
    [InlineData("Q")]              // the game's own key
    [InlineData("Shift+Tab")]
    [InlineData("Enter")]
    [InlineData("Ctrl+F3")]        // F-keys belong to the browser layer (discovery R3)
    [InlineData("F5")]
    [InlineData("Ctrl+NoSuchKey")]
    [InlineData("Ctrl")]           // only Alt may stand alone
    [InlineData("Ctrl+A+B")]
    public void Invalid_shortcuts_are_rejected(string text)
    {
        Assert.Throws<InvalidDataException>(() => ShortcutMap.Parse(text));
    }

    [Theory]
    [InlineData("Ctrl+Q")]         // game keys are fine with Ctrl/Alt
    [InlineData("Space")]
    [InlineData("7")]
    [InlineData("Alt")]
    public void Valid_shortcuts_parse(string text)
    {
        _ = ShortcutMap.Parse(text);
    }

    [Fact]
    public void Empty_shortcut_disables_it()
    {
        var map = ShortcutMap.Create(new ShortcutConfig { NextSlot = "", NextReady = "" }, 5);

        Assert.Null(map.MatchAltTap());
        Assert.Null(map.Match(ModifierKeys.None, Key.Space));
    }

    [Fact]
    public void Duplicate_bindings_are_rejected()
    {
        var config = new ShortcutConfig { NextSlot = "Space", NextReady = "Space" };

        Assert.Throws<InvalidDataException>(() => ShortcutMap.Create(config, 5));
    }

    [Fact]
    public void Shortcuts_are_described_for_tooltips()
    {
        Assert.Equal("Space", Defaults.Describe(ShortcutAction.NextReady));
        Assert.Equal("Alt", Defaults.Describe(ShortcutAction.NextSlot));
        Assert.Equal("Alt+Enter", Defaults.Describe(ShortcutAction.ToggleFullscreen));
    }
}
