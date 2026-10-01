using System.IO;
using System.Text.Json;
using Multibox.Core;

namespace Multibox.App;

/// <summary>Application settings from multibox.json. Thresholds live here, not in code (CLAUDE.md).</summary>
public sealed class AppConfig
{
    public string GameUrl { get; init; } = "https://fourvale.com/";
    /// <summary>Slots 1..SlotCount can be opened from the top bar; each gets its own WebView2 profile "slotN" (spec §5.2).</summary>
    public int SlotCount { get; init; } = 5;

    /// <summary>Slots opened on the very first start, before the player has chosen any.</summary>
    public List<int> DefaultSlots { get; init; } = [1];

    /// <summary>Identical for every slot: all profiles share one browser process.</summary>
    public string BrowserArguments { get; init; } = "";

    public int ReadIntervalMs { get; init; } = 250;
    public int MetricsIntervalMs { get; init; } = 2000;

    /// <summary>How often a row per slot is appended to the soak CSV (spec §15 criteria 2, 5, 8).</summary>
    public int SoakIntervalSeconds { get; init; } = 60;

    /// <summary>Keyboard shortcuts (spec §9). Validated by <see cref="ShortcutMap"/>.</summary>
    public ShortcutConfig Shortcuts { get; init; } = new();

    /// <summary>Game width / height. Fourvale renders 1920×1080 with Phaser Scale.FIT (bundle 0.98), so tiles use 16:9.</summary>
    public double GameAspect { get; init; } = 16.0 / 9.0;

    /// <summary>Largest width share of the column of small tiles in the Focus layout.</summary>
    public double FocusTileShare { get; init; } = 0.25;

    /// <summary>
    /// Focus layout: how long keyboard focus must stay on a slot before it becomes the large tile. Tapping quickly
    /// through slots then resizes once at the end instead of on every tap (each resize makes the games rescale).
    /// </summary>
    public int FocusSwapDelayMs { get; init; }

    /// <summary>Opacity of the party overlay's background (0.2–1); text stays fully opaque.</summary>
    public double OverlayOpacity { get; init; } = 0.82;

    /// <summary>How long a top-bar notice (e.g. "no character is ready") stays visible.</summary>
    public double NoticeSeconds { get; init; } = 2.5;

    /// <summary>Freshness limit per field source, e.g. "room-state": "00:00:05" (ADR 0003).</summary>
    public Dictionary<string, TimeSpan> Freshness { get; init; } = [];

    public static string DataRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FourvaleMultibox");

    /// <summary>Shared with the capture tool so slot logins carry over (ADR 0003).</summary>
    public static string WebViewDataFolder => Path.Combine(DataRoot, "WebView2");

    public FreshnessPolicy CreateFreshnessPolicy() => new(Freshness);

    public static AppConfig Load(string path)
    {
        var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        }) ?? throw new InvalidDataException("multibox.json is empty.");

        if (config.ReadIntervalMs <= 0 || config.MetricsIntervalMs <= 0 || config.SoakIntervalSeconds <= 0)
        {
            throw new InvalidDataException("multibox.json: intervals must be positive.");
        }

        if (config.GameAspect is < 0.5 or > 4)
        {
            throw new InvalidDataException("multibox.json: gameAspect must be between 0.5 and 4.");
        }

        if (config.FocusTileShare is <= 0.05 or >= 0.6 || config.NoticeSeconds <= 0 || config.FocusSwapDelayMs is < 0 or > 2000)
        {
            throw new InvalidDataException("multibox.json: focusTileShare must be 0.05-0.6, noticeSeconds positive, focusSwapDelayMs 0-2000.");
        }

        if (config.SlotCount is < 1 or > 9 || config.DefaultSlots.Any(n => n < 1 || n > config.SlotCount))
        {
            throw new InvalidDataException("multibox.json: slotCount must be 1-9 and defaultSlots within 1..slotCount.");
        }

        return config;
    }
}
