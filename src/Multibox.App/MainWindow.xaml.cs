using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Fourvale.Adapter;
using Fourvale.Adapter.Network;
using Multibox.Core;
using Multibox.Hosting;

namespace Multibox.App;

/// <summary>
/// Composition root: the open slots (each with its own session and adapter), the shared state store, the
/// isolation monitor, the soak recorder, focus/shortcuts and the Live state window. View models read the store only.
/// Focus switching only moves keyboard focus between slots; nothing is sent to the game (spec §3.2 Tier A).
/// </summary>
public partial class MainWindow : Window
{
    private sealed record OpenSlot(SlotSession Session, SlotCardViewModel Card, SlotPanel Panel);

    private readonly AppConfig _config;
    private readonly AdapterRules _rules;
    private readonly Redactor _redactor;
    private readonly FreshnessPolicy _freshness;
    private readonly ShortcutMap _shortcuts;
    private readonly StateStore _store = new();
    private readonly IsolationMonitor _isolation;
    private readonly Log _log;
    private readonly SortedDictionary<int, OpenSlot> _open = [];
    private readonly LiveStateViewModel _liveState = new();
    private readonly LiveStateWindow _liveWindow;
    private readonly DispatcherTimer _readTimer;
    private readonly DispatcherTimer _metricsTimer;
    private readonly DispatcherTimer _soakTimer;
    private readonly DispatcherTimer _noticeTimer;
    private readonly DispatcherTimer _swapTimer;
    private int? _largeSlot;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private readonly SoakRecorder _soak;
    private BrowserProcessMetrics? _metrics;
    private BrowserProcessMetrics.Snapshot? _lastMetrics;
    private readonly KeyRouter _router;
    private readonly SettingsViewModel _settings;
    private readonly OverlayViewModel _overlay;
    private readonly OverlayWindow _overlayWindow;
    private OverlaySettings _overlaySettings = new();
    private SettingsWindow? _settingsWindow;
    private IntPtr _hwnd;
    private bool _fullscreen;
    private int? _focused;
    private string? _lastPassNote;
    private DateTimeOffset _lastRead = DateTimeOffset.UtcNow;
    private bool _closing;
    private bool _restoring;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => UseDarkTitleBar(this);

        _config = AppConfig.Load(Path.Combine(AppContext.BaseDirectory, "multibox.json"));
        _freshness = _config.CreateFreshnessPolicy();
        _shortcuts = ShortcutMap.Create(_config.Shortcuts, _config.SlotCount);
        _isolation = new IsolationMonitor(_freshness);
        _rules = AdapterRules.LoadDefault();
        _redactor = new Redactor(_rules);
        var redactor = _redactor;
        _log = new Log(
            Path.Combine(AppConfig.DataRoot, "logs", $"app-{DateTime.Now:yyyyMMdd}.log"),
            message => { var n = 0; return redactor.RedactString(message, ref n); });
        _soak = new SoakRecorder(Path.Combine(AppConfig.DataRoot, "soak"), _startedAt);
        _liveState.SoakFile = $"Soak log: {_soak.FilePath}";

        for (var number = 1; number <= _config.SlotCount; number++)
        {
            var n = number;
            _liveState.SlotPills.Add(new SlotPillViewModel(n, new RelayCommand(() => _ = ToggleSlotAsync(n))));
        }

        _liveState.SetGridLayout = new RelayCommand(() => SetLayout(PanelLayout.Grid));
        _liveState.SetFocusLayout = new RelayCommand(() => SetLayout(PanelLayout.Focus));
        _router = new KeyRouter(_shortcuts);
        _liveState.ShortcutHelp = string.Join("\n", new[]
        {
            Help(_shortcuts.Describe(ShortcutAction.FocusSlot).Replace("1", "1…" + _config.SlotCount, StringComparison.Ordinal), "focus a slot"),
            Help(_shortcuts.Describe(ShortcutAction.NextSlot), "next slot"),
            Help(_shortcuts.Describe(ShortcutAction.PreviousSlot), "previous slot"),
            Help(_shortcuts.Describe(ShortcutAction.NextReady), "character that acts next (READY first, then anyone whose battle ended, else the lowest timer)"),
            Help(_shortcuts.Describe(ShortcutAction.ToggleFullscreen), "fullscreen"),
            "Plain-key shortcuts are off while you type in chat or a login field.",
        }.Where(l => l.Length > 0));

        _settings = new SettingsViewModel(_liveState.ShortcutHelp, _config.OverlayOpacity, OnOverlaySettingsChanged);
        _overlay = new OverlayViewModel(_liveState.Cards, _config.OverlayOpacity, () => _settings.SetMode(OverlayMode.Off), SaveSettings);
        _overlayWindow = new OverlayWindow(_overlay) { Owner = null };
        _overlayWindow.RowClicked += card => FocusSlot(card.Session.Id.Number);
        _overlayWindow.Moved += SaveSettings;

        DataContext = _liveState;
        _liveWindow = new LiveStateWindow(_liveState);
        _liveWindow.SourceInitialized += (_, _) => UseDarkTitleBar(_liveWindow);

        SmoothProgress.Duration = new Duration(TimeSpan.FromMilliseconds(_config.ReadIntervalMs)); // timer bar glides between reads
        _readTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(_config.ReadIntervalMs), DispatcherPriority.Normal, (_, _) => Tick(), Dispatcher);
        _metricsTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(_config.MetricsIntervalMs), DispatcherPriority.Background, (_, _) => SampleMetrics(), Dispatcher);
        _soakTimer = new DispatcherTimer(TimeSpan.FromSeconds(_config.SoakIntervalSeconds), DispatcherPriority.Background, (_, _) => WriteSoak(), Dispatcher);
        _noticeTimer = new DispatcherTimer(TimeSpan.FromSeconds(_config.NoticeSeconds), DispatcherPriority.Background, (_, _) => ClearNotice(), Dispatcher);
        _swapTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(Math.Max(1, _config.FocusSwapDelayMs)), DispatcherPriority.Input, (_, _) => ApplyFocusSwap(), Dispatcher);

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(_hwnd)?.AddHook(SuppressAltMenu);
        };
        Deactivated += (_, _) => _router.Reset();
        ComponentDispatcher.ThreadPreprocessMessage += OnThreadMessage;
        PanelHost.SizeChanged += (_, _) => ArrangePanels();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var settings = AppSettingsStore.Load(_config);
        _log.Info($"app started; opening slots {string.Join(",", settings.OpenSlots)}, layout {settings.Layout}");
        _liveState.Layout = settings.Layout;
        _overlaySettings = settings.Overlay;
        _overlay.IsMinimized = settings.Overlay.Minimized;
        _settings.Load(settings.Overlay, _config.OverlayOpacity);
        ApplyOverlayOptions();
        ShowLiveWindow();
        _readTimer.Start();
        _metricsTimer.Start();
        _soakTimer.Start();

        // One after another: the first creates the shared browser process, the rest join it.
        _restoring = true;
        foreach (var number in settings.OpenSlots)
        {
            await OpenSlotAsync(number);
        }

        _restoring = false;
        FocusSlot(settings.FocusedSlot ?? _open.Keys.FirstOrDefault(), announce: false);
        UpdateOverlayVisibility(DateTimeOffset.UtcNow);
    }

    // ----- Slots -----

    private async Task ToggleSlotAsync(int number)
    {
        if (_open.ContainsKey(number))
        {
            FocusSlot(number); // Closing is done with ✕ on the panel, so a stray click never logs a slot out.
            return;
        }

        await OpenSlotAsync(number);
        FocusSlot(number);
    }

    private async Task OpenSlotAsync(int number)
    {
        if (_closing || _open.ContainsKey(number))
        {
            return;
        }

        var id = new SlotId(number);
        var session = new SlotSession(id, _config, _rules, _redactor, _store, _isolation, _log);
        var card = new SlotCardViewModel(session, _freshness, new RelayCommand(session.Reload), new RelayCommand(() => CloseSlot(number)));
        var panel = new SlotPanel(card);
        panel.FocusRequested += c => FocusSlot(c.Session.Id.Number);
        panel.GameFocused += c => OnGameFocused(c.Session.Id.Number);
        _open[number] = new OpenSlot(session, card, panel);

        var index = _open.Keys.ToList().IndexOf(number);
        _liveState.Cards.Insert(index, card);
        PanelHost.Children.Insert(index, panel);
        SetPill(number, true);
        ArrangePanels();
        SaveSettings();

        await session.StartAsync();
    }

    private void CloseSlot(int number)
    {
        if (!_open.Remove(number, out var slot))
        {
            return;
        }

        _liveState.Cards.Remove(slot.Card);
        PanelHost.Children.Remove(slot.Panel);
        slot.Session.Dispose();
        _store.Remove(slot.Session.Id);
        _isolation.Forget(slot.Session.Id);
        SetPill(number, false);
        _log.Info($"{slot.Session.Id}: closed");

        if (_focused == number)
        {
            _focused = null;
            var next = SlotNavigator.Cycle(OpenIds(), new SlotId(number), +1);
            FocusSlot(next?.Number ?? 0, announce: false);
        }

        ArrangePanels();
        SaveSettings();
    }

    private void SetPill(int number, bool open)
    {
        if (_liveState.SlotPills.FirstOrDefault(p => p.Number == number) is { } pill)
        {
            pill.IsOpen = open;
        }
    }

    private IReadOnlyList<SlotId> OpenIds() => _open.Keys.Select(n => new SlotId(n)).ToList();

    // ----- Focus and shortcuts -----

    private static string Help(string keys, string what) => keys.Length == 0 ? "" : $"{keys}: {what}";

    /// <summary>Where Windows' keyboard focus is (diagnostics for "shortcuts stopped"); window handles and class names only.</summary>
    private void LogKeyboardFocus()
    {
        string Describe(IntPtr window)
        {
            if (window == IntPtr.Zero)
            {
                return "none";
            }

            var name = new System.Text.StringBuilder(64);
            GetClassName(window, name, name.Capacity);
            return $"{window:X} ({name}, parent {GetParent(window):X}, root {GetAncestor(window, 2):X}, child of main: {IsChild(_hwnd, window)})";
        }

        _log.Info($"keyboard focus on this thread: {Describe(GetFocus())}; foreground: {Describe(GetForegroundWindow())}; main {_hwnd:X}");
    }

    /// <summary>
    /// Every keyboard message for this window and its child windows, before anything handles it. Each game view's
    /// keyboard focus is a child window of this process (WebView2's input window), so its keys never raise WPF key
    /// events; they do pass through this thread's message loop, which <see cref="ComponentDispatcher"/> exposes.
    /// App shortcuts are marked handled (the game never sees them); everything else goes on untouched.
    /// In-process only: no system-wide keyboard hook (ADR 0009).
    /// </summary>
    private void OnThreadMessage(ref MSG msg, ref bool handled)
    {
        const int WmKeyDown = 0x0100, WmKeyUp = 0x0101, WmSysKeyDown = 0x0104, WmSysKeyUp = 0x0105;
        if (handled || _closing || msg.message is not (WmKeyDown or WmKeyUp or WmSysKeyDown or WmSysKeyUp))
        {
            return;
        }

        // Only keys typed into the game window (or its game views), not the Settings or overlay windows.
        if (_hwnd == IntPtr.Zero || (msg.hwnd != _hwnd && !IsChild(_hwnd, msg.hwnd)))
        {
            return;
        }

        var key = KeyInterop.KeyFromVirtualKey(msg.wParam.ToInt32());
        var isDown = msg.message is WmKeyDown or WmSysKeyDown;
        var isRepeat = isDown && (msg.lParam.ToInt64() & (1L << 30)) != 0; // bit 30: key was already down
        var typing = _focused is { } f && _open.TryGetValue(f, out var slot) && slot.Session.IsTyping;
        var decision = _router.OnKey(key, isDown, CurrentModifiers(), typing, isRepeat);
        if (decision.PassedBecause is { } reason && !isRepeat)
        {
            var note = $"shortcut key {key} passed to the game: slot {_focused} reports {reason}";
            if (note != _lastPassNote)
            {
                _lastPassNote = note;
                _log.Info(note);
            }
        }
        else if (decision.Action is not null)
        {
            _lastPassNote = null;
        }
        if (decision.Swallow)
        {
            handled = true;
        }

        if (decision.Action is { } action)
        {
            Dispatcher.BeginInvoke(() => Execute(action));
        }
    }

    private void Execute((ShortcutAction Action, int Slot) binding)
    {
        _log.Info($"shortcut {binding.Action}{(binding.Slot > 0 ? $" {binding.Slot}" : "")} (focused: {_focused?.ToString() ?? "none"})");
        switch (binding.Action)
        {
            case ShortcutAction.FocusSlot:
                if (_open.ContainsKey(binding.Slot))
                {
                    FocusSlot(binding.Slot);
                }
                else
                {
                    ShowNotice($"Slot {binding.Slot} is not open");
                }

                break;

            case ShortcutAction.NextSlot:
            case ShortcutAction.PreviousSlot:
                var direction = binding.Action == ShortcutAction.NextSlot ? +1 : -1;
                if (SlotNavigator.Cycle(OpenIds(), Focused(), direction) is { } cycled)
                {
                    FocusSlot(cycled.Number);
                }

                break;

            case ShortcutAction.NextReady:
                // Whoever acts next: a READY character (cycling through them), otherwise the lowest timer.
                // Read every slot first: the read tick can lag behind (e.g. while attack animations keep the UI busy),
                // and a slot that just attacked must not still count as READY.
                var now = DateTimeOffset.UtcNow;
                var sinceRead = now - _lastRead;
                foreach (var open in _open.Values)
                {
                    open.Session.Read(now);
                }

                var timers = OpenIds().ToDictionary(id => id, id => SlotNavigator.SecondsUntilReady(_store.Get(id), _freshness, now));
                var waiting = OpenIds().Where(id => SlotNavigator.IsWaitingAfterBattle(_store.Get(id), _freshness, now)).ToHashSet();
                var next = SlotNavigator.NextToAct(OpenIds(), Focused(), id => timers[id], waiting.Contains);
                _log.Info(string.Create(CultureInfo.InvariantCulture,
                    $"next to act -> {next?.ToString() ?? "none"} ({string.Join(", ", timers.Select(t => $"{t.Key} {(t.Value is { } v ? $"{v:0.0}s" : waiting.Contains(t.Key) ? "after battle" : "-")}"))}; last read {sinceRead.TotalMilliseconds:0} ms before)"));
                if (next is { } target)
                {
                    FocusSlot(target.Number);
                }
                else
                {
                    ShowNotice("No character is in battle or waiting after one");
                }

                break;

            case ShortcutAction.ToggleFullscreen:
                ToggleFullscreen();
                break;
        }
    }

    // ----- Settings -----

    private void OnShowSettings(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_settings) { Owner = this };
            _settingsWindow.SourceInitialized += (_, _) => UseDarkTitleBar(_settingsWindow);
        }

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    // ----- Party overlay -----

    /// <summary>A Settings change: apply it to the overlay and save.</summary>
    private void OnOverlaySettingsChanged()
    {
        ApplyOverlayOptions();
        UpdateOverlayVisibility(DateTimeOffset.UtcNow);
        SaveSettings();
    }

    private void ApplyOverlayOptions()
    {
        _overlayWindow.SetClickThrough(_settings.ClickThrough);
        _overlay.Arranging = !_settings.ClickThrough;
        if (!_settings.ClickThrough)
        {
            _overlay.IsMinimized = false; // arranging needs the full overlay
        }

        _overlay.OnlyBattleRows = _settings.OnlyBattleRows;
        _overlay.SetOpacity(_settings.OpacityPercent / 100);
    }

    /// <summary>
    /// Off: never. Arranging (click-through off): always, so it can be moved. Otherwise always, or only while at least
    /// one character is in a battle (a current InBattle field; stale values do not count).
    /// </summary>
    private void UpdateOverlayVisibility(DateTimeOffset now)
    {
        var inBattle = _open.Values.Any(s => _freshness.Current(_store.Get(s.Session.Id).Character.InBattle, now) is { Value: true });
        var show = OverlayVisibility.ShouldShow(_settings.Mode, _settings.ClickThrough, inBattle);

        if (show && !_overlayWindow.IsVisible)
        {
            // Last place it was, if that is still on a screen; otherwise near the top-left of the work area.
            var left = _overlaySettings.Left ?? SystemParameters.WorkArea.Left + 24;
            var top = _overlaySettings.Top ?? SystemParameters.WorkArea.Top + 80;
            var onScreen = left >= SystemParameters.VirtualScreenLeft - 50 && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 50
                           && top >= SystemParameters.VirtualScreenTop - 10 && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40;
            _overlayWindow.Left = onScreen ? left : SystemParameters.WorkArea.Left + 24;
            _overlayWindow.Top = onScreen ? top : SystemParameters.WorkArea.Top + 80;
            _overlayWindow.Show();
        }
        else if (!show && _overlayWindow.IsVisible)
        {
            _overlaySettings = CurrentOverlaySettings();
            _overlayWindow.Hide();
        }
    }

    private OverlaySettings CurrentOverlaySettings() => new(
        _settings.Mode,
        _settings.ClickThrough,
        _settings.OnlyBattleRows,
        _settings.OpacityPercent / 100,
        _overlay.IsMinimized,
        _overlayWindow.IsVisible ? _overlayWindow.Left : _overlaySettings.Left,
        _overlayWindow.IsVisible ? _overlayWindow.Top : _overlaySettings.Top);

    // ----- Fullscreen -----

    private void OnToggleFullscreen(object sender, RoutedEventArgs e) => ToggleFullscreen();

    /// <summary>Clears any stuck typing state in every slot and the router's held-key state, then gives focus back to the game.</summary>
    private void OnUnstickKeys(object sender, RoutedEventArgs e)
    {
        var typing = _open.Where(o => o.Value.Session.IsTyping).Select(o => o.Key).OrderBy(n => n).ToList();
        foreach (var slot in _open.Values)
        {
            slot.Session.ClearTyping();
        }

        _router.Reset();
        _lastPassNote = null;
        _log.Info(typing.Count == 0 ? "unstick keys: no slot was typing" : $"unstick keys: cleared typing in slot {string.Join(",", typing)}");
        ShowNotice("Shortcuts reset");
        if (_focused is { } f)
        {
            FocusSlot(f, announce: false);
        }

        // After focus has gone back to the game (the button click itself took it).
        Dispatcher.BeginInvoke(LogKeyboardFocus, DispatcherPriority.ContextIdle);
    }

    /// <summary>Borderless fullscreen over the taskbar; the top bar hides and comes back when the mouse touches the top edge.</summary>
    private void ToggleFullscreen()
    {
        _fullscreen = !_fullscreen;
        WindowState = WindowState.Normal; // re-maximise so the new window style takes effect (borderless covers the taskbar)
        WindowStyle = _fullscreen ? WindowStyle.None : WindowStyle.SingleBorderWindow;
        ResizeMode = _fullscreen ? ResizeMode.NoResize : ResizeMode.CanResize;
        WindowState = WindowState.Maximized;
        TopBar.Visibility = _fullscreen ? Visibility.Collapsed : Visibility.Visible;
        RevealStrip.Visibility = _fullscreen ? Visibility.Visible : Visibility.Collapsed;
        FullscreenButton.Content = _fullscreen ? "Exit fullscreen" : "Fullscreen";

        _log.Info($"fullscreen {(_fullscreen ? "on" : "off")}");
        if (_focused is { } f && _open.TryGetValue(f, out var slot))
        {
            slot.Panel.FocusGame();
        }
    }

    private void OnRevealTopBar(object sender, MouseEventArgs e)
    {
        if (_fullscreen)
        {
            TopBar.Visibility = Visibility.Visible;
        }
    }

    private void OnTopBarMouseLeave(object sender, MouseEventArgs e)
    {
        if (_fullscreen)
        {
            TopBar.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Tapping Alt alone would put the window into menu mode (and eat the next key). Alt is the "next slot" key,
    /// so keyboard-invoked system-menu activation without a key (SC_KEYMENU, lParam 0) is ignored.
    /// Alt+Space and Alt+F4 still work.
    /// </summary>
    private static IntPtr SuppressAltMenu(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmSysCommand = 0x0112;
        const int ScKeyMenu = 0xF100;
        if (msg == WmSysCommand && (wParam.ToInt64() & 0xFFF0) == ScKeyMenu && lParam == IntPtr.Zero)
        {
            handled = true;
        }

        return IntPtr.Zero;
    }

    private SlotId? Focused() => _focused is { } f ? new SlotId(f) : null;

    /// <summary>Gives a slot the keyboard (and the large tile in the Focus layout).</summary>
    private void FocusSlot(int number, bool announce = true)
    {
        if (!_open.TryGetValue(number, out var slot))
        {
            UpdateFocusVisuals();
            return;
        }

        var changed = _focused != number;
        _focused = number;
        UpdateFocusVisuals();
        if (changed && _liveState.Layout == PanelLayout.Focus)
        {
            ScheduleFocusSwap();
        }

        slot.Panel.FocusGame();
        if (changed && !_restoring)
        {
            SaveSettings();
        }

        if (announce && changed)
        {
            ClearNotice();
        }
    }

    /// <summary>The player clicked into a game: that slot now has the keyboard.</summary>
    private void OnGameFocused(int number)
    {
        if (_focused == number)
        {
            return;
        }

        if (_liveState.Layout == PanelLayout.Focus)
        {
            // The click finishes in the small tile; it becomes the large one once focus has settled.
            _focused = number;
            UpdateFocusVisuals();
            ScheduleFocusSwap();
            SaveSettings();
        }
        else
        {
            _focused = number;
            UpdateFocusVisuals();
            SaveSettings();
        }
    }

    private void UpdateFocusVisuals()
    {
        foreach (var (number, slot) in _open)
        {
            slot.Card.IsFocused = number == _focused;
        }

        _liveState.FocusText = _focused is { } f && _open.ContainsKey(f) ? $"▶ Slot {f}" : "";
    }

    /// <summary>Focus layout: make the focused slot large once focus has stayed put for focusSwapDelayMs.</summary>
    private void ScheduleFocusSwap()
    {
        _swapTimer.Stop();
        if (_config.FocusSwapDelayMs == 0)
        {
            ApplyFocusSwap();
            return;
        }

        _swapTimer.Start();
    }

    private void ApplyFocusSwap()
    {
        _swapTimer.Stop();
        if (_liveState.Layout == PanelLayout.Focus && _focused != _largeSlot)
        {
            ArrangePanels();
        }
    }

    private void SetLayout(PanelLayout layout)
    {
        _liveState.Layout = layout;
        ArrangePanels();
        SaveSettings();
        if (_focused is { } f && _open.TryGetValue(f, out var slot))
        {
            slot.Panel.FocusGame();
        }
    }

    private void ShowNotice(string text)
    {
        _liveState.Notice = text;
        _noticeTimer.Stop();
        _noticeTimer.Start();
    }

    private void ClearNotice()
    {
        _noticeTimer.Stop();
        _liveState.Notice = "";
    }

    private void SaveSettings()
    {
        if (!_restoring)
        {
            AppSettingsStore.Save(new AppSettings(_open.Keys.ToList(), _liveState.Layout, _focused) { Overlay = CurrentOverlaySettings() });
        }
    }

    // ----- Layout -----

    /// <summary>Header (22) plus the 2 px status edge on each side: the part of a panel that is not game.</summary>
    private static readonly Size PanelChrome = new(4, 26);

    /// <summary>
    /// Grid: equal 16:9 tiles in the shape that makes the games largest. Focus: the focused slot large, the others
    /// in a column beside it. Every game area has the game's own shape, so no bars appear inside the games, and every
    /// slot stays visible (discovery H1). All slots render at the same size (the largest game area) and smaller tiles
    /// only scale that picture, so a focus swap never resizes a page (ADR 0007).
    /// </summary>
    private void ArrangePanels()
    {
        var count = _open.Count;
        var focus = _liveState.Layout == PanelLayout.Focus && count > 1;
        var order = _open.Values.ToList();
        if (focus)
        {
            _largeSlot = _focused is { } f && _open.ContainsKey(f) ? f : _open.Keys.First();
            order = order.OrderBy(s => s.Session.Id.Number == _largeSlot ? 0 : 1).ThenBy(s => s.Session.Id.Number).ToList();
        }
        else
        {
            _largeSlot = null;
        }

        var plan = LayoutPlanner.Arrange(
            count, focus, new Size(PanelHost.ActualWidth, PanelHost.ActualHeight), _config.GameAspect, PanelChrome, _config.FocusTileShare);
        if (plan.Panels.Count != count)
        {
            return; // not measured yet; SizeChanged arranges again
        }

        foreach (var slot in order)
        {
            slot.Panel.SetRenderSize(plan.RenderSize);
        }

        for (var i = 0; i < count; i++)
        {
            var panel = order[i].Panel;
            var rect = plan.Panels[i];
            Canvas.SetLeft(panel, rect.X);
            Canvas.SetTop(panel, rect.Y);
            panel.Width = rect.Width;
            panel.Height = rect.Height;
        }
    }

    // ----- Adapter loop, diagnostics -----

    private void Tick()
    {
        var now = DateTimeOffset.UtcNow;
        _lastRead = now;
        foreach (var slot in _open.Values)
        {
            slot.Session.Read(now);
        }

        foreach (var incident in _isolation.Check(_store, now))
        {
            _log.Error($"ISOLATION VIOLATION: {incident}");
        }

        foreach (var slot in _open.Values)
        {
            slot.Card.Refresh(_store.Get(slot.Session.Id), now);
        }

        _liveState.RefreshDiagnostics(_startedAt, now, _isolation.Violations, _lastMetrics);
        _overlay.Refresh();
        UpdateOverlayVisibility(now);
    }

    private void SampleMetrics()
    {
        // All slots share one browser process tree; measure it once.
        if (_metrics is null && _open.Values.Select(s => s.Session.Environment).FirstOrDefault(e => e is not null) is { } environment)
        {
            _metrics = new BrowserProcessMetrics(environment);
        }

        try
        {
            _lastMetrics = _metrics?.Sample();
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or ObjectDisposedException)
        {
            // The environment went away (browser restart, or the slot that provided it closed); pick another next time.
            _metrics = null;
        }
    }

    private void WriteSoak()
    {
        var now = DateTimeOffset.UtcNow;
        _soak.Write(now, _open.Values.Select(s => (_store.Get(s.Session.Id), s.Session.Recoveries)), _freshness, _isolation.Violations, _lastMetrics);
    }

    private void ShowLiveWindow()
    {
        if (!_liveWindow.IsVisible)
        {
            var area = SystemParameters.WorkArea;
            _liveWindow.Left = area.Right - _liveWindow.Width - 24;
            _liveWindow.Top = area.Top + 80;
            _liveWindow.Show();
        }

        _liveWindow.Activate();
    }

    private void OnShowLiveState(object sender, RoutedEventArgs e) => ShowLiveWindow();

    private void OnTaskManager(object sender, RoutedEventArgs e) => _open.Values.FirstOrDefault()?.Session.OpenTaskManager();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _closing = true;
        ComponentDispatcher.ThreadPreprocessMessage -= OnThreadMessage;
        _readTimer.Stop();
        _metricsTimer.Stop();
        _soakTimer.Stop();
        _noticeTimer.Stop();
        _swapTimer.Stop();
        _settingsWindow?.Close();
        SaveSettings();
        _overlayWindow.Close();
        WriteSoak();
        foreach (var slot in _open.Values)
        {
            slot.Session.Dispose();
        }

        _soak.Dispose();
        _log.Info($"app closed; isolation violations {_isolation.Violations}");
        _log.Dispose();
        _liveWindow.Close();
    }

    // Read the physical modifier keys. GetKeyState/WPF only see keys sent to this process's windows; while a game view
    // (another process's window) has the keyboard, they never see Ctrl/Alt/Shift go down. GetAsyncKeyState is physical.
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern bool IsChild(IntPtr parent, IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int maxCount);



    private static ModifierKeys CurrentModifiers()
    {
        static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
        var modifiers = ModifierKeys.None;
        if (Down(0x11)) modifiers |= ModifierKeys.Control; // VK_CONTROL
        if (Down(0x12)) modifiers |= ModifierKeys.Alt;     // VK_MENU
        if (Down(0x10)) modifiers |= ModifierKeys.Shift;   // VK_SHIFT
        return modifiers;
    }

    private const int DwmUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private static void UseDarkTitleBar(Window window)
    {
        var enabled = 1;
        _ = DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, DwmUseImmersiveDarkMode, ref enabled, sizeof(int));
    }
}
