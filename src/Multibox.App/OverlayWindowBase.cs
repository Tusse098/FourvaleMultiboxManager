using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Multibox.App;

/// <summary>What every overlay has: background, size, arrange mode, hover and a close command.</summary>
public abstract class OverlayViewModelBase : Observable
{
    private bool _isHovered;
    private bool _arranging;
    private double _scale = 1;
    private Brush _background = Brushes.Transparent;

    protected OverlayViewModelBase(double opacity, Action close)
    {
        SetOpacity(opacity);
        Close = new RelayCommand(close);
    }

    public Brush Background { get => _background; private set => Set(ref _background, value); }
    public ICommand Close { get; }

    /// <summary>Arrange mode (click-through off): header always visible so the overlay can be dragged.</summary>
    public bool Arranging { get => _arranging; set => Set(ref _arranging, value); }

    /// <summary>The mouse is over the overlay's area (including the hidden header): show the header.</summary>
    public bool IsHovered { get => _isHovered; set => Set(ref _isHovered, value); }

    /// <summary>Size of the whole overlay, text and bars together (1 = normal).</summary>
    public double Scale { get => _scale; set => Set(ref _scale, value); }

    public void SetOpacity(double opacity)
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(Math.Clamp(opacity, 0.2, 1) * 255), 0x0F, 0x0F, 0x1A));
        brush.Freeze();
        Background = brush;
    }
}

/// <summary>
/// A window on top of the game that never takes focus from it: non-activating, no taskbar entry, click-through
/// unless arranging, and hidden instead of closed.
/// </summary>
public class OverlayWindowBase : Window
{
    private readonly DispatcherTimer _hoverTimer;
    private bool _clickThrough = true;

    public OverlayWindowBase()
    {
        SourceInitialized += (_, _) => ApplyExtendedStyle();

        // WPF's IsMouseOver is unreliable for a window that never activates, and the hidden header is click-through,
        // so hover is decided from the cursor position against the window's screen rectangle.
        _hoverTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Input, (_, _) => UpdateHover(), Dispatcher);
        _hoverTimer.Stop();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _hoverTimer.Start(); else { _hoverTimer.Stop(); SetHovered(false); }
        };
    }

    /// <summary>The overlay was dragged to a new place.</summary>
    public event Action? Moved;

    /// <summary>Dragging this element (the header) moves the overlay.</summary>
    protected void DragBy(UIElement header) =>
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not System.Windows.Controls.Button)
            {
                DragMove();
                Moved?.Invoke();
            }
        };

    /// <summary>Width of an overlay's content at size 100 %.</summary>
    public const double ContentWidth = 340;

    /// <summary>The overlay was resized by its corner; the new size (1 = normal).</summary>
    public event Action<double>? Resized;

    /// <summary>
    /// Dragging this corner grip changes the overlay's size. The size follows the mouse while dragging and is
    /// reported once on release. The mouse is measured against the window, whose left edge stays put.
    /// </summary>
    protected void ResizeBy(Thumb grip)
    {
        var offset = 0.0; // from the mouse to the overlay's right edge when the drag began
        grip.DragStarted += (_, _) =>
        {
            if (DataContext is OverlayViewModelBase viewModel)
            {
                offset = viewModel.Scale * ContentWidth - Mouse.GetPosition(this).X;
            }
        };
        grip.DragDelta += (_, _) =>
        {
            if (DataContext is OverlayViewModelBase viewModel)
            {
                viewModel.Scale = OverlayVisibility.ScaleForWidth(Mouse.GetPosition(this).X + offset, ContentWidth);
            }
        };
        grip.DragCompleted += (_, _) =>
        {
            if (DataContext is OverlayViewModelBase viewModel)
            {
                Resized?.Invoke(viewModel.Scale);
            }
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
            SetHovered(false);
        }
    }

    private void SetHovered(bool hovered)
    {
        if (DataContext is OverlayViewModelBase viewModel)
        {
            viewModel.IsHovered = hovered;
        }
    }

    private void UpdateHover()
    {
        var handle = new WindowInteropHelper(this).Handle;
        SetHovered(!_clickThrough && handle != IntPtr.Zero
            && GetCursorPos(out var cursor) && GetWindowRect(handle, out var rect)
            && cursor.X >= rect.Left && cursor.X < rect.Right && cursor.Y >= rect.Top && cursor.Y < rect.Bottom);
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
