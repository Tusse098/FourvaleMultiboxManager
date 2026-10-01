using System.Text.Json.Nodes;
using Fourvale.Adapter.Network;
using Fourvale.Capture.Capture;

namespace Fourvale.Capture.Tests;

/// <summary>Multi-session isolation: events for one slot must never show up in another slot's counters or file.</summary>
public class SlotIsolationTests
{
    [Fact]
    public async Task Events_for_one_slot_never_reach_another_slot()
    {
        var folder = Path.Combine(Path.GetTempPath(), "fourvale-capture-tests", Guid.NewGuid().ToString("N"));
        var redactor = TestSupport.NewRedactor();
        var slot1 = new SlotCapture("slot1", redactor);
        var slot2 = new SlotCapture("slot2", redactor);
        var startedAt = DateTimeOffset.Now;

        slot1.Start(folder, startedAt, "https://fourvale.com/", [], new Dictionary<string, string>());
        slot2.Start(folder, startedAt, "https://fourvale.com/", [], new Dictionary<string, string>());

        var frame = new Pack(ColyseusFrameDecoder.RoomData).Str("hpSync").Map(1).Str("hp").Int(77).ToArray();
        var decoded = ColyseusFrameDecoder.Decode(frame, redactor);
        slot1.OnCaptured(new CaptureEvent(CaptureDirection.In, decoded.Label, decoded.Record, 0, false, false));
        slot1.AddMarker("Combat start");

        var file1 = slot1.FilePath!;
        var file2 = slot2.FilePath!;
        await slot1.StopAsync();
        await slot2.StopAsync();

        Assert.NotEqual(file1, file2);
        Assert.Equal(1, slot1.Received);
        Assert.Equal(0, slot2.Received);
        Assert.Empty(slot2.Tally);
        Assert.Equal(0, slot2.Markers);

        var lines1 = await File.ReadAllLinesAsync(file1);
        var lines2 = await File.ReadAllLinesAsync(file2);
        Assert.Contains(lines1, l => l.Contains("hpSync"));
        Assert.DoesNotContain(lines2, l => l.Contains("hpSync") || l.Contains("Combat start"));
        Assert.Equal("slot2", JsonNode.Parse(lines2[0])!["slot"]!.GetValue<string>());
        Assert.Equal(["header", "footer"], lines2.Select(l => JsonNode.Parse(l)!["kind"]!.GetValue<string>()));

        Directory.Delete(folder, recursive: true);
    }
}
