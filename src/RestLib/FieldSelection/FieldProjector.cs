using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using RestLib.Serialization;

namespace RestLib.FieldSelection;

/// <summary>
/// Projects entity objects to include only selected fields. Effective JSON
/// contracts provide names, accessors, and member-specific serialization rules.
/// Sparse selections serialize individual members; dense or converter-owned
/// selections serialize the entity once and pick the requested JSON paths.
/// </summary>
internal static class FieldProjector
{
    /// <summary>
    /// Threshold ratio of selected fields to total properties above which
    /// serialize-then-pick is used instead of per-property access.
    /// </summary>
    private const double SerializeThresholdRatio = 0.5;

    private static readonly ConditionalWeakTable<
        JsonSerializerOptions,
        ConcurrentDictionary<Type, PropertyAccessorMap>> AccessorCaches = new();
    private static readonly JsonElement NullElement = CreateNullElement();

    /// <summary>
    /// Projects a single entity to a dictionary containing only the selected fields.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="entity">The entity instance to project.</param>
    /// <param name="selectedFields">The fields to include.</param>
    /// <param name="jsonOptions">The effective JSON serializer options.</param>
    /// <param name="responseShape">The response shape to use for nested selected fields.</param>
    /// <returns>A dictionary of field name to JSON value, or null if no projection is needed.</returns>
    internal static Dictionary<string, JsonElement>? Project<TEntity>(
        TEntity entity,
        IReadOnlyList<SelectedField> selectedFields,
        JsonSerializerOptions jsonOptions,
        FieldSelectionResponseShape responseShape = FieldSelectionResponseShape.Flat)
    {
        if (selectedFields.Count == 0)
        {
            return null;
        }

        var accessorMap = GetOrBuildAccessorMap(typeof(TEntity), jsonOptions);
        var converterOwnsSelectedPath = selectedFields.Any(field =>
            accessorMap.TryGetAccessor(field.PropertyName, out var accessor)
            && accessor.RequiresWholeEntitySerialization);
        var useSerializeFallback = accessorMap.RequiresSerializeFallback
            || converterOwnsSelectedPath
            || ShouldUseSerializeFallback(selectedFields.Count, accessorMap.PropertyCount);

        // Converter-owned JSON is authoritative. Density fallback can still recover
        // an explicitly selected member omitted by normal whole-object serialization.
        var missingValueAccessorMap = accessorMap.RequiresSerializeFallback
            ? null
            : accessorMap;
        var flatResult = useSerializeFallback
            ? SerializeThenPick(entity, selectedFields, jsonOptions, missingValueAccessorMap)
            : ProjectWithAccessors(entity, selectedFields, accessorMap);

        return ApplyResponseShape(flatResult, responseShape, jsonOptions);
    }

    /// <summary>
    /// Projects a list of entities to dictionaries containing only the selected fields.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="entities">The entities to project.</param>
    /// <param name="selectedFields">The fields to include.</param>
    /// <param name="jsonOptions">The effective JSON serializer options.</param>
    /// <param name="responseShape">The response shape to use for nested selected fields.</param>
    /// <returns>A list of projected dictionaries, or null if no projection is needed.</returns>
    internal static IReadOnlyList<Dictionary<string, JsonElement>>? ProjectMany<TEntity>(
        IReadOnlyList<TEntity> entities,
        IReadOnlyList<SelectedField> selectedFields,
        JsonSerializerOptions jsonOptions,
        FieldSelectionResponseShape responseShape = FieldSelectionResponseShape.Flat)
    {
        if (selectedFields.Count == 0)
        {
            return null;
        }

        var results = new List<Dictionary<string, JsonElement>>(entities.Count);
        foreach (var entity in entities)
        {
            var projected = Project(entity, selectedFields, jsonOptions, responseShape);
            if (projected is not null)
            {
                results.Add(projected);
            }
        }

        return results;
    }

    private static Dictionary<string, JsonElement> ProjectWithAccessors<TEntity>(
        TEntity entity,
        IReadOnlyList<SelectedField> selectedFields,
        PropertyAccessorMap accessorMap)
    {
        var result = new Dictionary<string, JsonElement>(selectedFields.Count);
        foreach (var field in selectedFields)
        {
            if (accessorMap.TryGetAccessor(field.PropertyName, out var accessor))
            {
                result[accessor.JsonPath] = ProjectWithAccessor(entity!, accessor);
            }
        }

        return result;
    }

    private static JsonElement ProjectWithAccessor(object entity, PathAccessor accessor)
    {
        var value = accessor.GetValue(entity);
        return value is null
            ? NullElement
            : JsonSerializer.SerializeToElement(
                value,
                accessor.PropertyType,
                accessor.ValueSerializerOptions);
    }

    private static bool ShouldUseSerializeFallback(int selectedCount, int totalProperties)
    {
        if (totalProperties == 0)
        {
            return true;
        }

        return (double)selectedCount / totalProperties > SerializeThresholdRatio;
    }

    private static Dictionary<string, JsonElement> SerializeThenPick<TEntity>(
        TEntity entity,
        IReadOnlyList<SelectedField> selectedFields,
        JsonSerializerOptions jsonOptions,
        PropertyAccessorMap? missingValueAccessorMap)
    {
        var json = JsonSerializer.Serialize(entity, jsonOptions);
        using var document = JsonDocument.Parse(json);
        var result = new Dictionary<string, JsonElement>(selectedFields.Count);

        foreach (var field in selectedFields)
        {
            PathAccessor? accessor = null;
            var hasAccessor = missingValueAccessorMap is not null
                && missingValueAccessorMap.TryGetAccessor(field.PropertyName, out accessor);
            var outputPath = hasAccessor ? accessor!.JsonPath : field.QueryParameterName;

            if (TryGetJsonPathValue(document.RootElement, outputPath, out var value)
                || (!outputPath.Equals(field.QueryParameterName, StringComparison.Ordinal)
                    && TryGetJsonPathValue(document.RootElement, field.QueryParameterName, out value)))
            {
                result[outputPath] = value.Clone();
            }
            else if (hasAccessor && !accessor!.RequiresWholeEntitySerialization)
            {
                result[outputPath] = ProjectWithAccessor(entity!, accessor);
            }
        }

        return result;
    }

    private static Dictionary<string, JsonElement> ApplyResponseShape(
        Dictionary<string, JsonElement> flatResult,
        FieldSelectionResponseShape responseShape,
        JsonSerializerOptions jsonOptions)
    {
        if (responseShape != FieldSelectionResponseShape.Nested)
        {
            return flatResult;
        }

        var nestedResult = new JsonObject();
        foreach (var field in flatResult)
        {
            SetNestedElement(nestedResult, field.Key, field.Value);
        }

        return SerializeNestedResult(nestedResult, jsonOptions);
    }

    private static PropertyAccessorMap GetOrBuildAccessorMap(
        Type entityType,
        JsonSerializerOptions jsonOptions)
    {
        if (!jsonOptions.IsReadOnly)
        {
            return PropertyAccessorMap.Build(entityType, jsonOptions);
        }

        var cache = AccessorCaches.GetValue(
            jsonOptions,
            static _ => new ConcurrentDictionary<Type, PropertyAccessorMap>());
        return cache.GetOrAdd(entityType, type => PropertyAccessorMap.Build(type, jsonOptions));
    }

    private static bool TryGetJsonPathValue(
        JsonElement current,
        string propertyPath,
        out JsonElement value)
    {
        value = current;
        foreach (var segment in propertyPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out value))
            {
                return false;
            }
        }

        return true;
    }

    private static void SetNestedElement(
        JsonObject result,
        string jsonPath,
        JsonElement value)
    {
        var segments = jsonPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return;
        }

        if (segments.Length == 1)
        {
            result[segments[0]] = JsonNode.Parse(value.GetRawText());
            return;
        }

        var current = result;
        for (var index = 0; index < segments.Length - 1; index++)
        {
            var segment = segments[index];
            if (current[segment] is JsonObject nestedObject)
            {
                current = nestedObject;
                continue;
            }

            var next = new JsonObject();
            current[segment] = next;
            current = next;
        }

        current[segments[^1]] = JsonNode.Parse(value.GetRawText());
    }

    private static Dictionary<string, JsonElement> SerializeNestedResult(
        JsonObject result,
        JsonSerializerOptions jsonOptions)
    {
        var root = JsonSerializer.SerializeToElement(result, jsonOptions);
        var projected = new Dictionary<string, JsonElement>(root.GetPropertyCount());
        foreach (var property in root.EnumerateObject())
        {
            projected[property.Name] = property.Value.Clone();
        }

        return projected;
    }

    private static JsonElement CreateNullElement()
    {
        using var document = JsonDocument.Parse("null");
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Serializer-contract-backed map of field-selection accessors for one entity type.
    /// </summary>
    private sealed class PropertyAccessorMap
    {
        private readonly JsonObjectContract _contract;
        private readonly ConcurrentDictionary<string, PathAccessor> _pathAccessors = new(StringComparer.Ordinal);

        private PropertyAccessorMap(
            JsonObjectContract contract,
            Dictionary<string, PathAccessor> accessors,
            bool requiresSerializeFallback)
        {
            _contract = contract;
            _pathAccessors = new ConcurrentDictionary<string, PathAccessor>(
                accessors,
                StringComparer.Ordinal);
            RequiresSerializeFallback = requiresSerializeFallback;
            PropertyCount = accessors.Count;
        }

        /// <summary>
        /// Gets a value indicating whether a converter owns the root JSON representation.
        /// </summary>
        internal bool RequiresSerializeFallback { get; }

        /// <summary>
        /// Gets the total number of directly selectable members.
        /// </summary>
        internal int PropertyCount { get; }

        /// <summary>
        /// Builds an accessor map from the effective JSON contract.
        /// </summary>
        internal static PropertyAccessorMap Build(
            Type entityType,
            JsonSerializerOptions jsonOptions)
        {
            var contract = JsonObjectContract.Get(entityType, jsonOptions);
            if (!contract.IsObject)
            {
                return new PropertyAccessorMap(contract, [], requiresSerializeFallback: true);
            }

            var accessors = contract.Members
                .Where(member => member.ClrName is not null && member.CanReadForFieldSelection)
                .GroupBy(member => member.ClrName!, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => PathAccessor.Create([group.First()]),
                    StringComparer.Ordinal);
            return new PropertyAccessorMap(contract, accessors, requiresSerializeFallback: false);
        }

        /// <summary>
        /// Resolves a configured CLR member path to its effective JSON accessor.
        /// </summary>
        internal bool TryGetAccessor(string propertyPath, out PathAccessor accessor)
        {
            if (_pathAccessors.TryGetValue(propertyPath, out accessor!))
            {
                return true;
            }

            if (!_contract.TryResolveClrPath(propertyPath, out var memberPath)
                || memberPath.Members.Any(member => !member.CanReadForFieldSelection))
            {
                accessor = null!;
                return false;
            }

            accessor = _pathAccessors.GetOrAdd(
                propertyPath,
                _ => PathAccessor.Create(memberPath.Members));
            return true;
        }
    }

    /// <summary>
    /// Reads a selected CLR path and retains its effective JSON serialization metadata.
    /// </summary>
    private sealed class PathAccessor
    {
        private readonly IReadOnlyList<JsonMemberContract> _members;

        private PathAccessor(IReadOnlyList<JsonMemberContract> members)
        {
            _members = members;
            JsonPath = string.Join('.', members.Select(member => member.JsonName));
            PropertyType = members[^1].MemberType;
            ValueSerializerOptions = members[^1].ValueSerializerOptions;
            RequiresWholeEntitySerialization = members
                .Take(members.Count - 1)
                .Any(member => member.HasMemberSerializationOverrides);
        }

        /// <summary>
        /// Gets the canonical dotted JSON path.
        /// </summary>
        internal string JsonPath { get; }

        /// <summary>
        /// Gets the selected leaf type.
        /// </summary>
        internal Type PropertyType { get; }

        /// <summary>
        /// Gets the serializer options carrying leaf member overrides.
        /// </summary>
        internal JsonSerializerOptions ValueSerializerOptions { get; }

        /// <summary>
        /// Gets a value indicating whether a converter owns an intermediate JSON shape.
        /// </summary>
        internal bool RequiresWholeEntitySerialization { get; }

        /// <summary>
        /// Creates an accessor for an ordered member path.
        /// </summary>
        internal static PathAccessor Create(IReadOnlyList<JsonMemberContract> members) => new(members);

        /// <summary>
        /// Reads the selected value, returning null when an intermediate value is null.
        /// </summary>
        internal object? GetValue(object entity)
        {
            object? current = entity;
            foreach (var member in _members)
            {
                if (current is null)
                {
                    return null;
                }

                current = member.GetValue(current);
            }

            return current;
        }
    }
}
