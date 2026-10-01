using Fourvale.Adapter.Network;
using Microsoft.Web.WebView2.Core;

namespace Multibox.Hosting;

/// <summary>DevTools protocol access for the adapter over one WebView2 instance. Events arrive on the UI thread.</summary>
public sealed class WebView2DevToolsChannel(CoreWebView2 core) : IDevToolsChannel
{
    public IDisposable Subscribe(string eventName, Action<string> handler)
    {
        var receiver = core.GetDevToolsProtocolEventReceiver(eventName);
        EventHandler<CoreWebView2DevToolsProtocolEventReceivedEventArgs> callback = (_, e) => handler(e.ParameterObjectAsJson);
        receiver.DevToolsProtocolEventReceived += callback;
        return new Unsubscriber(() => receiver.DevToolsProtocolEventReceived -= callback);
    }

    public Task<string> CallAsync(string method, string parametersJson) =>
        core.CallDevToolsProtocolMethodAsync(method, parametersJson);

    private sealed class Unsubscriber(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
