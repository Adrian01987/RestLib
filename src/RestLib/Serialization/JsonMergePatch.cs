using System.Buffers;
using System.Text.Json;

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
                patchProperties[matchingPatchIndex].JsonOptions);
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
                patchProperties[i].JsonOptions);
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
        var contract = targetType is null
            ? null
            : JsonObjectContract.Get(targetType, jsonOptions);

        return patch.EnumerateObject()
            .Select(property => ResolvePatchProperty(property, contract, jsonOptions))
            .ToArray();
    }

    private static ResolvedPatchProperty ResolvePatchProperty(
        JsonProperty patchProperty,
        JsonObjectContract? contract,
        JsonSerializerOptions jsonOptions)
    {
        if (contract?.IsObject != true
            || !contract.TryGetPatchMember(patchProperty.Name, out var member))
        {
            return new ResolvedPatchProperty(
                patchProperty.Name,
                patchProperty.Value,
                null,
                jsonOptions);
        }

        return new ResolvedPatchProperty(
            member.JsonName,
            patchProperty.Value,
            member.MemberType,
            member.ValueSerializerOptions);
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

        var contract = JsonObjectContract.Get(resultType, jsonOptions);
        if (!contract.IsObject)
        {
            return;
        }

        foreach (var patchProperty in patch.EnumerateObject())
        {
            if (!contract.TryGetPatchMember(patchProperty.Name, out var member))
            {
                continue;
            }

            if (!member.CanDeserialize)
            {
                continue;
            }

            if (patchProperty.Value.ValueKind == JsonValueKind.Null)
            {
                member.SetValue(result, GetDefaultValue(member.MemberType));
                continue;
            }

            if (patchProperty.Value.ValueKind == JsonValueKind.Object)
            {
                ApplyRemovedMemberDefaults(
                    member.GetValue(result),
                    member.MemberType,
                    patchProperty.Value,
                    member.ValueSerializerOptions);
            }
        }
    }

    private static object? GetDefaultValue(Type type) =>
        type.IsValueType ? Activator.CreateInstance(type) : null;

    private readonly record struct ResolvedPatchProperty(
        string Name,
        JsonElement Value,
        Type? PropertyType,
        JsonSerializerOptions JsonOptions);
}
