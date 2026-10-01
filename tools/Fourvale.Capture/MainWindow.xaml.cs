using Fourvale.Adapter;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Fourvale.Adapter.Network;
using Fourvale.Capture.Capture;
using Fourvale.Capture.Ui;

namespace Fourvale.Capture;

/// <summary>
/// Hosts one <see cref="SlotPanel"/> per open slot and arranges them by layout.
/// Capture state lives in the view models; this class only manages panels.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly string DataRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FourvaleMultibox");

    private static readonly string SettingsPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FourvaleMultibox", "capture-tool.json");

    private readonly CaptureRules _rules;
    private readonly AdapterRules _adapterRules;
    private readonly Redactor _redactor;
    private readonly MainViewModel _viewModel;
    private readonly Dictionary<SlotViewModel, SlotPanel> _panels = [];
    private bool _closingAfterStop;

    public MainWindow()
    {
        InitializeComponent();
        FitToScreen();
        SourceInitialized += (_, _) => UseDarkTitleBar();

        _rules = CaptureRules.Load(Path.Combine(AppContext.BaseDirectory, "capture-rules.json"));
        _adapterRules = AdapterRules.LoadDefault();
        _redactor = new Redactor(_adapterRules);
        _viewModel = new MainViewModel(_rules, _redactor, Path.Combine(DataRoot, "captures"), SettingsPath);

        _viewModel.SlotOpened += OpenPanel;
        _viewModel.SlotClosed += ClosePanel;
        _viewModel.LayoutChanged += ArrangePanels;
        _viewModel.ReloadRequested += slot => Panel(slot)?.Reload();
        _viewModel.ReturnFocusToGame += () => Panel(_viewModel.ActiveSlot)?.FocusGame();
        _viewModel.ObserverState = slot =>
        {
            var observer = Panel(slot)?.Observer;
            return observer is null
                ? ([], new Dictionary<string, string>())
                : (observer.Bundles.ToList(), new Dictionary<string, string>(observer.OpenSockets));
        };
        DataContext = _viewModel;

        Loaded += (_, _) => _viewModel.RestoreSession();
        Closing += OnClosing;
    }

    private SlotPanel? Panel(SlotViewModel slot) => _panels.GetValueOrDefault(slot);

    private void OpenPanel(SlotViewModel slot)
    {
        if (_panels.ContainsKey(slot))
        {
            return;
        }

        var panel = new SlotPanel(slot, _rules, _adapterRules, _redactor);
        panel.Activated += _viewModel.Activate;
        _panels[slot] = panel;
        PanelHost.Children.Add(panel);
        ArrangePanels();
        _ = panel.StartAsync(Path.Combine(DataRoot, "WebView2"));
    }

    private void ClosePanel(SlotViewModel slot)
    {
        if (_panels.Remove(slot, out var panel))
        {
            panel.Shutdown();
            PanelHost.Children.Remove(panel);
        }
    }

    /// <summary>
    /// Every open slot always stays visible: a hidden page pauses the game (observed 2026-10-01).
    /// Focus: the active slot large, the others as a live strip below it.
    /// Side by side: one row. Grid: as square as possible.
    /// </summary>
    private void ArrangePanels()
    {
        var panels = _panels.Values.OrderBy(p => p.Slot.Number).ToList();
        PanelHost.RowDefinitions.Clear();
        PanelHost.ColumnDefinitions.Clear();
        if (panels.Count == 0)
        {
            return;
        }

        if (_viewModel.Layout == PanelLayout.Focus)
        {
            ArrangeFocus(panels);
            return;
        }

        var (columns, rows) = _viewModel.Layout == PanelLayout.SideBySide ? (panels.Count, 1) : GridShape(panels.Count);
        for (var c = 0; c < columns; c++) PanelHost.ColumnDefinitions.Add(new ColumnDefinition());
        for (var r = 0; r < rows; r++) PanelHost.RowDefinitions.Add(new RowDefinition());

        for (var i = 0; i < panels.Count; i++)
        {
            Place(panels[i], row: i / columns, column: i % columns, columnSpan: 1);
        }
    }

    private void ArrangeFocus(List<SlotPanel> panels)
    {
        var active = panels.FirstOrDefault(p => p.Slot.IsActive) ?? panels[0];
        var others = panels.Where(p => !ReferenceEquals(p, active)).ToList();
        if (others.Count == 0)
        {
            Place(active, row: 0, column: 0, columnSpan: 1);
            return;
        }

        var strip = Math.Clamp(_rules.FocusStripShare, 0.1, 0.5);
        PanelHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1 - strip, GridUnitType.Star) });
        PanelHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(strip, GridUnitType.Star) });
        foreach (var _ in others)
        {
            PanelHost.ColumnDefinitions.Add(new ColumnDefinition());
        }

        Place(active, row: 0, column: 0, columnSpan: others.Count);
        for (var i = 0; i < others.Count; i++)
        {
            Place(others[i], row: 1, column: i, columnSpan: 1);
        }
    }

    private static void Place(SlotPanel panel, int row, int column, int columnSpan)
    {
        Grid.SetRow(panel, row);
        Grid.SetColumn(panel, column);
        Grid.SetColumnSpan(panel, columnSpan);
        panel.Visibility = Visibility.Visible;
    }

    private static (int Columns, int Rows) GridShape(int count)
    {
        var columns = (int)Math.Ceiling(Math.Sqrt(count));
        var rows = (int)Math.Ceiling(count / (double)columns);
        return (columns, rows);
    }

    private void FitToScreen()
    {
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Width, area.Width - 32);
        Height = Math.Min(Height, area.Height - 32);
    }

    private const int DwmUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void UseDarkTitleBar()
    {
        var enabled = 1;
        _ = DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, DwmUseImmersiveDarkMode, ref enabled, sizeof(int));
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closingAfterStop || !_viewModel.IsRecording)
        {
            foreach (var panel in _panels.Values)
            {
                panel.Shutdown();
            }

            return;
        }

        // Finish writing every capture file before the window goes away.
        e.Cancel = true;
        await _viewModel.StopAsync();
        _closingAfterStop = true;
        Close();
    }
}
