using System.Globalization;
using System.Text;

namespace Multibox.Core;

public enum LogLevel
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// The one logging wrapper (CLAUDE.md): every message passes through a redaction function before it is written.
/// Never log raw messages, credentials, cookies, tokens, chat or other players' data (spec §14).
/// </summary>
public sealed class Log : IDisposable
{
    private readonly Func<string, string> _redact;
    private readonly StreamWriter? _writer;
    private readonly object _gate = new();

    /// <param name="path">Log file, or null to log nowhere (tests).</param>
    /// <param name="redact">Applied to every message before writing.</param>
    public Log(string? path, Func<string, string> redact)
    {
        _redact = redact;
        if (path is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read), new UTF8Encoding(false))
            {
                AutoFlush = true,
            };
        }
    }

    public static Log None { get; } = new(null, s => s);

    public void Info(string message) => Write(LogLevel.Info, message);

    public void Warning(string message) => Write(LogLevel.Warning, message);

    /// <summary>Logs only the exception type, never its message: messages can echo payload data.</summary>
    public void Error(string message, Exception? exception = null) =>
        Write(LogLevel.Error, exception is null ? message : $"{message} ({exception.GetType().Name})");

    public void Write(LogLevel level, string message)
    {
        var line = string.Create(CultureInfo.InvariantCulture, $"{DateTimeOffset.Now:O} {level,-7} {_redact(message)}");
        lock (_gate)
        {
            _writer?.WriteLine(line);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
        }
    }
}
