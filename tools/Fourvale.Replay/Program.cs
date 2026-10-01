using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fourvale.Adapter.Colyseus;

// Offline replay of a Fourvale.Capture file through the schema decoder (ADR 0002). Discovery tool, not shipped.
// Usage: Fourvale.Replay <capture.jsonl> [--schema] [--self] [--state <seconds>]

if (args.Length == 0 || !File.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: Fourvale.Replay <capture.jsonl> [--schema] [--self] [--state <seconds>]");
    return 1;
}

var showSchema = args.Contains("--schema");
var showSelf = args.Contains("--self");
var stateAt = args.SkipWhile(a => a != "--state").Skip(1).Select(a => double.Parse(a, CultureInfo.InvariantCulture)).FirstOrDefault(-1);

var rooms = new Dictionary<string, (string Name, string SessionId)>(); // roomId -> own seat
var sockets = new Dictionary<string, RoomSession>();
var allSessions = new List<RoomSession>();
var printedSchemas = new HashSet<string>();
var timeline = new List<string>();
var dumped = false;

foreach (var line in File.ReadLines(args[0]))
{
    var record = JsonNode.Parse(line)!.AsObject();
    var kind = (string?)record["kind"];
    var seconds = ((long?)record["ms"] ?? 0) / 1000.0;

    switch (kind)
    {
        case "http" when ((string?)record["url"])?.Contains("/matchmake/") == true:
        {
            var body = record["body"];
            var roomId = (string?)body?["room"]?["roomId"];
            if (roomId is not null)
            {
                rooms[roomId] = ((string?)body?["room"]?["name"] ?? "?", (string?)body?["sessionId"] ?? "");

                // The matchmake response is often logged just after its socket opened.
                foreach (var open in allSessions.Where(s => s.RoomId == roomId && s.SessionId.Length == 0))
                {
                    open.Seat(rooms[roomId].Name, rooms[roomId].SessionId);
                }
            }

            break;
        }

        case "ws_open":
        {
            var url = (string?)record["url"] ?? "";
            var roomId = new Uri(url).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
            var seat = rooms.GetValueOrDefault(roomId, ("?", ""));
            var session = new RoomSession(roomId, seconds);
            session.Seat(seat.Item1, seat.Item2);
            sockets[(string)record["socket"]!] = session;
            allSessions.Add(session);
            break;
        }

        case "ws" when (string?)record["dir"] == "in":
        {
            if (!sockets.TryGetValue((string)record["socket"]!, out var session))
            {
                break;
            }

            var protocol = (string?)record["protocol"];
            try
            {
                switch (protocol)
                {
                    case "JOIN_ROOM" when record["handshake"] is not null:
                        session.Context = SchemaContext.FromHandshake(Convert.FromBase64String((string)record["handshake"]!));
                        if (showSchema && printedSchemas.Add(session.Name))
                        {
                            PrintSchema(session.Name, session.Context);
                        }

                        break;

                    case "ROOM_STATE" when session.Context is not null:
                        session.Decoder = new StateDecoder(session.Context);
                        Record(session, session.Decoder.Apply(Convert.FromBase64String((string)record["bytes"]!)), seconds);
                        session.States++;
                        break;

                    case "ROOM_STATE_PATCH" when session.Decoder is not null:
                        Record(session, session.Decoder.Apply(Convert.FromBase64String((string)record["bytes"]!)), seconds);
                        session.Patches++;
                        break;
                }
            }
            catch (FormatException ex)
            {
                session.Errors++;
                if (session.Errors <= 3)
                {
                    Console.Error.WriteLine($"[{seconds,7:0.0}s] {session.Name}: {protocol} failed: {ex.Message}");
                }
            }

            if (!dumped && stateAt >= 0 && seconds >= stateAt && session.Decoder is not null)
            {
                dumped = true;
                Console.WriteLine($"=== State of {session.Name} at {seconds:0.0}s (own sessionId shown as @me)");
                Console.WriteLine(session.Decoder.ToJson().ToJsonString(new JsonSerializerOptions { WriteIndented = true })
                    .Replace(session.SessionId.Length > 0 ? session.SessionId : "\u0000", "@me"));
            }

            break;
        }

        case "marker" when showSelf:
            timeline.Add($"{seconds,7:0.0}s  ── marker: {(string?)record["label"]}");
            break;
    }
}

Console.WriteLine($"=== Rooms: {allSessions.Count}, states {allSessions.Sum(s => s.States)}, patches {allSessions.Sum(s => s.Patches)}, " +
                  $"decode errors {allSessions.Sum(s => s.Errors)}, definition mismatches {allSessions.Sum(s => s.Decoder?.DefinitionMismatches ?? 0)}");

foreach (var group in allSessions.GroupBy(s => s.Name))
{
    var counts = group.SelectMany(s => s.FieldCounts).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Sum(p => p.Value));
    Console.WriteLine($"--- {group.Key}: {group.Count()} room(s), {group.Sum(s => s.Patches)} patches. Most changed fields:");
    foreach (var (path, count) in counts.OrderByDescending(p => p.Value).Take(25))
    {
        Console.WriteLine($"    {count,6}  {path}");
    }
}

if (showSelf)
{
    Console.WriteLine("=== Own character (@me) timeline");
    foreach (var entry in timeline)
    {
        Console.WriteLine(entry);
    }
}

return allSessions.Sum(s => s.Errors) == 0 ? 0 : 2;

void Record(RoomSession session, IReadOnlyList<StateChange> changes, double seconds)
{
    foreach (var change in changes)
    {
        var normalized = Normalize(change.Path, session);
        session.FieldCounts[normalized] = session.FieldCounts.GetValueOrDefault(normalized) + 1;

        if (showSelf && normalized.Contains("@me") && change.Value is not StateRef && !normalized.EndsWith(".x") && !normalized.EndsWith(".y"))
        {
            timeline.Add($"{seconds,7:0.0}s  {session.Name,-8} {normalized} = {Format(change.Value)}  (was {Format(change.Previous)})");
        }
    }
}

// Own sessionId becomes @me; other map keys become *, so paths can be counted across rooms and players.
static string Normalize(string path, RoomSession session)
{
    var fieldNames = session.FieldNames;
    var parts = path.Split('.');
    for (var i = 0; i < parts.Length; i++)
    {
        if (parts[i] == session.SessionId && session.SessionId.Length > 0)
        {
            parts[i] = "@me";
        }
        else if (!fieldNames.Contains(parts[i]))
        {
            parts[i] = "*";
        }
    }

    return string.Join('.', parts);
}

static string Format(object? value) => value switch
{
    null => "∅",
    StateRef r => $"<ref {r.RefId}>",
    double d => d.ToString("0.###", CultureInfo.InvariantCulture),
    string s => $"\"{s}\"",
    _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
};

static void PrintSchema(string room, SchemaContext context)
{
    Console.WriteLine($"=== Schema of room '{room}' (root type {context.RootTypeId})");
    foreach (var type in context.Types.Values.OrderBy(t => t.Id))
    {
        Console.WriteLine($"  type {type.Id}{(type.Id == context.RootTypeId ? " (root)" : "")}:");
        foreach (var field in type.Fields)
        {
            Console.WriteLine($"    {field}");
        }
    }
}

sealed class RoomSession(string roomId, double openedAt)
{
    private SchemaContext? _context;

    public string RoomId { get; } = roomId;
    public string Name { get; private set; } = "?";
    public string SessionId { get; private set; } = "";

    public void Seat(string name, string sessionId)
    {
        Name = name;
        SessionId = sessionId;
    }

    public double OpenedAt { get; } = openedAt;
    public StateDecoder? Decoder { get; set; }
    public int States { get; set; }
    public int Patches { get; set; }
    public int Errors { get; set; }
    public Dictionary<string, int> FieldCounts { get; } = [];
    public HashSet<string> FieldNames { get; private set; } = [];

    public SchemaContext? Context
    {
        get => _context;
        set
        {
            _context = value;
            FieldNames = value?.Types.Values.SelectMany(t => t.Fields).Select(f => f.Name).ToHashSet() ?? [];
        }
    }
}
