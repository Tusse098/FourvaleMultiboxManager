using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Fourvale.Adapter.Network;

namespace Fourvale.Capture.Capture;

/// <summary>
/// Appends already-redacted records to a JSON Lines file on a background task.
/// This is the only place the tool writes captured data to disk.
/// </summary>
public sealed class CaptureWriter : IAsyncDisposable
{
    private readonly Channel<string> _lines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly StreamWriter _writer;
    private readonly Task _pump;
    private long _bytesWritten;
    private int _recordCount;

    private CaptureWriter(string filePath)
    {
        FilePath = filePath;
        _writer = new StreamWriter(new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
        _pump = Task.Run(PumpAsync);
    }

    public string FilePath { get; }
    public long BytesWritten => Interlocked.Read(ref _bytesWritten);
    public int RecordCount => Volatile.Read(ref _recordCount);

    public static CaptureWriter Create(string folder, string slotName, DateTimeOffset startedAt)
    {
        Directory.CreateDirectory(folder);
        var safeSlot = string.Concat(slotName.Where(char.IsLetterOrDigit)).ToLowerInvariant();
        var name = $"{startedAt.ToLocalTime():yyyy-MM-dd_HH-mm-ss}_{safeSlot}.jsonl";
        return new CaptureWriter(Path.Combine(folder, name));
    }

    public void Write(JsonObject record)
    {
        var line = record.ToJsonString(CaptureJson.Options);
        Interlocked.Increment(ref _recordCount);
        _lines.Writer.TryWrite(line);
    }

    private async Task PumpAsync()
    {
        var reader = _lines.Reader;
        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            while (reader.TryRead(out var line))
            {
                await _writer.WriteLineAsync(line).ConfigureAwait(false);
                Interlocked.Add(ref _bytesWritten, Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length);
            }

            await _writer.FlushAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lines.Writer.TryComplete();
        await _pump.ConfigureAwait(false);
        await _writer.DisposeAsync().ConfigureAwait(false);
    }
}
