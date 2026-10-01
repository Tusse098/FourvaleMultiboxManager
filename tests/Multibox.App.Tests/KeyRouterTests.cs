using System.Windows.Input;
using Multibox.App;

namespace Multibox.App.Tests;

public class KeyRouterTests
{
    private static KeyRouter NewRouter() => new(ShortcutMap.Create(new ShortcutConfig(), slotCount: 5));

    [Fact]
    public void Digit_focuses_a_slot_and_both_press_and_release_are_swallowed()
    {
        var router = NewRouter();

        var down = router.OnKey(Key.D2, isDown: true, ModifierKeys.None, typing: false);
        var up = router.OnKey(Key.D2, isDown: false, ModifierKeys.None, typing: false);

        Assert.Equal(new KeyRouter.Decision(true, (ShortcutAction.FocusSlot, 2)), down);
        Assert.Equal(new KeyRouter.Decision(true, null), up);
    }

    [Fact]
    public void Held_key_repeat_acts_only_once()
    {
        var router = NewRouter();

        var first = router.OnKey(Key.Space, true, ModifierKeys.None, false);
        var repeat1 = router.OnKey(Key.Space, true, ModifierKeys.None, false);
        var repeat2 = router.OnKey(Key.Space, true, ModifierKeys.None, false);
        router.OnKey(Key.Space, false, ModifierKeys.None, false);
        var again = router.OnKey(Key.Space, true, ModifierKeys.None, false);

        Assert.NotNull(first.Action);
        Assert.Equal(new KeyRouter.Decision(true, null), repeat1);
        Assert.Equal(new KeyRouter.Decision(true, null), repeat2);
        Assert.NotNull(again.Action); // a new physical press
    }

    [Fact]
    public void Plain_shortcuts_pass_through_while_typing()
    {
        var router = NewRouter();

        Assert.Equal(new KeyRouter.Decision(false, null), router.OnKey(Key.D1, true, ModifierKeys.None, typing: true));
        Assert.Equal(new KeyRouter.Decision(false, null), router.OnKey(Key.Space, true, ModifierKeys.None, typing: true));
        Assert.Equal(new KeyRouter.Decision(false, null), router.OnKey(Key.D1, false, ModifierKeys.None, typing: true));
    }

    [Fact]
    public void Modified_shortcuts_still_work_while_typing()
    {
        var router = NewRouter();

        var decision = router.OnKey(Key.Enter, true, ModifierKeys.Alt, typing: true);

        Assert.Equal((ShortcutAction.ToggleFullscreen, 0), decision.Action);
    }

    [Fact]
    public void Game_keys_pass_through()
    {
        var router = NewRouter();

        foreach (var key in new[] { Key.Q, Key.I, Key.M, Key.Enter, Key.Escape, Key.Tab, Key.W, Key.D9 })
        {
            Assert.Equal(new KeyRouter.Decision(false, null), router.OnKey(key, true, ModifierKeys.None, false));
            Assert.Equal(new KeyRouter.Decision(false, null), router.OnKey(key, false, ModifierKeys.None, false));
        }
    }

    [Fact]
    public void Alt_tap_acts_on_release_and_alt_is_never_swallowed()
    {
        var router = NewRouter();

        var down = router.OnKey(Key.LeftAlt, true, ModifierKeys.Alt, false);
        var held = router.OnKey(Key.LeftAlt, true, ModifierKeys.Alt, false); // repeat while held
        var up = router.OnKey(Key.LeftAlt, false, ModifierKeys.None, false);

        Assert.Equal(new KeyRouter.Decision(false, null), down);
        Assert.Equal(new KeyRouter.Decision(false, null), held);
        Assert.Equal(new KeyRouter.Decision(false, (ShortcutAction.NextSlot, 0)), up);
    }

    [Fact]
    public void Alt_with_another_key_is_not_a_tap()
    {
        var router = NewRouter();

        router.OnKey(Key.LeftAlt, true, ModifierKeys.Alt, false);
        router.OnKey(Key.Tab, true, ModifierKeys.Alt, false);   // Windows Alt+Tab
        router.OnKey(Key.Tab, false, ModifierKeys.Alt, false);
        var up = router.OnKey(Key.LeftAlt, false, ModifierKeys.None, false);

        Assert.Null(up.Action);
    }

    [Fact]
    public void Alt_enter_toggles_fullscreen_without_counting_as_alt_tap()
    {
        var router = NewRouter();

        router.OnKey(Key.LeftAlt, true, ModifierKeys.Alt, false);
        var enter = router.OnKey(Key.Enter, true, ModifierKeys.Alt, false);
        router.OnKey(Key.Enter, false, ModifierKeys.Alt, false);
        var up = router.OnKey(Key.LeftAlt, false, ModifierKeys.None, false);

        Assert.Equal(new KeyRouter.Decision(true, (ShortcutAction.ToggleFullscreen, 0)), enter);
        Assert.Null(up.Action);
    }
}
