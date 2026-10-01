using System.IO;
using System.Text.Json;

using Fourvale.Adapter.Network;
namespace Fourvale.Capture.Capture;

/// <summary>
/// Capture-tool settings, loaded from capture-rules.json next to the executable.
/// Observation and redaction rules are the adapter's (fourvale-adapter.json, ADR 0003).
/// </summary>
public sealed class CaptureRules
{
    public string GameUrl { get; init; } = "https://fourvale.com/";
    public int SlotCount { get; init; } = 5;
    public IReadOnlyList<string> MarkerPresets { get; init; } = [];
    public int MessageTypeListSize { get; init; } = 40;

    /// <summary>
    /// Chromium switches that stop small or unfocused slots from being slowed down (spec §5.3).
    /// Must be identical for every slot, because all profiles share one browser process.
    /// </summary>
    public string BrowserArguments { get; init; } = "";

    /// <summary>Share of the game area height used by the strip of other slots in the Focus layout.</summary>
    public double FocusStripShare { get; init; } = 0.25;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static CaptureRules Load(string path) =>
        JsonSerializer.Deserialize<CaptureRules>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException("capture-rules.json is empty.");
}
