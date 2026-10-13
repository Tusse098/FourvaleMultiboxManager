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

    [Theory]
    [InlineData(true, true, true, true)]     // the focused slot's battle has enemies
    [InlineData(true, true, false, false)]   // nothing to list: hidden
    [InlineData(true, false, false, true)]   // arranging: always shown so it can be moved
    [InlineData(false, true, true, false)]   // off wins over enemies
    [InlineData(false, false, true, false)]  // off wins over arranging
    public void When_the_enemy_overlay_is_shown(bool enabled, bool clickThrough, bool hasEnemies, bool expected)
    {
        Assert.Equal(expected, OverlayVisibility.ShouldShowEnemies(enabled, clickThrough, hasEnemies));
    }

    [Fact]
    public void Settings_saved_by_1_0_0_load_with_normal_size_and_the_enemy_overlay_on()
    {
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        var old = """{"OpenSlots":[1],"Layout":"Grid","FocusedSlot":1,"Overlay":{"Mode":"Always","ClickThrough":true,"OnlyBattleRows":false,"Opacity":0.8,"Minimized":false,"Left":30,"Top":100},"DeveloperTools":false}""";

        var loaded = JsonSerializer.Deserialize<AppSettings>(old, options)!;

        Assert.Equal((OverlayMode.Always, 1d), (loaded.Overlay.Mode, loaded.Overlay.Scale));
        Assert.Equal(new EnemyOverlaySettings(Enabled: true, Scale: 1, Left: null, Top: null), loaded.EnemyOverlay);
    }

    [Theory]
    [InlineData(340, 1)]        // dragged to its normal width
    [InlineData(510, 1.5)]
    [InlineData(465.8, 1.37)]   // whole percent
    [InlineData(40, 0.5)]       // never smaller than the slider allows
    [InlineData(5000, 2)]       // or larger
    [InlineData(-30, 0.5)]      // mouse left of the overlay
    public void Dragging_the_corner_gives_the_size_for_that_width(double width, double expected)
    {
        Assert.Equal(expected, OverlayVisibility.ScaleForWidth(width, OverlayWindowBase.ContentWidth), 6);
    }

    /// <summary>Both overlay windows build from their XAML (resources, the header and the resize grip exist).</summary>
    [Fact]
    public void Overlay_windows_load()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App();
                app.InitializeComponent();
                var party = new OverlayWindow(new OverlayViewModel([], 0.8, () => { }, () => { }));
                var enemies = new EnemyOverlayWindow(new EnemyOverlayViewModel(0.8, () => { }));
                Assert.NotNull(party.FindName("Grip"));
                Assert.NotNull(enemies.FindName("Grip"));
                Assert.NotNull(enemies.FindName("Header"));
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
    }

    [Fact]
    public void Overlay_sizes_round_trip_and_stay_within_the_slider()
    {
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        var settings = new AppSettings([1], PanelLayout.Grid, 1)
        {
            Overlay = new OverlaySettings(Scale: 1.5),
            EnemyOverlay = new EnemyOverlaySettings(Enabled: false, Scale: 0.7, Left: 400, Top: 90),
        };

        var loaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, options), options)!;

        Assert.Equal(1.5, loaded.Overlay.Scale);
        Assert.Equal(settings.EnemyOverlay, loaded.EnemyOverlay);
        Assert.Equal((0.5, 2d, 1d), (OverlayVisibility.ClampScale(0.1), OverlayVisibility.ClampScale(9), OverlayVisibility.ClampScale(double.NaN)));
    }
}
