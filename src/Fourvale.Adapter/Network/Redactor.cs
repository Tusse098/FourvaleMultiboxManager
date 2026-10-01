using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Fourvale.Adapter.Network;

/// <summary>
/// Removes secrets and personal data from captured messages before they are written anywhere.
/// Every value that leaves the network observer passes through here.
/// </summary>
public sealed class Redactor
{
    public const string Placeholder = "<REDACTED>";
    public const string EmailPlaceholder = "<EMAIL>";
    public const string TokenPlaceholder = "<TOKEN>";

    private static readonly Regex EmailPattern = new(
        @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);

    // JWT-shaped values (three base64url segments, header starting with eyJ) and bearer strings.
    private static readonly Regex TokenValuePattern = new(
        @"(eyJ[A-Za-z0-9_\-]{5,}\.[A-Za-z0-9_\-]{5,}\.[A-Za-z0-9_\-]*)|((?i)bearer\s+\S+)", RegexOptions.Compiled);

    private readonly AdapterRules _rules;
    private readonly Regex _redactKey;
    private readonly Regex _dropPath;
    private readonly Regex? _chatType;
    private readonly HashSet<string> _chatKeys;

    public Redactor(AdapterRules rules)
    {
        _rules = rules;
        _redactKey = new Regex(rules.RedactKeyPattern, RegexOptions.Compiled);
        _dropPath = new Regex(rules.DropBodyPathPattern, RegexOptions.Compiled);
        _chatType = string.IsNullOrWhiteSpace(rules.ChatMessageTypePattern)
            ? null
            : new Regex(rules.ChatMessageTypePattern, RegexOptions.Compiled);
        _chatKeys = new HashSet<string>(rules.ChatRedactKeys, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsChatType(string? messageType) =>
        messageType is not null && _chatType is not null && _chatType.IsMatch(messageType);

    /// <summary>True when a response body on this path must never be read (auth, account, password).</summary>
    public bool IsAuthPath(string path)
    {
        foreach (var prefix in _rules.DropBodyPathPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return _dropPath.IsMatch(path);
    }

    /// <summary>
    /// Keeps scheme, host and path. Query parameter names are kept, values are replaced.
    /// Fragments and user info are dropped.
    /// </summary>
    public string SanitizeUrl(string url, ref int redactions)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            redactions++;
            return "<UNPARSEABLE_URL>";
        }

        var builder = new StringBuilder();
        builder.Append(uri.Scheme).Append("://").Append(uri.Host);
        if (!uri.IsDefaultPort)
        {
            builder.Append(':').Append(uri.Port);
        }

        builder.Append(uri.AbsolutePath);

        var query = uri.Query.TrimStart('?');
        if (query.Length > 0)
        {
            var parts = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
            builder.Append('?');
            for (var i = 0; i < parts.Length; i++)
            {
                var name = parts[i].Split('=', 2)[0];
                if (i > 0)
                {
                    builder.Append('&');
                }

                builder.Append(name).Append('=').Append(Placeholder);
                redactions++;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Redacts a JSON tree in place. Returns the node to store (the root itself may be replaced).
    /// </summary>
    public JsonNode? Redact(JsonNode? node, bool isChat, ref int redactions)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (_redactKey.IsMatch(key) || (isChat && _chatKeys.Contains(key)))
                    {
                        if (obj[key] is not null)
                        {
                            obj[key] = Placeholder;
                            redactions++;
                        }

                        continue;
                    }

                    var child = obj[key];
                    var replaced = Redact(child, isChat, ref redactions);
                    if (!ReferenceEquals(child, replaced))
                    {
                        obj[key] = replaced;
                    }
                }

                return obj;

            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    var child = array[i];
                    var replaced = Redact(child, isChat, ref redactions);
                    if (!ReferenceEquals(child, replaced))
                    {
                        array[i] = replaced;
                    }
                }

                return array;

            case JsonValue value when value.TryGetValue<string>(out var text):
                var cleaned = RedactString(text, ref redactions);
                return ReferenceEquals(cleaned, text) ? value : JsonValue.Create(cleaned);

            default:
                return node;
        }
    }

    /// <summary>Replaces e-mail addresses and token-shaped substrings inside free text.</summary>
    public string RedactString(string text, ref int redactions)
    {
        var count = 0;
        var result = TokenValuePattern.Replace(text, _ => { count++; return TokenPlaceholder; });
        result = EmailPattern.Replace(result, _ => { count++; return EmailPlaceholder; });
        if (count == 0)
        {
            return text;
        }

        redactions += count;
        return result;
    }
}
