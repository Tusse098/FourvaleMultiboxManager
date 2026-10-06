using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Multibox.Hosting;

/// <summary>
/// Tells the host whether a text field (chat, login form) has focus in a slot's page, so plain-key shortcuts
/// such as <c>1</c> are not taken while the player is typing. ADR 0005.
/// <para>
/// Read-only page script (spec §7.1 option 2, hard rule 5): it listens for focus changes, re-checks twice a second,
/// and posts one boolean when it changes.
/// It does not read game objects, call game functions, change the page or affect the game's own handling of input.
/// </para>
/// </summary>
public sealed class TypingWatcher
{
    public const string Script = """
        (() => {
          if (window.top !== window || !window.chrome || !window.chrome.webview) return;
          let last = null;
          const report = (force) => {
            const el = document.activeElement;
            const typing = !!el && el.isConnected && (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.isContentEditable === true);
            if (typing === last && force !== true) return;
            last = typing;
            window.chrome.webview.postMessage({ mbx: 'typing', value: typing });
          };
          // Real focus changes always report, so a field focused after the player pressed "Unstick keys" counts again.
          document.addEventListener('focusin', () => report(true), true);
          document.addEventListener('focusout', () => queueMicrotask(() => report(true)), true);
          // A focused text field that is removed from the page (e.g. chat closing) fires no focusout,
          // so the state is also re-checked twice a second. Reads document.activeElement only.
          setInterval(report, 500);
        })();
        """;

    private readonly string _allowedHost;

    public TypingWatcher(string gameUrl) => _allowedHost = new Uri(gameUrl).Host;

    /// <summary>A text field in this page has focus right now.</summary>
    public bool IsTyping { get; private set; }

    /// <summary>Forget the typing state (player pressed "Unstick keys"). The next real focus change reports again.</summary>
    public void Clear() => IsTyping = false;

    public async Task AttachAsync(CoreWebView2 core)
    {
        await core.AddScriptToExecuteOnDocumentCreatedAsync(Script);
        core.WebMessageReceived += OnMessage;
        core.NavigationStarting += (_, _) => IsTyping = false;
    }

    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // Only the game's own top-level page, and only the exact message shape this script sends.
        if (!Uri.TryCreate(e.Source, UriKind.Absolute, out var source) || !string.Equals(source.Host, _allowedHost, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("mbx", out var kind) && kind.ValueKind == JsonValueKind.String && kind.GetString() == "typing"
                && doc.RootElement.TryGetProperty("value", out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                IsTyping = value.GetBoolean();
            }
        }
        catch (JsonException)
        {
            // Not ours.
        }
    }
}
