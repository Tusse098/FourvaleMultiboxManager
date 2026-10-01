using System.Text.Json;
using System.Text.Json.Serialization;
using Multibox.App;

namespace Multibox.App.Tests;

public class OverlayVisibilityTests
{
    [Theory]
    [InlineData(OverlayMode.InBattle, true, true, true)]     // a character is fighting
    [InlineData(OverlayMode.InBattle, true, false, false)]   // nobody fighting: hidden
    [InlineData(OverlayMode.Always, true, false, true)]
    [InlineData(OverlayMode.Off, true, true, false)]         // off wins over battles
    [InlineData(OverlayMode.InBattle, false, false, true)]   // arranging: always shown so it can be moved
    [InlineData(OverlayMode.Off, false, true, false)]        // off wins over arranging
    public void When_the_overlay_is_shown(OverlayMode mode, bool clickThrough, bool anyInBattle, bool expected)
    {
        Assert.Equal(expected, OverlayVisibility.ShouldShow(mode, clickThrough, anyInBattle));
    }

    [Fact]
    public void Defaults_are_in_battle_only_and_click_through()
    {
        var settings = new OverlaySettings();

        Assert.Equal((OverlayMode.InBattle, true, false), (settings.Mode, settings.ClickThrough, settings.OnlyBattleRows));
    }

    [Fact]
    public void Settings_saved_by_the_previous_version_still_load()
    {
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        var old = """{"OpenSlots":[1,2],"Layout":"Focus","FocusedSlot":1,"Overlay":{"Visible":true,"Minimized":false,"Left":30,"Top":100}}""";

        var loaded = JsonSerializer.Deserialize<AppSettings>(old, options)!;

        Assert.Equal((OverlayMode.InBattle, true, 30d, 100d), (loaded.Overlay.Mode, loaded.Overlay.ClickThrough, loaded.Overlay.Left, loaded.Overlay.Top));
    }
}
