using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace Multibox.App;

public enum PanelLayout
{
    /// <summary>All slots in equal tiles (spec §8.1).</summary>
    Grid,

    /// <summary>The focused slot large, the others as small live tiles beside it (spec §8.1). Nothing is hidden (discovery H1).</summary>
    Focus,
}

/// <summary>One button in the top bar's slot picker.</summary>
public sealed class SlotPillViewModel(int number, ICommand toggle) : Observable
{
    private bool _isOpen;

    public int Number { get; } = number;
    public ICommand Toggle { get; } = toggle;

    public bool IsOpen
    {
        get => _isOpen;
        set => Set(ref _isOpen, value);
    }
}

/// <summary>When the party overlay is shown.</summary>
public enum OverlayMode
{
    /// <summary>Only while at least one character is in a battle.</summary>
    InBattle,
    Always,
    Off,
}

/// <summary>The rule for when the party overlay is on screen.</summary>
public static class OverlayVisibility
{
    /// <summary>
    /// Off: never. Arranging (click-through off): always, so it can be moved. Always: always.
    /// In battle: only while at least one character is currently in a battle.
    /// </summary>
    public static bool ShouldShow(OverlayMode mode, bool clickThrough, bool anyInBattle) => mode switch
    {
        OverlayMode.Off => false,
        _ when !clickThrough => true,
        OverlayMode.Always => true,
        _ => anyInBattle,
    };

    /// <summary>
    /// The enemy overlay. Off: never. Arranging (click-through off): always, so it can be moved.
    /// Otherwise only while the focused slot has current enemies to list.
    /// </summary>
    public static bool ShouldShowEnemies(bool enabled, bool clickThrough, bool hasEnemies) =>
        enabled && (!clickThrough || hasEnemies);

    public const double MinScale = 0.5;
    public const double MaxScale = 2;

    /// <summary>A saved overlay size, kept within what the Settings slider offers.</summary>
    public static double ClampScale(double scale) => double.IsFinite(scale) ? Math.Clamp(scale, MinScale, MaxScale) : 1;

    /// <summary>The size at which an overlay is this wide, in whole percent and within the slider's range (drag to resize).</summary>
    public static double ScaleForWidth(double width, double widthAtNormalSize) =>
        ClampScale(Math.Round(width / widthAtNormalSize, 2));
}

/// <summary>Party overlay options and placement (Settings window). No game data.</summary>
/// <param name="ClickThrough">Clicks pass through the overlay to the game. Off = arrange mode: always shown, header visible, draggable.</param>
/// <param name="OnlyBattleRows">List only characters that are in a battle.</param>
/// <param name="Opacity">Background opacity 0.2–1; null = the default from multibox.json.</param>
/// <param name="Scale">Size of the whole overlay, 0.5–2 (1 = normal).</param>
public sealed record OverlaySettings(
    OverlayMode Mode = OverlayMode.InBattle,
    bool ClickThrough = true,
    bool OnlyBattleRows = false,
    double? Opacity = null,
    bool Minimized = false,
    double? Left = null,
    double? Top = null,
    double Scale = 1);

/// <summary>
/// Enemy overlay options and placement (Settings window). No game data. Click-through and background opacity
/// are shared with the party overlay.
/// </summary>
/// <param name="Scale">Size of the whole overlay, 0.5–2 (1 = normal).</param>
public sealed record EnemyOverlaySettings(
    bool Enabled = true,
    double Scale = 1,
    double? Left = null,
    double? Top = null);

/// <summary>Open slots, layout, focused slot and overlay placement, so the next start looks the same. No game data.</summary>
public sealed record AppSettings(List<int> OpenSlots, PanelLayout Layout, int? FocusedSlot)
{
    public OverlaySettings Overlay { get; init; } = new();

    public EnemyOverlaySettings EnemyOverlay { get; init; } = new();

    /// <summary>Live state window, soak log and browser task manager, for troubleshooting. Off for normal players.</summary>
    public bool DeveloperTools { get; init; }

    /// <summary>The player's own shortcuts from Settings; null = the defaults in multibox.json.</summary>
    public ShortcutConfig? Shortcuts { get; init; }
}

/// <summary>
/// Stored in %AppData%\FourvaleMultibox\app-settings.json. (Full persistence is Phase 7.)
/// </summary>
public static class AppSettingsStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FourvaleMultibox", "app-settings.json");

    private static readonly JsonSerializerOptions Options = new() { Converters = { new JsonStringEnumConverter() } };

    public static AppSettings Load(AppConfig config)
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) is { OpenSlots: not null } saved)
            {
                var open = saved.OpenSlots.Where(n => n >= 1 && n <= config.SlotCount).Distinct().Order().ToList();
                return saved with { OpenSlots = open, FocusedSlot = open.Contains(saved.FocusedSlot ?? 0) ? saved.FocusedSlot : open.FirstOrDefault() };
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Unreadable settings: fall back to the defaults.
        }

        var defaults = config.DefaultSlots.Distinct().Order().ToList();
        return new AppSettings(defaults, PanelLayout.Grid, defaults.FirstOrDefault());
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings with { OpenSlots = settings.OpenSlots.Order().ToList() }, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not saving the choice must not affect the running sessions.
        }
    }
}
