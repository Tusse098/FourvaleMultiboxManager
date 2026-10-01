using System.Text.Json.Nodes;

namespace Fourvale.Adapter.Colyseus;

/// <summary>A schema object or collection that has a refId in the decoded state tree.</summary>
public abstract class StateRef(int refId)
{
    public int RefId { get; } = refId;
    public StateRef? Parent { get; internal set; }

    /// <summary>Field name (schema parent) or item key (collection parent).</summary>
    public string? ParentKey { get; internal set; }

    public string Path => Parent is null ? "" : Parent.Path.Length == 0 ? ParentKey ?? "" : $"{Parent.Path}.{ParentKey}";
}

public sealed class SchemaObject(SchemaType type, int refId) : StateRef(refId)
{
    private readonly object?[] _values = new object?[type.Fields.Count];

    public SchemaType Type { get; } = type;

    public object? Get(string field)
    {
        var index = Type.IndexOf(field);
        return index < 0 ? null : _values[index];
    }

    internal object? GetAt(int index) => _values[index];

    internal void SetAt(int index, object? value) => _values[index] = value;

    public IEnumerable<(SchemaField Field, object? Value)> Fields() =>
        Type.Fields.Select((f, i) => (f, _values[i]));
}

public sealed class StateCollection(SchemaField field, int refId) : StateRef(refId)
{
    // Wire index -> item key (string for maps, wire index for the other kinds), and key -> value in insertion order.
    private readonly Dictionary<int, object> _indexes = [];
    private readonly OrderedDictionary<object, object?> _items = [];

    public SchemaField Field { get; } = field;
    public FieldKind Kind => Field.Kind;
    public int Count => _items.Count;
    public IEnumerable<object?> Values => _items.Values;
    public IEnumerable<KeyValuePair<object, object?>> Items => _items;

    public object? GetByKey(object key) => _items.GetValueOrDefault(key);

    internal object? GetByIndex(int index) =>
        _indexes.TryGetValue(index, out var key) ? _items.GetValueOrDefault(key) : null;

    internal object? KeyOf(int index) => _indexes.GetValueOrDefault(index);

    internal void SetIndex(int index, object key) => _indexes[index] = key;

    internal void Set(object key, object? value) => _items[key] = value;

    internal void DeleteByIndex(int index)
    {
        if (_indexes.Remove(index, out var key))
        {
            _items.Remove(key);
        }
    }

    internal void Clear()
    {
        _indexes.Clear();
        _items.Clear();
    }
}

public enum ChangeOperation
{
    Replace = 0,
    Touch = 1,
    Clear = 10,
    Delete = 64,
    Add = 128,
    DeleteAndAdd = 192,
}

/// <summary>One value change produced by applying a state or patch. <see cref="Path"/> is e.g. "players.abc123.hp".</summary>
public sealed record StateChange(string Path, ChangeOperation Operation, object? Value, object? Previous);

/// <summary>
/// Applies @colyseus/schema v2 full states and patches for one room, mirroring the bundled client's
/// <c>Schema.decode</c> (see ADR 0002). Read-only: it never produces bytes for the server.
/// Throws <see cref="FormatException"/> on malformed input; the state may then be partially updated
/// and should be treated as stale until the next full state.
/// </summary>
public sealed class StateDecoder
{
    private const byte SwitchToStructure = 255;
    private const byte TypeId = 213;

    private readonly SchemaContext _context;
    private readonly Dictionary<int, StateRef> _refs = [];

    public StateDecoder(SchemaContext context)
    {
        _context = context;
        Root = new SchemaObject(context.RootType, 0);
        _refs[0] = Root;
    }

    public SchemaObject Root { get; }

    /// <summary>Number of times a field index was not in the schema and bytes had to be skipped.</summary>
    public int DefinitionMismatches { get; private set; }

    public IReadOnlyList<StateChange> Apply(ReadOnlySpan<byte> bytes)
    {
        var reader = new SchemaReader(bytes);
        var changes = new List<StateChange>();
        StateRef current = Root;

        while (reader.HasMore)
        {
            var b = reader.ReadByte();

            if (b == SwitchToStructure)
            {
                var refId = reader.ReadIndex();
                current = _refs.TryGetValue(refId, out var target)
                    ? target
                    : throw new FormatException($"refId {refId} not found.");
                continue;
            }

            if (current is SchemaObject schema)
            {
                ApplySchemaOperation(ref reader, schema, b, changes);
            }
            else
            {
                ApplyCollectionOperation(ref reader, (StateCollection)current, b, changes);
            }
        }

        return changes;
    }

    private void ApplySchemaOperation(ref SchemaReader reader, SchemaObject target, byte b, List<StateChange> changes)
    {
        var op = (b >> 6) << 6;
        var index = b % (op == 0 ? 255 : op);
        var field = target.Type.FieldAt(index);

        if (field is null)
        {
            SkipToNextKnownStructure(ref reader);
            return;
        }

        var previous = target.GetAt(index);
        object? value = null;

        if ((op & (int)ChangeOperation.Delete) == (int)ChangeOperation.Delete && op != (int)ChangeOperation.DeleteAndAdd)
        {
            target.SetAt(index, null);
        }

        if (op != (int)ChangeOperation.Delete)
        {
            value = ReadValue(ref reader, field, op, previous);
        }

        if (value is not null)
        {
            Attach(value, target, field.Name);
            target.SetAt(index, value);
        }

        if (!Equals(previous, value))
        {
            changes.Add(new StateChange(Join(target.Path, field.Name), (ChangeOperation)op, value, previous));
        }
    }

    private void ApplyCollectionOperation(ref SchemaReader reader, StateCollection target, byte op, List<StateChange> changes)
    {
        if (op == (byte)ChangeOperation.Clear)
        {
            target.Clear();
            changes.Add(new StateChange(target.Path, ChangeOperation.Clear, null, null));
            return;
        }

        var index = reader.ReadIndex();
        var previous = target.GetByIndex(index);
        object key;

        if ((op & (int)ChangeOperation.Add) == (int)ChangeOperation.Add)
        {
            key = target.Kind == FieldKind.Map ? reader.ReadString() : index;
            target.SetIndex(index, key);
        }
        else
        {
            key = target.KeyOf(index) ?? index;
        }

        object? value = null;
        if ((op & (int)ChangeOperation.Delete) == (int)ChangeOperation.Delete && op != (int)ChangeOperation.DeleteAndAdd)
        {
            target.DeleteByIndex(index);
        }

        if (op != (int)ChangeOperation.Delete)
        {
            // Collections hold either schema objects or primitives; nested collections do not exist in v2.
            var child = target.Field.HasSchemaChild
                ? new SchemaField("", FieldKind.Ref, null, target.Field.ChildTypeId)
                : new SchemaField("", FieldKind.Primitive, target.Field.PrimitiveType, null);
            value = ReadValue(ref reader, child, op, previous);
        }

        if (value is not null)
        {
            var keyText = Convert.ToString(key, System.Globalization.CultureInfo.InvariantCulture) ?? "";
            Attach(value, target, keyText);
            target.Set(key, value);
        }

        if (!Equals(previous, value))
        {
            changes.Add(new StateChange(
                Join(target.Path, Convert.ToString(key, System.Globalization.CultureInfo.InvariantCulture) ?? ""),
                (ChangeOperation)op, value, previous));
        }
    }

    private object? ReadValue(ref SchemaReader reader, SchemaField field, int op, object? previous)
    {
        if (field.Kind == FieldKind.Ref)
        {
            var refId = reader.ReadIndex();
            _refs.TryGetValue(refId, out var existing);
            if (op == (int)ChangeOperation.Replace)
            {
                return existing ?? throw new FormatException($"REPLACE refers to unknown refId {refId}.");
            }

            var typeId = field.ChildTypeId!.Value;
            if (reader.HasMore && reader.Peek() == TypeId)
            {
                reader.ReadByte();
                typeId = reader.ReadIndex();
            }

            if (existing is not null)
            {
                return existing;
            }

            var created = new SchemaObject(_context.GetType(typeId), refId);
            _refs[refId] = created;
            return created;
        }

        if (field.Kind == FieldKind.Primitive)
        {
            return reader.ReadPrimitive(field.PrimitiveType ?? throw new FormatException($"Field '{field.Name}' has no type."));
        }

        // A collection-typed field of a schema object.
        var collectionRefId = reader.ReadIndex();
        if (_refs.TryGetValue(collectionRefId, out var known))
        {
            return previous is StateCollection p && p.RefId == collectionRefId ? p : known;
        }

        var collection = new StateCollection(field, collectionRefId);
        _refs[collectionRefId] = collection;
        return collection;
    }

    /// <summary>Recovery used by the client on a definition mismatch: skip to the next SWITCH_TO_STRUCTURE with a known refId.</summary>
    private void SkipToNextKnownStructure(ref SchemaReader reader)
    {
        DefinitionMismatches++;
        while (reader.HasMore)
        {
            var position = reader.Offset;
            if (reader.PeekAt(position) == SwitchToStructure
                && reader.TryPeekIndexAt(position + 1, out var refId)
                && _refs.ContainsKey(refId))
            {
                return;
            }

            reader.Offset++;
        }
    }

    private static void Attach(object value, StateRef parent, string key)
    {
        if (value is StateRef child && !ReferenceEquals(child, parent))
        {
            child.Parent = parent;
            child.ParentKey = key;
        }
    }

    private static string Join(string path, string key) => path.Length == 0 ? key : $"{path}.{key}";

    /// <summary>The whole decoded state as JSON (maps become objects, other collections arrays).</summary>
    public JsonObject ToJson() => (JsonObject)ToJson(Root)!;

    public static JsonNode? ToJson(object? value) => value switch
    {
        null => null,
        SchemaObject obj => new JsonObject(obj.Fields().Select(f => KeyValuePair.Create(f.Field.Name, ToJson(f.Value)))),
        StateCollection { Kind: FieldKind.Map } map => new JsonObject(map.Items.Select(i =>
            KeyValuePair.Create(Convert.ToString(i.Key, System.Globalization.CultureInfo.InvariantCulture) ?? "", ToJson(i.Value)))),
        StateCollection list => new JsonArray(list.Values.Select(ToJson).ToArray()),
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        long l => JsonValue.Create(l),
        double d when double.IsFinite(d) => JsonValue.Create(d),
        double d => JsonValue.Create(d.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        _ => JsonValue.Create(value.ToString()),
    };
}
