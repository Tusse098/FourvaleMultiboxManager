namespace Fourvale.Adapter.Colyseus;

public enum FieldKind
{
    Primitive,
    Ref,
    Map,
    Array,
    Set,
    Collection,
}

/// <summary>
/// One field of a schema type. For <see cref="FieldKind.Ref"/> and collections of schemas,
/// <see cref="ChildTypeId"/> names the child type; for primitives and collections of primitives,
/// <see cref="PrimitiveType"/> names the primitive.
/// </summary>
public sealed record SchemaField(string Name, FieldKind Kind, string? PrimitiveType, int? ChildTypeId)
{
    public bool IsCollection => Kind is FieldKind.Map or FieldKind.Array or FieldKind.Set or FieldKind.Collection;

    /// <summary>True when the field (or each collection item) is a schema object.</summary>
    public bool HasSchemaChild => ChildTypeId is not null;

    public override string ToString() => Kind switch
    {
        FieldKind.Primitive => $"{Name}: {PrimitiveType}",
        FieldKind.Ref => $"{Name}: ref<{ChildTypeId}>",
        _ => $"{Name}: {Kind.ToString().ToLowerInvariant()}<{(ChildTypeId is { } id ? $"ref<{id}>" : PrimitiveType)}>",
    };
}

public sealed record SchemaType(int Id, IReadOnlyList<SchemaField> Fields)
{
    public SchemaField? FieldAt(int index) => index >= 0 && index < Fields.Count ? Fields[index] : null;

    public int IndexOf(string name)
    {
        for (var i = 0; i < Fields.Count; i++)
        {
            if (Fields[i].Name == name)
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>
/// The set of schema types a room uses, as declared by that room's own handshake.
/// Field indexes follow declaration order, as in the client.
/// </summary>
public sealed class SchemaContext
{
    public SchemaContext(IReadOnlyDictionary<int, SchemaType> types, int rootTypeId)
    {
        if (!types.ContainsKey(rootTypeId))
        {
            throw new FormatException($"Root type {rootTypeId} is not defined.");
        }

        Types = types;
        RootTypeId = rootTypeId;
    }

    public IReadOnlyDictionary<int, SchemaType> Types { get; }
    public int RootTypeId { get; }
    public SchemaType RootType => Types[RootTypeId];

    public SchemaType GetType(int id) =>
        Types.TryGetValue(id, out var type) ? type : throw new FormatException($"Unknown schema type id {id}.");

    /// <summary>
    /// The three types used to encode the handshake itself:
    /// ReflectionField { name, type, referencedType }, ReflectionType { id, fields[] }, Reflection { types[], rootType }.
    /// </summary>
    public static SchemaContext Reflection { get; } = new(
        new Dictionary<int, SchemaType>
        {
            [0] = new(0,
            [
                new("name", FieldKind.Primitive, "string", null),
                new("type", FieldKind.Primitive, "string", null),
                new("referencedType", FieldKind.Primitive, "number", null),
            ]),
            [1] = new(1,
            [
                new("id", FieldKind.Primitive, "number", null),
                new("fields", FieldKind.Array, null, 0),
            ]),
            [2] = new(2,
            [
                new("types", FieldKind.Array, null, 1),
                new("rootType", FieldKind.Primitive, "number", null),
            ]),
        },
        rootTypeId: 2);

    /// <summary>Builds the room's schema from the handshake bytes that follow the serializer id in JOIN_ROOM.</summary>
    public static SchemaContext FromHandshake(ReadOnlySpan<byte> handshake)
    {
        var decoder = new StateDecoder(Reflection);
        decoder.Apply(handshake);
        var root = decoder.Root;

        var types = new Dictionary<int, SchemaType>();
        var declared = new List<(int Id, List<(string Name, string Type, int? Referenced)> Fields)>();

        foreach (var typeObject in Items(root, "types"))
        {
            var id = ToInt(typeObject.Get("id"), "ReflectionType.id");
            var fields = Items(typeObject, "fields")
                .Select(f => (
                    Name: f.Get("name") as string ?? throw new FormatException("ReflectionField without name."),
                    Type: f.Get("type") as string ?? throw new FormatException("ReflectionField without type."),
                    Referenced: f.Get("referencedType") is { } r ? ToInt(r, "referencedType") : (int?)null))
                .ToList();
            declared.Add((id, fields));
        }

        var ids = declared.Select(d => d.Id).ToHashSet();
        foreach (var (id, fields) in declared)
        {
            types[id] = new SchemaType(id, fields.Select(f => ToField(f.Name, f.Type, f.Referenced, ids)).ToList());
        }

        return new SchemaContext(types, ToInt(root.Get("rootType"), "Reflection.rootType"));
    }

    private static SchemaField ToField(string name, string type, int? referenced, HashSet<int> knownTypes)
    {
        if (referenced is null)
        {
            return new SchemaField(name, FieldKind.Primitive, type, null);
        }

        // "ref" with a type id, a collection with a type id, or "map:string" style (primitive children, referencedType -1).
        if (knownTypes.Contains(referenced.Value))
        {
            return new SchemaField(name, KindOf(type), null, referenced.Value);
        }

        var parts = type.Split(':', 2);
        if (parts.Length != 2)
        {
            throw new FormatException($"Field '{name}' references unknown type {referenced} ('{type}').");
        }

        return new SchemaField(name, KindOf(parts[0]), parts[1], null);
    }

    private static FieldKind KindOf(string kind) => kind switch
    {
        "ref" => FieldKind.Ref,
        "map" => FieldKind.Map,
        "array" => FieldKind.Array,
        "set" => FieldKind.Set,
        "collection" => FieldKind.Collection,
        _ => throw new FormatException($"Unknown container kind '{kind}'."),
    };

    private static IEnumerable<SchemaObject> Items(SchemaObject owner, string field) =>
        owner.Get(field) is StateCollection collection
            ? collection.Values.OfType<SchemaObject>()
            : [];

    private static int ToInt(object? value, string what) => value switch
    {
        long l when l is >= int.MinValue and <= int.MaxValue => (int)l,
        _ => throw new FormatException($"{what} is not an integer."),
    };
}
