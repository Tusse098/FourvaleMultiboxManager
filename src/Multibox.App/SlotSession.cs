using System.IO;
using Fourvale.Adapter;
using Fourvale.Adapter.Network;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Multibox.Core;
using Multibox.Hosting;

namespace Multibox.App;

/// <summary>
/// One slot end to end: its WebView2 (own profile) → NetworkObserver → FourvaleSession (adapter) → StateStore.
/// Each slot has its own view, observer and session, so nothing can be attributed to another slot,
/// and a crash is recovered for this slot only (spec §5.3, §15 criterion 7).
/// </summary>
public sealed class SlotSession : IDisposable
{
    private readonly AppConfig _config;
    private readonly AdapterRules _rules;
    private readonly Redactor _redactor;
    private readonly StateStore _store;
    private readonly IsolationMonitor _isolation;
    private readonly Log _log;
    private readonly FourvaleSession _session = new();
    private NetworkObserver? _observer;
    private TypingWatcher? _typing;
    private AdapterHealth? _lastHealth;
    private bool _disposed;

    public SlotSession(SlotId id, AppConfig config, AdapterRules rules, Redactor redactor, StateStore store, IsolationMonitor isolation, Log log)
    {
        Id = id;
        _config = config;
        _rules = rules;
        _redactor = redactor;
        _store = store;
        _isolation = isolation;
        _log = log;
        View = CreateView();
    }

    public SlotId Id { get; }
    public WebView2CompositionControl View { get; private set; }
    public string Status { get; private set; } = "Starting…";

    /// <summary>A text field (chat, login) has focus in this slot's page: plain-key shortcuts must pass through.</summary>
    public bool IsTyping => _typing?.IsTyping ?? false;

    /// <summary>Clear a typing state that may be stuck; clicking into a text field sets it again.</summary>
    public void ClearTyping() => _typing?.Clear();


    /// <summary>Times this slot recovered from a crash (reload or rebuilt view).</summary>
    public int Recoveries { get; private set; }

    /// <summary>The slot's browser environment, once started (shared by all slots: one browser process tree).</summary>
    public CoreWebView2Environment? Environment => View.CoreWebView2?.Environment;

    public event Action? StatusChanged;

    /// <summary>Raised when the view had to be rebuilt; the window must swap the old control for <see cref="View"/>.</summary>
    public event Action<WebView2CompositionControl, WebView2CompositionControl>? ViewReplaced;

    // Visual hosting: the page renders into WPF, so panels can be scaled without resizing the game (instant swaps).
    private WebView2CompositionControl CreateView() => SlotBrowser.CreateComposited(AppConfig.WebViewDataFolder, Id.ToString(), _config.BrowserArguments);

    public async Task StartAsync()
    {
        SetBrowser(BrowserState.Loading, "Starting…");
        var view = View;
        try
        {
            await view.EnsureCoreWebView2Async();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            SetBrowser(BrowserState.Crashed, "Microsoft Edge WebView2 Runtime is not installed.");
            return;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            // Typically: the capture tool has the same profile folder open.
            SetBrowser(BrowserState.Crashed, "Could not start the game view. Is Fourvale Capture still running?");
            _log.Error($"{Id}: WebView2 start failed", ex);
            return;
        }

        if (_disposed || !ReferenceEquals(view, View))
        {
            return;
        }

        var core = view.CoreWebView2;
        SlotBrowser.Configure(core);
        _typing = new TypingWatcher(_config.GameUrl);
        await _typing.AttachAsync(core);
        core.NavigationStarting += (_, _) =>
        {
            // New page: old rooms and fields are no longer backed by anything; a new login may follow.
            _observer?.ResetPageState();
            _session.Reset();
            _store.Reset(Id);
            _isolation.Forget(Id);
            SetBrowser(BrowserState.Navigating, "Loading…");
        };
        core.NavigationCompleted += (_, e) =>
            SetBrowser(e.IsSuccess ? BrowserState.Ready : BrowserState.Crashed, e.IsSuccess ? "Ready" : $"Could not load the game ({e.WebErrorStatus})");
        core.ProcessFailed += OnProcessFailed;

        _observer?.Dispose();
        _observer = new NetworkObserver(new WebView2DevToolsChannel(core), _rules, _redactor);
        _observer.Captured += _session.OnCaptured;
        await _observer.StartAsync();

        core.Navigate(_config.GameUrl);
        _log.Info($"{Id}: started");
    }

    /// <summary>
    /// Renderer crash or hang: reload this slot only. Browser process exit: this slot's view is dead and is rebuilt.
    /// Other process failures (GPU, utility) restart on their own and are only logged.
    /// </summary>
    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        _log.Warning($"{Id}: process failed ({e.ProcessFailedKind}, {e.Reason})");
        switch (e.ProcessFailedKind)
        {
            case CoreWebView2ProcessFailedKind.RenderProcessExited:
            case CoreWebView2ProcessFailedKind.RenderProcessUnresponsive:
            case CoreWebView2ProcessFailedKind.FrameRenderProcessExited:
                Recoveries++;
                SetBrowser(BrowserState.Crashed, "Game view crashed. Reloading…");
                View.CoreWebView2?.Reload();
                break;

            case CoreWebView2ProcessFailedKind.BrowserProcessExited:
                Recoveries++;
                SetBrowser(BrowserState.Crashed, "Browser stopped. Restarting this slot…");
                _ = RebuildAsync();
                break;
        }
    }

    private async Task RebuildAsync()
    {
        var old = View;
        _observer?.Dispose();
        _observer = null;
        View = CreateView();
        ViewReplaced?.Invoke(old, View);
        old.Dispose();
        await StartAsync();
    }

    /// <summary>Adapter read: confirms fields into the store. Called on a timer on the UI thread.</summary>
    public void Read(DateTimeOffset now)
    {
        _store.Update(Id, state => _session.ReadInto(state, now));

        var state = _store.Get(Id);
        if (state.Health != _lastHealth)
        {
            _log.Info($"{Id}: adapter health {_lastHealth?.ToString() ?? "-"} -> {state.Health}, decode errors {state.DecodeErrors}");
            _lastHealth = state.Health;
        }
    }

    public void Reload() => View.CoreWebView2?.Reload();

    /// <summary>Edge's browser task manager: lets the player end one slot's process to test crash recovery.</summary>
    public void OpenTaskManager() => View.CoreWebView2?.OpenTaskManagerWindow();

    private void SetBrowser(BrowserState browser, string status)
    {
        _store.Update(Id, s => s.Browser = browser);
        Status = status;
        StatusChanged?.Invoke();
    }

    public void Dispose()
    {
        _disposed = true;
        _observer?.Dispose();
        View.Dispose();
    }
}
