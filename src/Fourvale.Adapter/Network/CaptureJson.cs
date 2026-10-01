using System.Text.Encodings.Web;
using System.Text.Json;

namespace Fourvale.Adapter.Network;

public static class CaptureJson
{
    /// <summary>Capture files are read by people, not embedded in HTML, so keep "<", "'" and non-ASCII readable.</summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
