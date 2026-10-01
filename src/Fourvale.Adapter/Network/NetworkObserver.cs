using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fourvale.Adapter.Network;

public enum CaptureDirection
{
    In,
    Out,
    Http,
    Socket,
}

/// <summary>One observed network event, already redacted.</summary>
public sealed record CaptureEvent(
    CaptureDirection Direction,
    string Label,
    JsonObject Record,
    int Redactions,
    bool Dropped,
    bool DecodeFailed);

/// <summary>
/// Passive network observation through the DevTools protocol (spec §7.1 option 1).
/// Only enables the Network domain and reads events. Never injects script, never sends
/// game input, never reads request headers or request bodies.
/// </summary>
public sealed class NetworkObserver : IDisposable
{
    private static readonly string[] EventNames =
    [
        "Network.webSocketCreated",
        "Network.webSocketClosed",
        "Network.webSocketFrameReceived",
        "Network.webSocketFrameSent",
        "Network.webSocketFrameError",
        "Network.responseReceived",
        "Network.loadingFinished",
        "Network.loadingFailed",
    ];

    private readonly IDevToolsChannel _channel;
    private readonly AdapterRules _rules;
    private readonly Redactor _redactor;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly Dictionary<string, string> _sockets = [];
    private readonly Dictionary<string, PendingResponse> _pending = [];
    private readonly List<string> _bundles = [];

    private sealed record PendingResponse(string Url, string Path, int Status, string MimeType);

    public NetworkObserver(IDevToolsChannel channel, AdapterRules rules, Redactor redactor)
    {
        _channel = channel;
        _rules = rules;
        _redactor = redactor;
    }

    public event Action<CaptureEvent>? Captured;

    /// <summary>Sanitised URLs of currently open WebSockets, keyed by DevTools request id.</summary>
    public IReadOnlyDictionary<string, string> OpenSockets => _sockets;

    /// <summary>Sanitised script URLs from the game host; the hashed names identify the Fourvale build.</summary>
    public IReadOnlyList<string> Bundles => _bundles;

    public async Task StartAsync()
    {
        foreach (var name in EventNames)
        {
            _subscriptions.Add(_channel.Subscribe(name, json => OnEvent(name, json)));
        }

        await _channel.CallAsync("Network.enable", "{}");
    }

    /// <summary>Clears per-page state after a navigation or reload.</summary>
    public void ResetPageState()
    {
        _sockets.Clear();
        _pending.Clear();
        _bundles.Clear();
    }

    private void OnEvent(string name, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var p = doc.RootElement;
            switch (name)
            {
                case "Network.webSocketCreated": OnSocketCreated(p); break;
                case "Network.webSocketClosed": OnSocketClosed(p); break;
                case "Network.webSocketFrameReceived": OnFrame(p, CaptureDirection.In); break;
                case "Network.webSocketFrameSent": OnFrame(p, CaptureDirection.Out); break;
                case "Network.webSocketFrameError": OnFrameError(p); break;
                case "Network.responseReceived": OnResponse(p); break;
                case "Network.loadingFinished": _ = OnLoadingFinishedAsync(p.GetProperty("requestId").GetString() ?? ""); break;
                case "Network.loadingFailed": _pending.Remove(p.GetProperty("requestId").GetString() ?? ""); break;
            }
        }
        catch (Exception ex)
        {
            // Never let a malformed event break the session. Record the failure, not the payload.
            Emit(CaptureDirection.Socket, "observer error",
                new JsonObject { ["kind"] = "observer_error", ["event"] = name, ["error"] = ex.GetType().Name },
                0, dropped: false, decodeFailed: true);
        }
    }

    private void OnSocketCreated(JsonElement p)
    {
        var id = p.GetProperty("requestId").GetString() ?? "";
        var redactions = 0;
        var url = _redactor.SanitizeUrl(p.GetProperty("url").GetString() ?? "", ref redactions);
        _sockets[id] = url;
        Emit(CaptureDirection.Socket, "socket opened",
            new JsonObject { ["kind"] = "ws_open", ["socket"] = id, ["url"] = url }, redactions, false, false);
    }

    private void OnSocketClosed(JsonElement p)
    {
        var id = p.GetProperty("requestId").GetString() ?? "";
        _sockets.Remove(id);
        Emit(CaptureDirection.Socket, "socket closed",
            new JsonObject { ["kind"] = "ws_close", ["socket"] = id }, 0, false, false);
    }

    private void OnFrameError(JsonElement p)
    {
        var redactions = 0;
        var message = _redactor.RedactString(p.TryGetProperty("errorMessage", out var m) ? m.GetString() ?? "" : "", ref redactions);
        Emit(CaptureDirection.Socket, "socket error",
            new JsonObject { ["kind"] = "ws_error", ["socket"] = p.GetProperty("requestId").GetString(), ["error"] = message },
            redactions, false, false);
    }

    private void OnFrame(JsonElement p, CaptureDirection direction)
    {
        var id = p.GetProperty("requestId").GetString() ?? "";
        var response = p.GetProperty("response");
        var opcode = response.GetProperty("opcode").GetInt32();
        var payload = response.TryGetProperty("payloadData", out var d) ? d.GetString() ?? "" : "";

        var record = new JsonObject
        {
            ["kind"] = "ws",
            ["dir"] = direction == CaptureDirection.In ? "in" : "out",
            ["socket"] = id,
        };

        switch (opcode)
        {
            case 2:
            {
                var bytes = Convert.FromBase64String(payload);
                var result = ColyseusFrameDecoder.Decode(bytes, _redactor);
                foreach (var (key, value) in result.Record.ToList())
                {
                    result.Record.Remove(key);
                    record[key] = value;
                }

                Emit(direction, result.Label, record, result.Redactions, false, result.DecodeFailed);
                return;
            }

            case 1:
            {
                var redactions = 0;
                record["opcode"] = 1;
                JsonNode? parsed = null;
                try
                {
                    parsed = JsonNode.Parse(payload);
                }
                catch (JsonException)
                {
                    // Free text cannot be redacted reliably, so it is not stored.
                }

                if (parsed is null)
                {
                    record["textOmitted"] = true;
                    record["length"] = payload.Length;
                    Emit(direction, "text frame (omitted)", record, 0, true, false);
                }
                else
                {
                    record["json"] = _redactor.Redact(parsed, false, ref redactions);
                    Emit(direction, "text frame", record, redactions, false, false);
                }

                return;
            }

            default:
                record["opcode"] = opcode;
                Emit(direction, $"control frame {opcode}", record, 0, false, false);
                return;
        }
    }

    private void OnResponse(JsonElement p)
    {
        var id = p.GetProperty("requestId").GetString() ?? "";
        var type = p.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
        var response = p.GetProperty("response");
        var rawUrl = response.GetProperty("url").GetString() ?? "";
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
        {
            return;
        }

        var redactions = 0;
        var url = _redactor.SanitizeUrl(rawUrl, ref redactions);

        if (type == "Script" && HostMatches(uri.Host, _rules.BundleHosts))
        {
            if (!_bundles.Contains(url))
            {
                _bundles.Add(url);
            }

            return;
        }

        if (type is not ("XHR" or "Fetch") || !HostMatches(uri.Host, _rules.ApiHosts))
        {
            return;
        }

        var status = response.TryGetProperty("status", out var s) ? s.GetInt32() : 0;
        var mime = response.TryGetProperty("mimeType", out var m) ? m.GetString() ?? "" : "";

        if (_redactor.IsAuthPath(uri.AbsolutePath))
        {
            // Auth responses carry tokens: the body is never requested.
            Emit(CaptureDirection.Http, $"{uri.AbsolutePath} (auth, dropped)",
                new JsonObject
                {
                    ["kind"] = "http",
                    ["url"] = $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}",
                    ["status"] = status,
                    ["dropped"] = "auth",
                },
                0, dropped: true, decodeFailed: false);
            return;
        }

        _pending[id] = new PendingResponse(url, uri.AbsolutePath, status, mime);
    }

    private async Task OnLoadingFinishedAsync(string requestId)
    {
        if (!_pending.Remove(requestId, out var pending))
        {
            return;
        }

        var record = new JsonObject
        {
            ["kind"] = "http",
            ["url"] = pending.Url,
            ["status"] = pending.Status,
            ["mimeType"] = pending.MimeType,
        };
        var redactions = 0;
        var failed = false;

        try
        {
            var resultJson = await _channel.CallAsync(
                "Network.getResponseBody", new JsonObject { ["requestId"] = requestId }.ToJsonString());
            using var doc = JsonDocument.Parse(resultJson);
            var body = doc.RootElement.GetProperty("body").GetString() ?? "";
            var base64 = doc.RootElement.TryGetProperty("base64Encoded", out var b) && b.GetBoolean();

            JsonNode? parsed = null;
            if (!base64)
            {
                try
                {
                    parsed = JsonNode.Parse(body);
                }
                catch (JsonException)
                {
                }
            }

            if (parsed is null)
            {
                record["bodyOmitted"] = base64 ? "binary" : "not JSON";
                record["length"] = body.Length;
            }
            else
            {
                record["body"] = _redactor.Redact(parsed, false, ref redactions);
            }
        }
        catch (Exception ex)
        {
            record["bodyError"] = ex.GetType().Name;
            failed = true;
        }

        Emit(CaptureDirection.Http, pending.Path, record, redactions, false, failed);
    }

    private static bool HostMatches(string host, IReadOnlyList<string> hosts) =>
        hosts.Any(h => string.Equals(h, host, StringComparison.OrdinalIgnoreCase));

    private void Emit(CaptureDirection direction, string label, JsonObject record, int redactions, bool dropped, bool decodeFailed) =>
        Captured?.Invoke(new CaptureEvent(direction, label, record, redactions, dropped, decodeFailed));

    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        Captured = null;
    }
}
