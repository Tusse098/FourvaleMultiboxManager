using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Fourvale.Adapter.Tests;

/// <summary>
/// Guards tests/fixtures/messages against unsanitised data (CLAUDE.md "Discovery and fixtures"):
/// identifiers must be placeholders, player names must be PlayerNN, and nothing secret-looking may appear.
/// </summary>
public partial class FixtureHygieneTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "fixtures", "messages");

    [GeneratedRegex(@"^(<ID_\d+>|<TICKET>|enemy_\d+)$")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"(?i)[\w.+-]+@[\w-]+\.[a-z]{2,}|eyJ|bearer|reconnectionToken")]
    private static partial Regex SecretLike();

    public static TheoryData<string> Files()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(Folder, "*.json"))
        {
            data.Add(Path.GetFileName(file));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Fixture_is_documented_and_sanitised(string file)
    {
        var text = File.ReadAllText(Path.Combine(Folder, file));
        var root = JsonNode.Parse(text)!;

        Assert.NotNull(root["_fixture"]?["captured"]);
        Assert.NotNull(root["_fixture"]?["fourvale"]);
        Assert.DoesNotMatch(SecretLike(), text);

        foreach (var (key, value) in Strings(root["payload"]))
        {
            if (key is "sessionId" or "roomId" or "processId" or "sourceId" or "targetId" or "senderId" or "ticket" or "battleKey")
            {
                Assert.Matches(Placeholder(), value);
            }

            if (key is "name" or "username" or "from")
            {
                // Enemy names and room types are game content; player names only appear as PlayerNN.
                Assert.True(
                    value.StartsWith("Player", StringComparison.Ordinal) || IsEnemyContext(root) || RoomTypes.Contains(value),
                    $"{file}: '{key}' = '{value}'");
            }
        }
    }

    private static readonly HashSet<string> RoomTypes = ["town", "dungeon", "battle"];

    private static bool IsEnemyContext(JsonNode root) => (string?)root["_fixture"]?["type"] == "startBattle";

    private static IEnumerable<(string Key, string Value)> Strings(JsonNode? node, string key = "")
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (k, v) in obj)
                {
                    foreach (var item in Strings(v, k))
                    {
                        yield return item;
                    }
                }

                break;
            case JsonArray array:
                foreach (var v in array)
                {
                    foreach (var item in Strings(v, key))
                    {
                        yield return item;
                    }
                }

                break;
            case JsonValue value when value.TryGetValue<string>(out var s):
                yield return (key, s);
                break;
        }
    }
}
