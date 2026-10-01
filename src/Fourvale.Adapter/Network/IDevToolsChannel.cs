namespace Fourvale.Adapter.Network;

/// <summary>
/// The part of the Chrome DevTools Protocol the adapter needs: subscribe to events and call read-only methods.
/// Implemented over WebView2 in Multibox.Hosting; faked in tests.
/// </summary>
public interface IDevToolsChannel
{
    /// <summary>Calls <paramref name="handler"/> with each event's parameters as JSON. Dispose to unsubscribe.</summary>
    IDisposable Subscribe(string eventName, Action<string> handler);

    /// <summary>Calls a DevTools method and returns its result as JSON.</summary>
    Task<string> CallAsync(string method, string parametersJson);
}
