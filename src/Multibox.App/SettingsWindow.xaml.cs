using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Input;

namespace Multibox.App;

/// <summary>Settings shown in <see cref="SettingsWindow"/>: party and enemy overlay options, shortcuts and developer tools.</summary>
public sealed class SettingsViewModel(ShortcutsViewModel shortcuts, double defaultOpacity, Action changed) : Observable
{
    private OverlayMode _mode = OverlayMode.InBattle;
    private bool _clickThrough = true;
    private bool _onlyBattleRows;
    private double _opacityPercent = Math.Round(defaultOpacity * 100);
    private double _scalePercent = 100;
    private bool _enemyOverlay = true;
    private double _enemyScalePercent = 100;
    private bool _developerTools;

    public ShortcutsViewModel Shortcuts { get; } = shortcuts;

    public string VersionText { get; } =
        $"Version {typeof(SettingsViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "?"}";

    /// <summary>Shows the Live state window and browser task manager buttons and writes the soak log.</summary>
    public bool DeveloperTools
    {
        get => _developerTools;
        set { if (Set(ref _developerTools, value)) changed(); }
    }

    public bool ShowInBattle { get => _mode == OverlayMode.InBattle; set { if (value) SetMode(OverlayMode.InBattle); } }
    public bool ShowAlways { get => _mode == OverlayMode.Always; set { if (value) SetMode(OverlayMode.Always); } }
    public bool ShowOff { get => _mode == OverlayMode.Off; set { if (value) SetMode(OverlayMode.Off); } }

    /// <summary>Clicks pass through the overlay to the game. Untick to drag it into place.</summary>
    public bool ClickThrough
    {
        get => _clickThrough;
        set
        {
            if (Set(ref _clickThrough, value))
            {
                OnPropertyChanged(nameof(ArrangeHint));
                changed();
            }
        }
    }

    public string ArrangeHint => ClickThrough
        ? "Clicks go to the game. Untick to move or resize the overlays."
        : "Arranging: the overlays are shown with their header; drag the header to move, drag the corner to resize, then tick this again.";

    public bool OnlyBattleRows
    {
        get => _onlyBattleRows;
        set { if (Set(ref _onlyBattleRows, value)) changed(); }
    }

    /// <summary>Background opacity in percent (20–100).</summary>
    public double OpacityPercent
    {
        get => _opacityPercent;
        set
        {
            if (Set(ref _opacityPercent, Math.Clamp(Math.Round(value), 20, 100)))
            {
                OnPropertyChanged(nameof(OpacityText));
                changed();
            }
        }
    }

    public string OpacityText => string.Create(CultureInfo.CurrentCulture, $"{OpacityPercent:0}%");

    /// <summary>Size of the party overlay in percent (50–200).</summary>
    public double ScalePercent
    {
        get => _scalePercent;
        set
        {
            if (Set(ref _scalePercent, ToScalePercent(value / 100)))
            {
                OnPropertyChanged(nameof(ScaleText));
                changed();
            }
        }
    }

    public string ScaleText => string.Create(CultureInfo.CurrentCulture, $"{ScalePercent:0}%");

    /// <summary>Show the HP of the enemies in the focused slot's battle.</summary>
    public bool EnemyOverlay
    {
        get => _enemyOverlay;
        set { if (Set(ref _enemyOverlay, value)) changed(); }
    }

    /// <summary>Size of the enemy overlay in percent (50–200).</summary>
    public double EnemyScalePercent
    {
        get => _enemyScalePercent;
        set
        {
            if (Set(ref _enemyScalePercent, ToScalePercent(value / 100)))
            {
                OnPropertyChanged(nameof(EnemyScaleText));
                changed();
            }
        }
    }

    public string EnemyScaleText => string.Create(CultureInfo.CurrentCulture, $"{EnemyScalePercent:0}%");

    public OverlayMode Mode => _mode;

    private static double ToScalePercent(double scale) => Math.Round(OverlayVisibility.ClampScale(scale) * 100);

    public void Load(OverlaySettings overlay, EnemyOverlaySettings enemyOverlay, double defaultOpacity, bool developerTools)
    {
        _developerTools = developerTools;
        _mode = overlay.Mode;
        _clickThrough = overlay.ClickThrough;
        _onlyBattleRows = overlay.OnlyBattleRows;
        _opacityPercent = Math.Clamp(Math.Round((overlay.Opacity ?? defaultOpacity) * 100), 20, 100);
        _scalePercent = ToScalePercent(overlay.Scale);
        _enemyOverlay = enemyOverlay.Enabled;
        _enemyScalePercent = ToScalePercent(enemyOverlay.Scale);
        foreach (var name in new[]
                 {
                     nameof(ShowInBattle), nameof(ShowAlways), nameof(ShowOff), nameof(ClickThrough), nameof(ArrangeHint), nameof(OnlyBattleRows),
                     nameof(OpacityPercent), nameof(OpacityText), nameof(ScalePercent), nameof(ScaleText),
                     nameof(EnemyOverlay), nameof(EnemyScalePercent), nameof(EnemyScaleText), nameof(DeveloperTools),
                 })
        {
            OnPropertyChanged(name);
        }
    }

    public void SetMode(OverlayMode mode)
    {
        if (_mode == mode)
        {
            return;
        }

        _mode = mode;
        OnPropertyChanged(nameof(ShowInBattle));
        OnPropertyChanged(nameof(ShowAlways));
        OnPropertyChanged(nameof(ShowOff));
        changed();
    }
}

public partial class SettingsWindow : Window
{
    private readonly ShortcutsViewModel _shortcuts;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _shortcuts = viewModel.Shortcuts;

        // While a shortcut is being changed, keys typed into this window become the new shortcut.
        PreviewKeyDown += (_, e) => e.Handled = _shortcuts.OnKeyDown(RealKey(e), Keyboard.Modifiers);
        PreviewKeyUp += (_, e) => e.Handled = _shortcuts.OnKeyUp(RealKey(e));
        Deactivated += (_, _) => _shortcuts.CancelCapture();
        IsVisibleChanged += (_, _) => _shortcuts.CancelCapture();
    }

    /// <summary>With Alt held, WPF reports <see cref="Key.System"/> and the real key in <see cref="KeyEventArgs.SystemKey"/>.</summary>
    private static Key RealKey(KeyEventArgs e) => e.Key switch
    {
        Key.System => e.SystemKey,
        Key.ImeProcessed => e.ImeProcessedKey,
        _ => e.Key,
    };

    // Closing only hides it; the main window can show it again.
    protected override void OnClosing(CancelEventArgs e)
    {
        if (Application.Current.MainWindow?.IsLoaded == true && !Application.Current.Dispatcher.HasShutdownStarted)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }
}
