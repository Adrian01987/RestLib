using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace RestLib.Serialization;

/// <summary>
/// Immutable view of an object's effective System.Text.Json member contract.
/// </summary>
internal sealed class JsonObjectContract
{
    private static readonly ConditionalWeakTable<
        JsonSerializerOptions,
        ConcurrentDictionary<Type, JsonObjectContract>> ContractCaches = new();

    private readonly Dictionary<string, JsonMemberContract> _membersByClrName;
    private readonly Dictionary<string, JsonMemberContract> _membersByJsonName;
    private readonly JsonSerializerOptions _jsonOptions;

    private JsonObjectContract(Type objectType, JsonSerializerOptions jsonOptions)
    {
        ObjectType = objectType;
        _jsonOptions = jsonOptions;

        var metadataOptions = jsonOptions;
        if (!jsonOptions.IsReadOnly)
        {
            metadataOptions = new JsonSerializerOptions(jsonOptions);
            metadataOptions.MakeReadOnly(populateMissingResolver: true);
        }

        var typeInfo = metadataOptions.GetTypeInfo(objectType);
        IsObject = typeInfo.Kind == JsonTypeInfoKind.Object;
        if (!IsObject)
        {
            Members = [];
            _membersByClrName = new Dictionary<string, JsonMemberContract>(StringComparer.Ordinal);
            _membersByJsonName = new Dictionary<string, JsonMemberContract>(StringComparer.Ordinal);
            return;
        }

        var members = typeInfo.Properties
            .Select(property => new JsonMemberContract(property, jsonOptions))
            .ToList();

        // Default metadata normally retains ignored properties with disabled accessors.
        // Keep an explicit reflection fallback for resolvers that remove [JsonIgnore]
        // members so an allow-listed field selection can retain its documented override.
        var representedClrNames = members
            .Where(member => member.ClrName is not null)
            .Select(member => member.ClrName!)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var property in objectType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (representedClrNames.Contains(property.Name)
                || property.GetCustomAttribute<JsonIgnoreAttribute>() is null)
            {
                continue;
            }

            members.Add(JsonMemberContract.CreateIgnoredFallback(property, jsonOptions));
        }

        Members = members.ToArray();
        _membersByClrName = Members
            .Where(member => member.ClrName is not null)
            .GroupBy(member => member.ClrName!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        _membersByJsonName = Members
            .GroupBy(member => member.JsonName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    }

    /// <summary>
    /// Gets a value indicating whether the serializer exposes an object member contract.
    /// A converter-owned representation has no independently addressable members.
    /// </summary>
    internal bool IsObject { get; }

    /// <summary>
    /// Gets the CLR type represented by this contract.
    /// </summary>
    internal Type ObjectType { get; }

    /// <summary>
    /// Gets the immutable members exposed by the effective JSON contract.
    /// </summary>
    internal IReadOnlyList<JsonMemberContract> Members { get; }

    /// <summary>
    /// Gets the effective contract for a CLR type and serializer instance.
    /// Read-only serializer options use an identity-scoped cache; mutable test or
    /// low-level options are rebuilt so later option mutations cannot reuse stale metadata.
    /// </summary>
    internal static JsonObjectContract Get(Type objectType, JsonSerializerOptions jsonOptions)
    {
        ArgumentNullException.ThrowIfNull(objectType);
        ArgumentNullException.ThrowIfNull(jsonOptions);

        if (!jsonOptions.IsReadOnly)
        {
            return new JsonObjectContract(objectType, jsonOptions);
        }

        var cache = ContractCaches.GetValue(
            jsonOptions,
            static _ => new ConcurrentDictionary<Type, JsonObjectContract>());
        return cache.GetOrAdd(objectType, type => new JsonObjectContract(type, jsonOptions));
    }

    /// <summary>
    /// Resolves a member by its CLR property name.
    /// </summary>
    internal bool TryGetMemberByClrName(string clrName, out JsonMemberContract member) =>
        _membersByClrName.TryGetValue(clrName, out member!);

    /// <summary>
    /// Resolves a member by its canonical JSON name using the configured case policy.
    /// </summary>
    internal bool TryGetMemberByJsonName(string jsonName, out JsonMemberContract member)
    {
        if (_membersByJsonName.TryGetValue(jsonName, out member!))
        {
            return true;
        }

        if (!_jsonOptions.PropertyNameCaseInsensitive)
        {
            member = null!;
            return false;
        }

        member = Members.FirstOrDefault(candidate =>
            candidate.JsonName.Equals(jsonName, StringComparison.OrdinalIgnoreCase))!;
        return member is not null;
    }

    /// <summary>
    /// Resolves a PATCH member. Canonical JSON names are authoritative. When the
    /// serializer accepts names case-insensitively, legacy CLR/snake/camel aliases
    /// remain accepted for backward compatibility across every PATCH implementation.
    /// </summary>
    internal bool TryGetPatchMember(string patchName, out JsonMemberContract member)
    {
        if (TryGetMemberByJsonName(patchName, out member))
        {
            return true;
        }

        if (!_jsonOptions.PropertyNameCaseInsensitive)
        {
            return false;
        }

        var normalizedPatchName = RemoveUnderscores(patchName);
        member = Members.FirstOrDefault(candidate =>
            candidate.ClrName is not null
            && (candidate.ClrName.Equals(patchName, StringComparison.OrdinalIgnoreCase)
                || RemoveUnderscores(candidate.ClrName).Equals(
                    normalizedPatchName,
                    StringComparison.OrdinalIgnoreCase)
                || RemoveUnderscores(candidate.JsonName).Equals(
                    normalizedPatchName,
                    StringComparison.OrdinalIgnoreCase)))!;
        return member is not null;
    }

    /// <summary>
    /// Resolves a dotted CLR property path to its effective JSON member path.
    /// </summary>
    internal bool TryResolveClrPath(string clrPath, out JsonMemberPath memberPath)
    {
        var segments = clrPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            memberPath = null!;
            return false;
        }

        var members = new List<JsonMemberContract>(segments.Length);
        var contract = this;
        foreach (var segment in segments)
        {
            if (!contract.IsObject || !contract.TryGetMemberByClrName(segment, out var member))
            {
                memberPath = null!;
                return false;
            }

            members.Add(member);
            contract = Get(member.MemberType, _jsonOptions);
        }

        memberPath = new JsonMemberPath(members);
        return true;
    }

    private static string RemoveUnderscores(string value) =>
        value.Replace("_", string.Empty, StringComparison.Ordinal);
}

/// <summary>
/// Effective metadata for one JSON object member.
/// </summary>
internal sealed class JsonMemberContract
{
    private readonly JsonPropertyInfo? _jsonProperty;

    /// <summary>
    /// Initializes effective metadata for a serializer-provided JSON property.
    /// </summary>
    /// <param name="jsonProperty">The serializer property metadata.</param>
    /// <param name="jsonOptions">The serializer options that produced the metadata.</param>
    internal JsonMemberContract(JsonPropertyInfo jsonProperty, JsonSerializerOptions jsonOptions)
    {
        _jsonProperty = jsonProperty;
        ClrMember = jsonProperty.AttributeProvider as MemberInfo;
        JsonName = jsonProperty.Name;
        MemberType = jsonProperty.PropertyType;
        ValueSerializerOptions = CreateValueSerializerOptions(jsonProperty, jsonOptions);
    }

    private JsonMemberContract(
        PropertyInfo ignoredProperty,
        string jsonName,
        JsonSerializerOptions jsonOptions)
    {
        ClrMember = ignoredProperty;
        JsonName = jsonName;
        MemberType = ignoredProperty.PropertyType;
        ValueSerializerOptions = jsonOptions;
    }

    /// <summary>
    /// Gets the underlying CLR member, when the JSON metadata represents one.
    /// </summary>
    internal MemberInfo? ClrMember { get; }

    /// <summary>
    /// Gets the CLR member name, when available.
    /// </summary>
    internal string? ClrName => ClrMember?.Name;

    /// <summary>
    /// Gets the CLR property used by EF and reflection-based field selection.
    /// </summary>
    internal PropertyInfo? ClrProperty => ClrMember as PropertyInfo;

    /// <summary>
    /// Gets the canonical serialized property name.
    /// </summary>
    internal string JsonName { get; }

    /// <summary>
    /// Gets the member value type.
    /// </summary>
    internal Type MemberType { get; }

    /// <summary>
    /// Gets a value indicating whether normal JSON deserialization can set the member.
    /// </summary>
    internal bool CanDeserialize => _jsonProperty?.Set is not null;

    /// <summary>
    /// Gets a value indicating whether field selection can explicitly read the member.
    /// </summary>
    internal bool CanReadForFieldSelection =>
        _jsonProperty?.Get is not null || ClrProperty?.CanRead == true;

    /// <summary>
    /// Gets a value indicating whether detached member serialization needs member-specific metadata.
    /// </summary>
    internal bool HasMemberSerializationOverrides =>
        _jsonProperty?.CustomConverter is not null || _jsonProperty?.NumberHandling is not null;

    /// <summary>
    /// Gets serializer options that retain member-level converter and number-handling overrides.
    /// </summary>
    internal JsonSerializerOptions ValueSerializerOptions { get; }

    /// <summary>
    /// Creates metadata for an explicitly selectable [JsonIgnore] property removed by a resolver.
    /// </summary>
    internal static JsonMemberContract CreateIgnoredFallback(
        PropertyInfo property,
        JsonSerializerOptions jsonOptions)
    {
        var jsonName = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
            ?? jsonOptions.PropertyNamingPolicy?.ConvertName(property.Name)
            ?? property.Name;
        return new JsonMemberContract(property, jsonName, jsonOptions);
    }

    /// <summary>
    /// Reads the member value. Explicit field allow-listing is allowed to override [JsonIgnore].
    /// </summary>
    internal object? GetValue(object instance)
    {
        if (_jsonProperty?.Get is not null)
        {
            return _jsonProperty.Get(instance);
        }

        return ClrProperty?.GetValue(instance);
    }

    /// <summary>
    /// Sets the member through the effective JSON contract when deserialization permits it.
    /// </summary>
    /// <param name="instance">The containing object.</param>
    /// <param name="value">The value to assign.</param>
    internal void SetValue(object instance, object? value) => _jsonProperty?.Set?.Invoke(instance, value);

    private static JsonSerializerOptions CreateValueSerializerOptions(
        JsonPropertyInfo jsonProperty,
        JsonSerializerOptions jsonOptions)
    {
        if (jsonProperty.CustomConverter is null && jsonProperty.NumberHandling is null)
        {
            return jsonOptions;
        }

        var valueOptions = new JsonSerializerOptions(jsonOptions);
        if (jsonProperty.CustomConverter is not null)
        {
            valueOptions.Converters.Insert(0, jsonProperty.CustomConverter);
        }

        if (jsonProperty.NumberHandling is { } numberHandling)
        {
            valueOptions.NumberHandling = numberHandling;
        }

        valueOptions.MakeReadOnly(populateMissingResolver: true);
        return valueOptions;
    }
}

/// <summary>
/// Ordered JSON member metadata for a dotted CLR property path.
/// </summary>
internal sealed class JsonMemberPath(IReadOnlyList<JsonMemberContract> members)
{
    /// <summary>
    /// Gets the ordered members in the path.
    /// </summary>
    internal IReadOnlyList<JsonMemberContract> Members { get; } = members;

    /// <summary>
    /// Gets the canonical dotted JSON path.
    /// </summary>
    internal string JsonPath => string.Join('.', Members.Select(member => member.JsonName));

    /// <summary>
    /// Gets the leaf member.
    /// </summary>
    internal JsonMemberContract Leaf => Members[^1];

    /// <summary>
    /// Gets a value indicating whether an intermediate member converter owns the nested JSON shape.
    /// </summary>
    internal bool RequiresWholeEntitySerialization =>
        Members.Take(Members.Count - 1).Any(member => member.HasMemberSerializationOverrides);
}
