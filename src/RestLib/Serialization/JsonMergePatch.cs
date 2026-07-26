using System.Buffers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace RestLib.Serialization;

/// <summary>
/// Applies JSON Merge Patch documents according to RFC 7396 while preserving
/// the serializer's raw JSON representation.
/// </summary>
internal static class JsonMergePatch
{
    /// <summary>
    /// Applies a merge patch to a typed value by merging its serialized JSON
    /// representation and deserializing the result with the same options.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="target">The current value.</param>
    /// <param name="patchDocument">The JSON Merge Patch document.</param>
    /// <param name="jsonOptions">The serializer options used for both directions.</param>
    /// <returns>The patched value, or <c>null</c> when the merged representation is JSON null.</returns>
    internal static T? Apply<T>(
        T target,
        JsonElement patchDocument,
        JsonSerializerOptions jsonOptions)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(jsonOptions);

        var targetJson = JsonSerializer.SerializeToUtf8Bytes(target, jsonOptions);
        using var targetDocument = JsonDocument.Parse(targetJson);
        var mergedJson = MergeToUtf8Bytes(
            targetDocument.RootElement,
            patchDocument,
            typeof(T),
            jsonOptions);

        var result = JsonSerializer.Deserialize<T>(mergedJson, jsonOptions);
        ApplyRemovedMemberDefaults(result, typeof(T), patchDocument, jsonOptions);
        return result;
    }

    /// <summary>
    /// Applies a merge patch to a value whose type is known at runtime.
    /// </summary>
    /// <param name="target">The current value.</param>
    /// <param name="targetType">The declared value type.</param>
    /// <param name="patchDocument">The JSON Merge Patch document.</param>
    /// <param name="jsonOptions">The serializer options used for both directions.</param>
    /// <returns>The patched value.</returns>
    internal static object? Apply(
        object? target,
        Type targetType,
        JsonElement patchDocument,
        JsonSerializerOptions jsonOptions)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        ArgumentNullException.ThrowIfNull(jsonOptions);

        var targetJson = JsonSerializer.SerializeToUtf8Bytes(target, targetType, jsonOptions);
        using var targetDocument = JsonDocument.Parse(targetJson);
        var mergedJson = MergeToUtf8Bytes(
            targetDocument.RootElement,
            patchDocument,
            targetType,
            jsonOptions);

        var result = JsonSerializer.Deserialize(mergedJson, targetType, jsonOptions);
        ApplyRemovedMemberDefaults(result, targetType, patchDocument, jsonOptions);
        return result;
    }

    private static byte[] MergeToUtf8Bytes(
        JsonElement target,
        JsonElement patch,
        Type targetType,
        JsonSerializerOptions jsonOptions)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteMergedValue(writer, target, patch, targetType, jsonOptions);
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteMergedValue(
        Utf8JsonWriter writer,
        JsonElement target,
        JsonElement patch,
        Type? targetType,
        JsonSerializerOptions jsonOptions)
    {
        if (patch.ValueKind != JsonValueKind.Object)
        {
            patch.WriteTo(writer);
            return;
        }

        var targetProperties = target.ValueKind == JsonValueKind.Object
            ? target.EnumerateObject().ToArray()
            : [];
        var patchProperties = ResolvePatchProperties(patch, targetType, jsonOptions);
        var consumedPatches = new bool[patchProperties.Length];

        writer.WriteStartObject();

        foreach (var targetProperty in targetProperties)
        {
            var matchingPatchIndex = FindLastMatchingProperty(
                targetProperty.Name,
                patchProperties);
            if (matchingPatchIndex < 0)
            {
                targetProperty.WriteTo(writer);
                continue;
            }

            MarkMatchingPropertiesConsumed(
                targetProperty.Name,
                patchProperties,
                consumedPatches);
            var patchValue = patchProperties[matchingPatchIndex].Value;
            if (patchValue.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            writer.WritePropertyName(targetProperty.Name);
            WriteMergedValue(
                writer,
                targetProperty.Value,
                patchValue,
                patchProperties[matchingPatchIndex].PropertyType,
                jsonOptions);
        }

        for (var i = 0; i < patchProperties.Length; i++)
        {
            if (consumedPatches[i] || patchProperties[i].Value.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            writer.WritePropertyName(patchProperties[i].Name);
            WriteMergedValue(
                writer,
                default,
                patchProperties[i].Value,
                patchProperties[i].PropertyType,
                jsonOptions);
        }

        writer.WriteEndObject();
    }

    private static int FindLastMatchingProperty(
        string targetPropertyName,
        IReadOnlyList<ResolvedPatchProperty> patchProperties)
    {
        for (var i = patchProperties.Count - 1; i >= 0; i--)
        {
            if (targetPropertyName.Equals(patchProperties[i].Name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static void MarkMatchingPropertiesConsumed(
        string targetPropertyName,
        IReadOnlyList<ResolvedPatchProperty> patchProperties,
        bool[] consumedPatches)
    {
        for (var i = 0; i < patchProperties.Count; i++)
        {
            if (targetPropertyName.Equals(patchProperties[i].Name, StringComparison.Ordinal))
            {
                consumedPatches[i] = true;
            }
        }
    }

    private static ResolvedPatchProperty[] ResolvePatchProperties(
        JsonElement patch,
        Type? targetType,
        JsonSerializerOptions jsonOptions)
    {
        JsonTypeInfo? typeInfo = null;
        if (targetType is not null)
        {
            typeInfo = jsonOptions.GetTypeInfo(targetType);
        }

        return patch.EnumerateObject()
            .Select(property => ResolvePatchProperty(property, typeInfo, jsonOptions))
            .ToArray();
    }

    private static ResolvedPatchProperty ResolvePatchProperty(
        JsonProperty patchProperty,
        JsonTypeInfo? typeInfo,
        JsonSerializerOptions jsonOptions)
    {
        if (typeInfo?.Kind != JsonTypeInfoKind.Object)
        {
            return new ResolvedPatchProperty(patchProperty.Name, patchProperty.Value, null);
        }

        var propertyInfo = FindProperty(typeInfo.Properties, patchProperty.Name, jsonOptions);
        return propertyInfo is null
            ? new ResolvedPatchProperty(patchProperty.Name, patchProperty.Value, null)
            : new ResolvedPatchProperty(propertyInfo.Name, patchProperty.Value, propertyInfo.PropertyType);
    }

    private static JsonPropertyInfo? FindProperty(
        IList<JsonPropertyInfo> properties,
        string patchPropertyName,
        JsonSerializerOptions jsonOptions)
    {
        var exactMatch = properties.FirstOrDefault(property =>
            property.Name.Equals(patchPropertyName, StringComparison.Ordinal));
        if (exactMatch is not null)
        {
            return exactMatch;
        }

        if (jsonOptions.PropertyNameCaseInsensitive)
        {
            var caseInsensitiveMatch = properties.FirstOrDefault(property =>
                property.Name.Equals(patchPropertyName, StringComparison.OrdinalIgnoreCase));
            if (caseInsensitiveMatch is not null)
            {
                return caseInsensitiveMatch;
            }
        }

        return properties.FirstOrDefault(property =>
        {
            var memberName = (property.AttributeProvider as MemberInfo)?.Name;
            return NameMatchesClrMember(patchPropertyName, property.Name, memberName);
        });
    }

    private static void ApplyRemovedMemberDefaults(
        object? result,
        Type resultType,
        JsonElement patch,
        JsonSerializerOptions jsonOptions)
    {
        if (result is null || patch.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var typeInfo = jsonOptions.GetTypeInfo(resultType);
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        foreach (var patchProperty in patch.EnumerateObject())
        {
            var propertyInfo = FindProperty(typeInfo.Properties, patchProperty.Name, jsonOptions);
            if (propertyInfo is null)
            {
                continue;
            }

            if (patchProperty.Value.ValueKind == JsonValueKind.Null)
            {
                propertyInfo.Set?.Invoke(result, GetDefaultValue(propertyInfo.PropertyType));
                continue;
            }

            if (patchProperty.Value.ValueKind == JsonValueKind.Object &&
                propertyInfo.Get is not null)
            {
                ApplyRemovedMemberDefaults(
                    propertyInfo.Get(result),
                    propertyInfo.PropertyType,
                    patchProperty.Value,
                    jsonOptions);
            }
        }
    }

    private static object? GetDefaultValue(Type type) =>
        type.IsValueType ? Activator.CreateInstance(type) : null;

    private static bool NameMatchesClrMember(
        string patchPropertyName,
        string serializedPropertyName,
        string? memberName)
    {
        if (memberName is not null &&
            memberName.Equals(patchPropertyName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalizedPatchName = RemoveUnderscores(patchPropertyName);
        return normalizedPatchName.Equals(
                   RemoveUnderscores(serializedPropertyName),
                   StringComparison.OrdinalIgnoreCase) ||
               (memberName is not null &&
                normalizedPatchName.Equals(
                    RemoveUnderscores(memberName),
                    StringComparison.OrdinalIgnoreCase));
    }

    private static string RemoveUnderscores(string value) =>
        value.Replace("_", string.Empty, StringComparison.Ordinal);

    private readonly record struct ResolvedPatchProperty(
        string Name,
        JsonElement Value,
        Type? PropertyType);
}
