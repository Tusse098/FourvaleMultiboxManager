using System.Text.Json;

namespace Fourvale.Adapter;

/// <summary>
/// Observation and redaction rules, loaded from fourvale-adapter.json (copied next to every executable
/// that uses the adapter). Kept in config so redaction can be tightened without a code change.
/// </summary>
public sealed class AdapterRules
{
    public const string FileName = "fourvale-adapter.json";

    /// <summary>Hosts whose XHR/fetch JSON responses are observed (after redaction).</summary>
    public IReadOnlyList<string> ApiHosts { get; init; } = [];

    /// <summary>Hosts whose script URLs are noted, to identify the Fourvale build.</summary>
    public IReadOnlyList<string> BundleHosts { get; init; } = [];

    /// <summary>Paths whose response bodies are never read (auth, account, password).</summary>
    public IReadOnlyList<string> DropBodyPathPrefixes { get; init; } = [];
    public string DropBodyPathPattern { get; init; } = "";

    /// <summary>JSON keys whose values are always replaced, anywhere in a message.</summary>
    public string RedactKeyPattern { get; init; } = "";

    /// <summary>Message types treated as chat: <see cref="ChatRedactKeys"/> are also replaced.</summary>
    public string ChatMessageTypePattern { get; init; } = "";
    public IReadOnlyList<string> ChatRedactKeys { get; init; } = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Loads the rules shipped next to the running executable.</summary>
    public static AdapterRules LoadDefault() => Load(Path.Combine(AppContext.BaseDirectory, FileName));

    public static AdapterRules Load(string path) => Parse(File.ReadAllText(path));

    public static AdapterRules Parse(string json)
    {
        var rules = JsonSerializer.Deserialize<AdapterRules>(json, JsonOptions)
            ?? throw new InvalidDataException($"{FileName} is empty.");

        if (string.IsNullOrWhiteSpace(rules.RedactKeyPattern) || string.IsNullOrWhiteSpace(rules.DropBodyPathPattern))
        {
            // Refuse to run with redaction switched off by an incomplete config.
            throw new InvalidDataException($"{FileName} must define redactKeyPattern and dropBodyPathPattern.");
        }

        return rules;
    }
}
