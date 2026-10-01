using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Multibox.Hosting;

/// <summary>Creates and configures the WebView2 control for one slot.</summary>
public static class SlotBrowser
{
    /// <summary>
    /// One profile per slot (spec §5.2): separate cookies and storage, shared browser process.
    /// <paramref name="browserArguments"/> must be identical for every slot, because the profiles share that process.
    /// </summary>
    public static WebView2 Create(string userDataFolder, string profileName, string browserArguments) => new()
    {
        CreationProperties = new CoreWebView2CreationProperties
        {
            UserDataFolder = userDataFolder,
            ProfileName = profileName,
            AdditionalBrowserArguments = browserArguments,
        },
        DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x0F, 0x0F, 0x1A),
    };

    /// <summary>
    /// Visual-hosting variant for the app: the page is rendered into the WPF visual tree instead of a native child
    /// window, so a panel can be scaled like an image without resizing the page (instant, flicker-free layout swaps).
    /// Same profile rules as <see cref="Create"/>.
    /// </summary>
    public static WebView2CompositionControl CreateComposited(string userDataFolder, string profileName, string browserArguments) => new()
    {
        CreationProperties = new CoreWebView2CreationProperties
        {
            UserDataFolder = userDataFolder,
            ProfileName = profileName,
            AdditionalBrowserArguments = browserArguments,
        },
        DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x0F, 0x0F, 0x1A),
    };

    /// <summary>
    /// Game-panel settings: no status bar, and no browser shortcuts (F3 find, F5 reload, Ctrl+F, …) so they
    /// cannot pop up over the game (discovery R3). The game's own keys are unaffected.
    /// </summary>
    public static void Configure(CoreWebView2 core)
    {
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
    }
}
