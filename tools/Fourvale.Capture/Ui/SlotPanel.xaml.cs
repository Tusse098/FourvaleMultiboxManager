using Fourvale.Adapter;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Fourvale.Adapter.Network;
using Fourvale.Capture.Capture;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Multibox.Hosting;

namespace Fourvale.Capture.Ui;

/// <summary>
/// Hosts one slot's WebView2 (its own profile) and that slot's network observer.
/// The observer feeds only this slot's <see cref="SlotCapture"/>.
/// </summary>
public partial class SlotPanel : UserControl
{
    private readonly SlotViewModel _slot;
    private readonly CaptureRules _rules;
    private readonly AdapterRules _adapterRules;
    private readonly Redactor _redactor;
    private WebView2? _webView;
    private NetworkObserver? _observer;

    public SlotPanel(SlotViewModel slot, CaptureRules rules, AdapterRules adapterRules, Redactor redactor)
    {
        InitializeComponent();
        _slot = slot;
        _rules = rules;
        _adapterRules = adapterRules;
        _redactor = redactor;
        DataContext = slot;

        Header.MouseLeftButtonDown += (_, _) => Activated?.Invoke(_slot);
    }

    /// <summary>Raised when the player clicks this panel's header or into its game view.</summary>
    public event Action<SlotViewModel>? Activated;

    public SlotViewModel Slot => _slot;
    public NetworkObserver? Observer => _observer;

    public async Task StartAsync(string userDataFolder)
    {
        var webView = SlotBrowser.Create(userDataFolder, _slot.ProfileName, _rules.BrowserArguments);
        _webView = webView;
        GameHost.Children.Add(webView);

        try
        {
            await webView.EnsureCoreWebView2Async();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            _slot.GameStatus = "Microsoft Edge WebView2 Runtime is not installed.";
            return;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or ObjectDisposedException)
        {
            _slot.GameStatus = $"Could not start ({ex.GetType().Name}).";
            return;
        }

        if (!ReferenceEquals(_webView, webView))
        {
            return; // Closed while starting.
        }

        var core = webView.CoreWebView2;
        SlotBrowser.Configure(core);
        core.ProcessFailed += (_, e) => _slot.GameStatus = $"Stopped ({e.ProcessFailedKind}). Press ⟳ to reload.";
        core.NavigationStarting += (_, _) =>
        {
            _observer?.ResetPageState();
            _slot.Live.Reset();
            _slot.GameStatus = "Loading…";
        };
        core.NavigationCompleted += (_, e) =>
            _slot.GameStatus = e.IsSuccess ? "Ready" : $"Could not load the game ({e.WebErrorStatus}).";

        // Clicking into the game focuses the WebView; treat that as choosing this slot.
        webView.GotFocus += (_, _) => Activated?.Invoke(_slot);

        _observer = new NetworkObserver(new WebView2DevToolsChannel(core), _adapterRules, _redactor);
        _observer.Captured += _slot.Live.OnCaptured;
        _observer.Captured += _slot.Capture.OnCaptured;
        await _observer.StartAsync();

        core.Navigate(_rules.GameUrl);
        _slot.IsReady = true;
    }

    public void Reload() => _webView?.CoreWebView2?.Reload();

    public void FocusGame() => _webView?.Focus();

    public void Shutdown()
    {
        _observer?.Dispose();
        _observer = null;

        if (_webView is not null)
        {
            GameHost.Children.Remove(_webView);
            _webView.Dispose();
            _webView = null;
        }
    }
}
