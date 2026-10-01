using System.Text.Json.Nodes;
using Fourvale.Adapter.Network;
using Fourvale.Capture.Capture;

namespace Fourvale.Capture.Tests;

public class SlotCaptureTests
{
    private static CaptureEvent Event(JsonObject record) => new(CaptureDirection.In, "test", record, 0, false, false);

    [Fact]
    public async Task Recording_does_not_strip_the_record_other_listeners_read()
    {
        var folder = Path.Combine(Path.GetTempPath(), "fourvale-capture-tests", Guid.NewGuid().ToString("N"));
        var capture = new SlotCapture("slot1", TestSupport.NewRedactor());
        capture.Start(folder, DateTimeOffset.Now, "https://fourvale.com/", [], new Dictionary<string, string>());
        var record = new JsonObject { ["kind"] = "ws_open", ["socket"] = "s1", ["url"] = "wss://h/p/r1" };

        capture.OnCaptured(Event(record));
        await capture.StopAsync();

        Assert.Equal("s1", (string?)record["socket"]);
        Directory.Delete(folder, recursive: true);
    }
}
