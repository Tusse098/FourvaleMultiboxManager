using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Multibox.App;

/// <summary>Party overlay: a compact row per open slot, read from the state store via the slot cards.</summary>
public sealed class OverlayViewModel : Observable
{
    private bool _isMinimized;
    private bool _isHovered;
    private bool _arranging;
    private bool _onlyBattleRows;
    private Brush _background = Brushes.Transparent;
    private string _summary = "";

    public OverlayViewModel(ObservableCollection<SlotCardViewModel> cards, double opacity, Action close, Action changed)
    {
        Cards = cards;
        SetOpacity(opacity);
        Close = new RelayCommand(close);
        ToggleMinimized = new RelayCommand(() =>
        {
            IsMinimized = !IsMinimized;
            changed();
        });
    }

    public ObservableCollection<SlotCardViewModel> Cards { get; }
    public Brush Background { get => _background; private set => Set(ref _background, value); }

    /// <summary>Arrange mode (click-through off): header always visible so the overlay can be dragged.</summary>
    public bool Arranging { get => _arranging; set => Set(ref _arranging, value); }

    /// <summary>Hide rows of characters that are not in a battle.</summary>
    public bool OnlyBattleRows { get => _onlyBattleRows; set => Set(ref _onlyBattleRows, value); }

    public void SetOpacity(double opacity)
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(Math.Clamp(opacity, 0.2, 1) * 255), 0x0F, 0x0F, 0x1A));
        brush.Freeze();
        Background = brush;
    }
    public ICommand Close { get; }
    public ICommand ToggleMinimized { get; }

    public bool IsMinimized
    {
        get => _isMinimized;
        set
        {
            if (Set(ref _isMinimized, value))
            {
                OnPropertyChanged(nameof(IsExpanded));
                OnPropertyChanged(nameof(MinimizeGlyph));
            }
        }
    }

    public bool IsExpanded => !IsMinimized;

    /// <summary>The mouse is over the overlay's area (including the hidden header): show the header.</summary>
    public bool IsHovered { get => _isHovered; set => Set(ref _isHovered, value); }
    public string MinimizeGlyph => IsMinimized ? "▢" : "–";

    /// <summary>One line for the header, so the minimised overlay still says who needs attention.</summary>
    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    public void Refresh()
    {
        var ready = Cards.Where(c => c.IsReady).Select(c => c.Session.Id.Number).ToList();
        var inBattle = Cards.Count(c => c.IsInBattle);
        var problems = Cards.Count(c => c.IsProblem);
        var parts = new List<string>();
        if (ready.Count > 0) parts.Add($"READY: {string.Join(", ", ready)}");
        if (inBattle > 0) parts.Add(string.Create(CultureInfo.CurrentCulture, $"{inBattle} in battle"));
        if (problems > 0) parts.Add(string.Create(CultureInfo.CurrentCulture, $"{problems} with problems"));
        Summary = parts.Count > 0 ? string.Join(" · ", parts) : $"{Cards.Count} slots";
    }
}

public partial class OverlayWindow : Window
{
    private readonly OverlayViewModel _viewModel;
    private readonly DispatcherTimer _hoverTimer;

    public OverlayWindow(OverlayViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not System.Windows.Controls.Button)
            {
                DragMove();
                Moved?.Invoke();
            }
        };
        SourceInitialized += (_, _) => ApplyExtendedStyle();

        // WPF's IsMouseOver is unreliable for a window that never activates, and the hidden header is click-through,
        // so hover is decided from the cursor position against the window's screen rectangle.
        _hoverTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Input, (_, _) => UpdateHover(), Dispatcher);
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _hoverTimer.Start(); else { _hoverTimer.Stop(); _viewModel.IsHovered = false; }
        };
    }

    /// <summary>
    /// Click-through: every click passes to the window underneath (the game), as if the overlay were not there.
    /// Its header and buttons cannot be used then; turn it off in Settings to arrange the overlay.
    /// </summary>
    public void SetClickThrough(bool clickThrough)
    {
        _clickThrough = clickThrough;
        ApplyExtendedStyle();
        if (clickThrough)
        {
            _viewModel.IsHovered = false;
        }
    }

    private bool _clickThrough = true;

    private void UpdateHover()
    {
        var handle = new WindowInteropHelper(this).Handle;
        _viewModel.IsHovered = !_clickThrough && handle != IntPtr.Zero
            && GetCursorPos(out var cursor) && GetWindowRect(handle, out var rect)
            && cursor.X >= rect.Left && cursor.X < rect.Right && cursor.Y >= rect.Top && cursor.Y < rect.Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    /// <summary>The player clicked a character row: focus that slot.</summary>
    public event Action<SlotCardViewModel>? RowClicked;

    /// <summary>The overlay was dragged to a new place.</summary>
    public event Action? Moved;

    private void OnRowClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SlotCardViewModel card })
        {
            RowClicked?.Invoke(card);
        }
    }

    // Closing (Alt+F4 or the app shutting down) hides it unless the app is exiting.
    protected override void OnClosing(CancelEventArgs e)
    {
        if (Application.Current.MainWindow?.IsLoaded == true && !Application.Current.Dispatcher.HasShutdownStarted)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    /// <summary>
    /// Never activates (clicking or dragging must not take focus from the game window, or the app's shortcuts
    /// would stop working), no taskbar entry, and click-through (WS_EX_TRANSPARENT) unless arranging.
    /// </summary>
    private void ApplyExtendedStyle()
    {
        const int GwlExStyle = -20;
        const long WsExNoActivate = 0x08000000L;
        const long WsExToolWindow = 0x00000080L;
        const long WsExTransparent = 0x00000020L;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return; // applied in SourceInitialized
        }

        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64() | WsExNoActivate | WsExToolWindow;
        style = _clickThrough ? style | WsExTransparent : style & ~WsExTransparent;
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
}
