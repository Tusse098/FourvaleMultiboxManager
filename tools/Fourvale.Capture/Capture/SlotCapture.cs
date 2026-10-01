using System.Diagnostics;
using System.Text.Json.Nodes;

using Fourvale.Adapter.Network;
namespace Fourvale.Capture.Capture;

/// <summary>
/// Everything captured for one slot: counters, message-type tally and its own capture file.
/// Each slot's observer feeds only its own SlotCapture, so data from one account can never
/// end up in another account's file.
/// </summary>
public sealed class SlotCapture(string profileName, Redactor redactor)
{
    private readonly Dictionary<string, (string Glyph, string Label, int Count)> _tally = [];
    private readonly Stopwatch _clock = new();
    private CaptureWriter? _writer;

    public string ProfileName { get; } = profileName;

    public int Received { get; private set; }
    public int Sent { get; private set; }
    public int Http { get; private set; }
    public int Redacted { get; private set; }
    public int Dropped { get; private set; }
    public int Undecoded { get; private set; }
    public int Markers { get; private set; }

    public IReadOnlyDictionary<string, (string Glyph, string Label, int Count)> Tally => _tally;
    public bool IsRecording => _writer is not null;
    public string? FilePath => _writer?.FilePath;
    public string? LastFilePath { get; private set; }
    public long BytesWritten => _writer?.BytesWritten ?? 0;
    public int RecordCount => _writer?.RecordCount ?? 0;
    public TimeSpan Elapsed => _clock.Elapsed;

    public void Start(
        string folder,
        DateTimeOffset startedAt,
        string gameUrl,
        IReadOnlyList<string> bundles,
        IReadOnlyDictionary<string, string> openSockets)
    {
        if (_writer is not null)
        {
            return;
        }

        Reset();
        _clock.Restart();
        _writer = CaptureWriter.Create(folder, ProfileName, startedAt);

        _writer.Write(Stamp(new JsonObject
        {
            ["kind"] = "header",
            ["tool"] = "Fourvale.Capture",
            ["toolVersion"] = typeof(SlotCapture).Assembly.GetName().Version?.ToString(),
            ["capturedAt"] = startedAt.ToString("O"),
            ["slot"] = ProfileName,
            ["gameUrl"] = gameUrl,
            ["bundles"] = new JsonArray(bundles.Select(b => (JsonNode?)JsonValue.Create(b)).ToArray()),
            ["note"] = "Redacted at capture time. Still contains other players' names and session ids: sanitise before committing as a fixture.",
        }));

        foreach (var (id, url) in openSockets)
        {
            _writer.Write(Stamp(new JsonObject { ["kind"] = "ws_open", ["socket"] = id, ["url"] = url, ["alreadyOpen"] = true }));
        }
    }

    public async Task StopAsync()
    {
        if (_writer is null)
        {
            return;
        }

        _writer.Write(Stamp(new JsonObject { ["kind"] = "footer", ["durationMs"] = _clock.ElapsedMilliseconds }));
        var writer = _writer;
        _writer = null;
        _clock.Stop();
        await writer.DisposeAsync();
        LastFilePath = writer.FilePath;
    }

    public void OnCaptured(CaptureEvent e)
    {
        switch (e.Direction)
        {
            case CaptureDirection.In: Received++; break;
            case CaptureDirection.Out: Sent++; break;
            case CaptureDirection.Http: Http++; break;
        }

        Redacted += e.Redactions;
        if (e.Dropped) Dropped++;
        if (e.DecodeFailed) Undecoded++;

        var glyph = e.Direction switch
        {
            CaptureDirection.In => "↓",
            CaptureDirection.Out => "↑",
            CaptureDirection.Http => "⇅",
            _ => "•",
        };
        var key = glyph + e.Label;
        _tally[key] = _tally.TryGetValue(key, out var entry) ? entry with { Count = entry.Count + 1 } : (glyph, e.Label, 1);

        _writer?.Write(Stamp(e.Record));
    }

    /// <summary>Writes a marker and returns the cleaned label, or null when not recording.</summary>
    public string? AddMarker(string label)
    {
        if (_writer is null || string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        var redactions = 0;
        var clean = redactor.RedactString(label.Trim(), ref redactions);
        _writer.Write(Stamp(new JsonObject { ["kind"] = "marker", ["label"] = clean }));
        Markers++;
        return clean;
    }

    private void Reset()
    {
        Received = Sent = Http = Redacted = Dropped = Undecoded = Markers = 0;
        _tally.Clear();
    }

    private JsonObject Stamp(JsonObject record)
    {
        var stamped = new JsonObject
        {
            ["t"] = DateTimeOffset.UtcNow.ToString("O"),
            ["ms"] = _clock.ElapsedMilliseconds,
        };
        // Copy rather than move: other listeners (the live state) read the same record.
        foreach (var (key, value) in record)
        {
            stamped[key] = value?.DeepClone();
        }

        return stamped;
    }
}
