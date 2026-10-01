using System.IO;
using System.Text.Json;

namespace Fourvale.Capture.Ui;

public enum PanelLayout
{
    Focus,
    SideBySide,
    Grid,
}

/// <summary>Which slots were open and how they were arranged. Never contains game or account data.</summary>
public sealed class ToolSettings
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public List<int> OpenSlots { get; set; } = [1];
    public int ActiveSlot { get; set; } = 1;
    public PanelLayout Layout { get; set; } = PanelLayout.Grid;
    public bool ReloadOnStart { get; set; } = true;

    public static ToolSettings Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<ToolSettings>(File.ReadAllText(path), Options) ?? new ToolSettings()
                : new ToolSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new ToolSettings();
        }
    }

    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience; failing to save them must not affect the session.
        }
    }
}
