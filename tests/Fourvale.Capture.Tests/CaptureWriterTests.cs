using System.Text;
using System.Text.Json.Nodes;
using Fourvale.Capture.Capture;
using Fourvale.Adapter.Network;

namespace Fourvale.Capture.Tests;

public class CaptureWriterTests
{
    [Fact]
    public async Task Writes_one_json_line_per_record()
    {
        var folder = Path.Combine(Path.GetTempPath(), "fourvale-capture-tests", Guid.NewGuid().ToString("N"));
        var writer = CaptureWriter.Create(folder, "slot1", DateTimeOffset.Now);

        writer.Write(new JsonObject { ["kind"] = "header" });
        writer.Write(new JsonObject { ["kind"] = "marker", ["label"] = "Combat start" });
        await writer.DisposeAsync();

        var lines = await File.ReadAllLinesAsync(writer.FilePath);
        Assert.Equal(2, lines.Length);
        Assert.Equal("Combat start", JsonNode.Parse(lines[1])!["label"]!.GetValue<string>());
        Assert.EndsWith("_slot1.jsonl", writer.FilePath);

        Directory.Delete(folder, recursive: true);
    }
}
