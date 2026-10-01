using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Multibox.Hosting;

/// <summary>
/// Tells the host whether a text field (chat, login form) has focus in a slot's page, so plain-key shortcuts
/// such as <c>1</c> or <c>Space</c> are not taken while the player is typing. ADR 0005.
/// <para>
/// Read-only page script (spec §7.1 option 2, hard rule 5): it listens for focus changes and posts one boolean.
/// It does not read game objects, call game functions, change the page or affect the game's own handling of input.
/// </para>
/// </summary>
public sealed class TypingWatcher
{
    public const string Script = """
        (() => {
          if (window.top !== window || !window.chrome || !window.chrome.webview) return;
          const report = () => {
            const el = document.activeElement;
            const typing = !!el && (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.isContentEditable === true);
            window.chrome.webview.postMessage({ mbx: 'typing', value: typing });
          };
          document.addEventListener('focusin', report, true);
          document.addEventListener('focusout', () => queueMicrotask(report), true);
        })();
        """;

    private readonly string _allowedHost;

    public TypingWatcher(string gameUrl) => _allowedHost = new Uri(gameUrl).Host;

    /// <summary>A text field in this page has focus right now.</summary>
    public bool IsTyping { get; private set; }

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
