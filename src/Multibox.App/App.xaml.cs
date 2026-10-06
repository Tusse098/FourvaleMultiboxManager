using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Fourvale.Adapter;
using Fourvale.Adapter.Network;
using Multibox.Core;

namespace Multibox.App;

/// <summary>
/// Catches errors nothing else handled, so the app never just disappears: the player gets a message that says where
/// the log is, and the error goes to <c>crash-yyyyMMdd.log</c>. A failure during start (e.g. a broken multibox.json)
/// closes the app after the message; a later one keeps the running games open.
/// </summary>
public partial class App : Application
{
    private static readonly string LogFolder = Path.Combine(AppConfig.DataRoot, "logs");
    private readonly object _gate = new();
    private Log? _crashLog;
    private DateTimeOffset _lastDialog = DateTimeOffset.MinValue;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                Record(ex, "background error (the app closes)");
                Tell(ex, closing: true);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Record(args.Exception, "unobserved task error");
            args.SetObserved(); // already logged; must not take the app down
        };

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        lock (_gate)
        {
            _crashLog?.Dispose();
        }

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var starting = MainWindow is not { IsLoaded: true };
        Record(e.Exception, starting ? "error while starting (the app closes)" : "error (the app keeps running)");
        e.Handled = true;
        Tell(e.Exception, closing: starting);
        if (starting)
        {
            Shutdown(1);
        }
    }

    /// <summary>
    /// Through the one logging wrapper, with redaction. Only the type and stack trace are written: exception messages
    /// can echo game data (Log.Error rule). Our own settings errors are the exception; their text is ours.
    /// </summary>
    private void Record(Exception error, string what)
    {
        var ex = Cause(error);
        try
        {
            lock (_gate)
            {
                _crashLog ??= new Log(Path.Combine(LogFolder, $"crash-{DateTime.Now:yyyyMMdd}.log"), CreateRedaction());
                var detail = IsSettingsError(ex) ? $": {ex.Message}" : "";
                _crashLog.Write(LogLevel.Error, $"{what}: {ex.GetType().FullName}{detail}{Environment.NewLine}{ex.StackTrace}");
            }
        }
        catch (Exception logFailure) when (logFailure is IOException or UnauthorizedAccessException)
        {
            // The log folder is not writable; the message box still tells the player.
        }
    }

    private void Tell(Exception error, bool closing)
    {
        var ex = Cause(error);
        // At most one box every few seconds, so a repeating error cannot bury the screen in dialogs.
        var now = DateTimeOffset.UtcNow;
        if (!closing && now - _lastDialog < TimeSpan.FromSeconds(10))
        {
            return;
        }

        _lastDialog = now;
        var reason = IsSettingsError(ex)
            ? $"A settings file could not be read:\n{ex.Message}\n\nFix or delete multibox.json (next to the app) or app-settings.json (in %AppData%\\FourvaleMultibox)."
            : "Something went wrong inside the app.";
        var next = closing
            ? "The app will close."
            : "Your games are still running. If something looks wrong, restart the app.";
        var text = $"{reason}\n\n{next}\n\nDetails were saved in:\n{LogFolder}\nPlease include the newest crash log when you report a bug (it contains no passwords or game data).";

        try
        {
            MessageBox.Show(text, "Fourvale Multibox Manager", MessageBoxButton.OK, closing ? MessageBoxImage.Error : MessageBoxImage.Warning);
        }
        catch (InvalidOperationException)
        {
            // No UI thread left to show it on (shutting down); the log has it.
        }
    }

    private static bool IsSettingsError(Exception ex) => ex is InvalidDataException or JsonException;

    /// <summary>
    /// The error that actually happened: WPF wraps errors from the main window's constructor in a XamlParseException,
    /// and reflection wraps others in a TargetInvocationException.
    /// </summary>
    private static Exception Cause(Exception ex)
    {
        var current = ex;
        while (current is System.Windows.Markup.XamlParseException or System.Reflection.TargetInvocationException or AggregateException
               && current.InnerException is { } inner)
        {
            current = inner;
        }

        return current;
    }

    private static Func<string, string> CreateRedaction()
    {
        try
        {
            var redactor = new Redactor(AdapterRules.LoadDefault());
            return message => { var n = 0; return redactor.RedactString(message, ref n); };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            return message => message; // only type names and stack traces are written; nothing from the game
        }
    }
}
