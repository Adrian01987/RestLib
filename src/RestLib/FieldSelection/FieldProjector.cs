using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RestLib.FieldSelection;

/// <summary>
/// Projects entity objects to include only selected fields.
/// Uses a hybrid strategy: per-property reflection with compiled expression tree getters
/// for sparse field selections, and serialize-then-pick for dense selections.
/// Falls back to serialize-then-pick when a type has a class-level <see cref="JsonConverterAttribute"/>.
/// </summary>
internal static class FieldProjector
{
    /// <summary>
    /// Threshold ratio of selected fields to total properties above which
    /// serialize-then-pick is used instead of per-property reflection.
    /// When selecting more than half the properties, serializing the whole object
    /// once and picking fields is faster than serializing each property individually.
    /// </summary>
    private const double SerializeThresholdRatio = 0.5;

    private static readonly ConcurrentDictionary<Type, PropertyAccessorMap> AccessorCache = new();

    /// <summary>
    /// Projects a single entity to a dictionary containing only the selected fields.
    /// Uses a hybrid strategy: per-property reflection for sparse selections,
    /// serialize-then-pick for dense selections (more than 50% of properties).
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="entity">The entity instance to project.</param>
    /// <param name="selectedFields">The fields to include.</param>
    /// <param name="jsonOptions">The JSON serializer options (for naming policy).</param>
    /// <returns>A dictionary of field name to JSON value, or null if no projection needed.</returns>
    internal static Dictionary<string, JsonElement>? Project<TEntity>(
        TEntity entity,
        IReadOnlyList<SelectedField> selectedFields,
        JsonSerializerOptions jsonOptions)
    {
        if (selectedFields.Count == 0)
        {
            return null;
        }

        var accessorMap = GetOrBuildAccessorMap(typeof(TEntity), jsonOptions);

        // Fall back to serialize-then-pick for types with class-level JsonConverter
        // or when selecting a large fraction of properties (cheaper to serialize once)
        if (accessorMap.RequiresSerializeFallback ||
            ShouldUseSerializeFallback(selectedFields.Count, accessorMap.PropertyCount))
        {
            return SerializeThenPick(entity, selectedFields, jsonOptions);
        }

        var result = new Dictionary<string, JsonElement>(selectedFields.Count);

        foreach (var field in selectedFields)
        {
            if (accessorMap.TryGetAccessor(field.PropertyName, out var accessor))
            {
                var value = accessor.GetValue(entity!);
                var element = JsonSerializer.SerializeToElement(value, accessor.PropertyType, jsonOptions);
                result[field.QueryFieldName] = element;
            }
        }

        return result;
    }

    /// <summary>
    /// Projects a list of entities to dictionaries containing only the selected fields.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="entities">The entities to project.</param>
    /// <param name="selectedFields">The fields to include.</param>
    /// <param name="jsonOptions">The JSON serializer options (for naming policy).</param>
    /// <returns>A list of projected dictionaries, or null if no projection needed.</returns>
    internal static IReadOnlyList<Dictionary<string, JsonElement>>? ProjectMany<TEntity>(
        IReadOnlyList<TEntity> entities,
        IReadOnlyList<SelectedField> selectedFields,
        JsonSerializerOptions jsonOptions)
    {
        if (selectedFields.Count == 0)
        {
            return null;
        }

        var results = new List<Dictionary<string, JsonElement>>(entities.Count);

        foreach (var entity in entities)
        {
            var projected = Project(entity, selectedFields, jsonOptions);
            if (projected is not null)
            {
                results.Add(projected);
            }
        }

        return results;
    }

    /// <summary>
    /// Determines whether the serialize-then-pick approach should be used
    /// based on the ratio of selected fields to total properties.
    /// </summary>
    private static bool ShouldUseSerializeFallback(int selectedCount, int totalProperties)
    {
        if (totalProperties == 0)
        {
            return true;
        }

        return (double)selectedCount / totalProperties > SerializeThresholdRatio;
    }

    /// <summary>
    /// Serialize-then-pick implementation used as fallback for types with
    /// class-level <see cref="JsonConverterAttribute"/> or dense field selections.
    /// </summary>
    private static Dictionary<string, JsonElement> SerializeThenPick<TEntity>(
        TEntity entity,
        IReadOnlyList<SelectedField> selectedFields,
        JsonSerializerOptions jsonOptions)
    {
        var json = JsonSerializer.Serialize(entity, jsonOptions);
        using var doc = JsonDocument.Parse(json);

        var result = new Dictionary<string, JsonElement>(selectedFields.Count);

        foreach (var field in selectedFields)
        {
            if (doc.RootElement.TryGetProperty(field.QueryFieldName, out var value))
            {
                result[field.QueryFieldName] = value.Clone();
            }
        }

        return result;
    }

    private static PropertyAccessorMap GetOrBuildAccessorMap(Type entityType, JsonSerializerOptions jsonOptions)
    {
        return AccessorCache.GetOrAdd(entityType, type => PropertyAccessorMap.Build(type, jsonOptions));
    }

    /// <summary>
    /// Cached map of property accessors for a given entity type.
    /// </summary>
    private sealed class PropertyAccessorMap
    {
        private readonly Dictionary<string, PropertyAccessor> _accessors;

        private PropertyAccessorMap(Dictionary<string, PropertyAccessor> accessors, bool requiresSerializeFallback)
        {
            _accessors = accessors;
            RequiresSerializeFallback = requiresSerializeFallback;
            PropertyCount = accessors.Count;
        }

        /// <summary>
        /// Gets a value indicating whether this type requires the serialize-then-pick fallback.
        /// </summary>
        public bool RequiresSerializeFallback { get; }

        /// <summary>
        /// Gets the total number of serializable properties on the entity type.
        /// </summary>
        public int PropertyCount { get; }

        /// <summary>
        /// Builds a <see cref="PropertyAccessorMap"/> for the given entity type.
        /// </summary>
        public static PropertyAccessorMap Build(Type entityType, JsonSerializerOptions jsonOptions)
        {
            // Check for class-level JsonConverter
            var hasClassConverter = entityType.GetCustomAttribute<JsonConverterAttribute>() is not null;
            if (hasClassConverter)
            {
                return new PropertyAccessorMap([], requiresSerializeFallback: true);
            }

            var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var accessors = new Dictionary<string, PropertyAccessor>(properties.Length, StringComparer.Ordinal);

            foreach (var prop in properties)
            {
                if (!prop.CanRead)
                {
                    continue;
                }

                // Skip [JsonIgnore] properties
                var ignoreAttr = prop.GetCustomAttribute<JsonIgnoreAttribute>();
                if (ignoreAttr is not null && ignoreAttr.Condition == JsonIgnoreCondition.Always)
                {
                    continue;
                }

                // Determine the JSON property name respecting [JsonPropertyName] and naming policy
                var jsonPropNameAttr = prop.GetCustomAttribute<JsonPropertyNameAttribute>();
                var jsonName = jsonPropNameAttr?.Name
                    ?? jsonOptions.PropertyNamingPolicy?.ConvertName(prop.Name)
                    ?? prop.Name;

                // Build compiled getter
                var getter = CompileGetter(entityType, prop);

                accessors[prop.Name] = new PropertyAccessor(getter, prop.PropertyType, jsonName);
            }

            return new PropertyAccessorMap(accessors, requiresSerializeFallback: false);
        }

        /// <summary>
        /// Tries to get a <see cref="PropertyAccessor"/> by C# property name.
        /// </summary>
        public bool TryGetAccessor(string propertyName, out PropertyAccessor accessor)
        {
            return _accessors.TryGetValue(propertyName, out accessor!);
        }

        /// <summary>
        /// Compiles a fast getter delegate for a property using expression trees.
        /// </summary>
        private static Func<object, object?> CompileGetter(Type entityType, PropertyInfo property)
        {
            // (object entity) => (object?)((TEntity)entity).PropertyName
            var parameter = Expression.Parameter(typeof(object), "entity");
            var castEntity = Expression.Convert(parameter, entityType);
            var propertyAccess = Expression.Property(castEntity, property);
            var castResult = Expression.Convert(propertyAccess, typeof(object));

            return Expression.Lambda<Func<object, object?>>(castResult, parameter).Compile();
        }
    }

    /// <summary>
    /// Holds the compiled getter and metadata for a single property.
    /// </summary>
    private sealed class PropertyAccessor
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PropertyAccessor"/> class.
        /// </summary>
        /// <param name="getValue">The compiled getter delegate.</param>
        /// <param name="propertyType">The property type.</param>
        /// <param name="jsonName">The JSON property name (after naming policy).</param>
        public PropertyAccessor(Func<object, object?> getValue, Type propertyType, string jsonName)
        {
            GetValue = getValue;
            PropertyType = propertyType;
            JsonName = jsonName;
        }

        /// <summary>
        /// Gets the compiled getter delegate.
        /// </summary>
        public Func<object, object?> GetValue { get; }

        /// <summary>
        /// Gets the property type (used for serialization).
        /// </summary>
        public Type PropertyType { get; }

        /// <summary>
        /// Gets the JSON property name after applying naming policy and attributes.
        /// </summary>
        public string JsonName { get; }
    }
}
