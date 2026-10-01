using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using Fourvale.Adapter.Network;
using Fourvale.Capture.Capture;

namespace Fourvale.Capture.Ui;

public sealed class MainViewModel : Observable
{
    private readonly CaptureRules _rules;
    private readonly ToolSettings _settings;
    private readonly string _settingsPath;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _liveTimer;
    private int _liveTick;
    private bool _showDecoded = true;

    private SlotViewModel _activeSlot;
    private PanelLayout _layout;
    private bool _isRecording;
    private bool _isBusy;
    private bool _reloadOnStart;
    private string _noteText = "";
    private string _lastMarker = "";

    public MainViewModel(CaptureRules rules, Redactor redactor, string captureFolder, string settingsPath)
    {
        _rules = rules;
        _settingsPath = settingsPath;
        _settings = ToolSettings.Load(settingsPath);
        CaptureFolder = captureFolder;

        Slots = new ObservableCollection<SlotViewModel>(
            Enumerable.Range(1, Math.Max(1, rules.SlotCount)).Select(n => new SlotViewModel(n, redactor, rules.MessageTypeListSize)));
        MarkerPresets = rules.MarkerPresets;

        _layout = _settings.Layout;
        _reloadOnStart = _settings.ReloadOnStart;
        _activeSlot = Slots.FirstOrDefault(s => s.Number == _settings.ActiveSlot) ?? Slots[0];
        _activeSlot.IsActive = true;

        SlotPillCommand = new RelayCommand(p => OnSlotPill(p as SlotViewModel));
        CloseSlotCommand = new RelayCommand(p => CloseSlot(p as SlotViewModel), _ => !IsRecording);
        ActivateSlotCommand = new RelayCommand(p => { if (p is SlotViewModel s) Activate(s); });
        ReloadSlotCommand = new RelayCommand(p => { if (p is SlotViewModel s) ReloadRequested?.Invoke(s); });
        SetLayoutCommand = new RelayCommand(p => { if (Enum.TryParse<PanelLayout>(p as string, out var l)) Layout = l; });
        StartStopCommand = new RelayCommand(_ => _ = ToggleRecordingAsync(), _ => !_isBusy && Slots.Any(s => s.IsOpen && s.IsReady));
        AddMarkerCommand = new RelayCommand(p => AddMarker(p as string), _ => ActiveSlot.IsRecording);
        AddNoteCommand = new RelayCommand(_ => AddNote(), _ => ActiveSlot.IsRecording && !string.IsNullOrWhiteSpace(NoteText));
        OpenFolderCommand = new RelayCommand(_ => OpenFolder());
        ReloadCommand = new RelayCommand(_ => ReloadRequested?.Invoke(ActiveSlot), _ => ActiveSlot.IsReady);

        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Refresh(), Dispatcher.CurrentDispatcher);
        _timer.Start();

        // The action meter moves every ~50 ms in battle; 4 refreshes a second keeps the view readable.
        _liveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background,
            (_, _) => ActiveSlot.RefreshLive(includeJson: ++_liveTick % 4 == 0), Dispatcher.CurrentDispatcher);
        _liveTimer.Start();

        ShowDecodedCommand = new RelayCommand(_ => ShowDecoded = true);
        ShowMessagesCommand = new RelayCommand(_ => ShowDecoded = false);
    }

    public ICommand ShowDecodedCommand { get; }
    public ICommand ShowMessagesCommand { get; }

    /// <summary>Sidebar shows the decoded state (true) or the message-type list (false).</summary>
    public bool ShowDecoded
    {
        get => _showDecoded;
        set
        {
            if (Set(ref _showDecoded, value))
            {
                Raise(nameof(ShowMessages));
                ReturnFocusToGame?.Invoke();
            }
        }
    }

    public bool ShowMessages => !ShowDecoded;

    /// <summary>The window creates a game view for this slot.</summary>
    public event Action<SlotViewModel>? SlotOpened;

    /// <summary>The window disposes this slot's game view.</summary>
    public event Action<SlotViewModel>? SlotClosed;

    /// <summary>The window re-arranges the open panels.</summary>
    public event Action? LayoutChanged;

    public event Action<SlotViewModel>? ReloadRequested;

    /// <summary>Raised after a sidebar action so the window can hand keyboard focus back to the active game.</summary>
    public event Action? ReturnFocusToGame;

    public string CaptureFolder { get; }
    public ObservableCollection<SlotViewModel> Slots { get; }
    public IReadOnlyList<string> MarkerPresets { get; }
    public string GameUrl => _rules.GameUrl;

    public ICommand SlotPillCommand { get; }
    public ICommand CloseSlotCommand { get; }
    public ICommand ActivateSlotCommand { get; }
    public ICommand ReloadSlotCommand { get; }
    public ICommand SetLayoutCommand { get; }
    public ICommand StartStopCommand { get; }
    public ICommand AddMarkerCommand { get; }
    public ICommand AddNoteCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand ReloadCommand { get; }

    public SlotViewModel ActiveSlot
    {
        get => _activeSlot;
        private set
        {
            if (ReferenceEquals(value, _activeSlot))
            {
                return;
            }

            _activeSlot.IsActive = false;
            _activeSlot = value;
            _activeSlot.IsActive = true;
            _activeSlot.RefreshLive(includeJson: true);
            Raise();
            Raise(nameof(WindowTitle));
            SaveSettings();
            if (Layout == PanelLayout.Focus)
            {
                LayoutChanged?.Invoke();
            }
        }
    }

    public PanelLayout Layout
    {
        get => _layout;
        set
        {
            if (Set(ref _layout, value))
            {
                Raise(nameof(IsFocusLayout));
                Raise(nameof(IsSideBySideLayout));
                Raise(nameof(IsGridLayout));
                SaveSettings();
                LayoutChanged?.Invoke();
                ReturnFocusToGame?.Invoke();
            }
        }
    }

    public bool IsFocusLayout => Layout == PanelLayout.Focus;
    public bool IsSideBySideLayout => Layout == PanelLayout.SideBySide;
    public bool IsGridLayout => Layout == PanelLayout.Grid;

    public bool IsRecording
    {
        get => _isRecording;
        private set
        {
            if (Set(ref _isRecording, value))
            {
                Raise(nameof(CanChangeSlots));
                Raise(nameof(StartStopText));
                Raise(nameof(StatusTitle));
            }
        }
    }

    public bool CanChangeSlots => !IsRecording;
    public int OpenCount => Slots.Count(s => s.IsOpen);
    public string WindowTitle => $"Fourvale Capture · {ActiveSlot.Name}";
    public string StartStopText => IsRecording ? "Stop capture" : OpenCount > 1 ? $"Start capture ({OpenCount} slots)" : "Start capture";

    public string StatusTitle => IsRecording
        ? OpenCount > 1 ? $"Recording {Slots.Count(s => s.IsRecording)} slots" : "Recording"
        : "Not recording";

    public bool ReloadOnStart
    {
        get => _reloadOnStart;
        set
        {
            if (Set(ref _reloadOnStart, value))
            {
                SaveSettings();
            }
        }
    }

    public string NoteText
    {
        get => _noteText;
        set => Set(ref _noteText, value);
    }

    public string LastMarker
    {
        get => _lastMarker;
        private set => Set(ref _lastMarker, value);
    }

    /// <summary>Opens the slots that were open last time.</summary>
    public void RestoreSession()
    {
        var toOpen = _settings.OpenSlots.Where(n => Slots.Any(s => s.Number == n)).Distinct().ToList();
        if (toOpen.Count == 0)
        {
            toOpen.Add(ActiveSlot.Number);
        }

        foreach (var slot in Slots.Where(s => toOpen.Contains(s.Number)))
        {
            Open(slot);
        }

        if (!ActiveSlot.IsOpen)
        {
            ActiveSlot = Slots.First(s => s.IsOpen);
        }

        LayoutChanged?.Invoke();
    }

    public void Activate(SlotViewModel slot)
    {
        if (slot.IsOpen)
        {
            ActiveSlot = slot;
        }
    }

    private void OnSlotPill(SlotViewModel? slot)
    {
        if (slot is null)
        {
            return;
        }

        if (!slot.IsOpen)
        {
            if (IsRecording)
            {
                return; // Opening a slot mid-recording would give it a partial capture.
            }

            Open(slot);
            ActiveSlot = slot;
            LayoutChanged?.Invoke();
            return;
        }

        ActiveSlot = slot;
        ReturnFocusToGame?.Invoke();
    }

    private void Open(SlotViewModel slot)
    {
        if (slot.IsOpen)
        {
            return;
        }

        slot.IsOpen = true;
        slot.GameStatus = "Starting…";
        SlotOpened?.Invoke(slot);
        RaiseSlotsChanged();
    }

    private void CloseSlot(SlotViewModel? slot)
    {
        if (slot is null || !slot.IsOpen || IsRecording)
        {
            return;
        }

        slot.IsOpen = false;
        slot.IsReady = false;
        slot.GameStatus = "Closed";
        SlotClosed?.Invoke(slot);

        if (ReferenceEquals(slot, ActiveSlot) && Slots.FirstOrDefault(s => s.IsOpen) is { } next)
        {
            ActiveSlot = next;
        }

        RaiseSlotsChanged();
        LayoutChanged?.Invoke();
    }

    private async Task ToggleRecordingAsync()
    {
        _isBusy = true;
        try
        {
            if (IsRecording)
            {
                await StopAsync();
            }
            else
            {
                StartAll();
            }
        }
        finally
        {
            _isBusy = false;
            CommandManager.InvalidateRequerySuggested();
            ReturnFocusToGame?.Invoke();
        }
    }

    /// <summary>Set by the window: returns the observer state (bundles, open sockets) for a slot.</summary>
    public Func<SlotViewModel, (IReadOnlyList<string> Bundles, IReadOnlyDictionary<string, string> Sockets)>? ObserverState { get; set; }

    private void StartAll()
    {
        var startedAt = DateTimeOffset.Now;
        foreach (var slot in Slots.Where(s => s.IsOpen && s.IsReady))
        {
            var state = ObserverState?.Invoke(slot) ?? ([], new Dictionary<string, string>());
            slot.Capture.Start(CaptureFolder, startedAt, _rules.GameUrl, state.Bundles, state.Sockets);
            if (ReloadOnStart)
            {
                ReloadRequested?.Invoke(slot);
            }
        }

        LastMarker = "";
        IsRecording = true;
        Refresh();
    }

    public async Task StopAsync()
    {
        foreach (var slot in Slots)
        {
            await slot.Capture.StopAsync();
        }

        IsRecording = false;
        Refresh();
    }

    private void AddMarker(string? label)
    {
        var clean = label is null ? null : ActiveSlot.Capture.AddMarker(label);
        if (clean is not null)
        {
            var count = ActiveSlot.Capture.Markers;
            LastMarker = $"✓ {clean} → {ActiveSlot.Name} at {ActiveSlot.Elapsed} ({count} marker{(count == 1 ? "" : "s")})";
        }

        ReturnFocusToGame?.Invoke();
    }

    private void AddNote()
    {
        AddMarker(NoteText);
        NoteText = "";
    }

    private void OpenFolder()
    {
        Directory.CreateDirectory(CaptureFolder);
        Process.Start(new ProcessStartInfo { FileName = CaptureFolder, UseShellExecute = true });
        ReturnFocusToGame?.Invoke();
    }

    private void RaiseSlotsChanged()
    {
        Raise(nameof(OpenCount));
        Raise(nameof(StartStopText));
        Raise(nameof(StatusTitle));
        SaveSettings();
    }

    private void Refresh()
    {
        foreach (var slot in Slots.Where(s => s.IsOpen || s.IsRecording))
        {
            slot.Refresh();
        }

        Raise(nameof(StatusTitle));
    }

    private void SaveSettings()
    {
        _settings.OpenSlots = Slots.Where(s => s.IsOpen).Select(s => s.Number).ToList();
        _settings.ActiveSlot = ActiveSlot.Number;
        _settings.Layout = Layout;
        _settings.ReloadOnStart = ReloadOnStart;
        _settings.Save(_settingsPath);
    }
}
