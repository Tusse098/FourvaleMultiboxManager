using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows;

namespace Multibox.App;

/// <summary>Settings shown in <see cref="SettingsWindow"/>: party overlay options, the keyboard reference and developer tools.</summary>
public sealed class SettingsViewModel(string shortcutHelp, double defaultOpacity, Action changed) : Observable
{
    private OverlayMode _mode = OverlayMode.InBattle;
    private bool _clickThrough = true;
    private bool _onlyBattleRows;
    private double _opacityPercent = Math.Round(defaultOpacity * 100);
    private bool _developerTools;

    public string ShortcutHelp { get; } = shortcutHelp;

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
        ? "Clicks go to the game. Untick to move the overlay."
        : "Arranging: the overlay is shown with its header; drag it into place, then tick this again.";

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

    public OverlayMode Mode => _mode;

    public void Load(OverlaySettings overlay, double defaultOpacity, bool developerTools)
    {
        _developerTools = developerTools;
        _mode = overlay.Mode;
        _clickThrough = overlay.ClickThrough;
        _onlyBattleRows = overlay.OnlyBattleRows;
        _opacityPercent = Math.Clamp(Math.Round((overlay.Opacity ?? defaultOpacity) * 100), 20, 100);
        foreach (var name in new[] { nameof(ShowInBattle), nameof(ShowAlways), nameof(ShowOff), nameof(ClickThrough), nameof(ArrangeHint), nameof(OnlyBattleRows), nameof(OpacityPercent), nameof(OpacityText), nameof(DeveloperTools) })
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
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

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
