using System.Text.Json;
using Microsoft.Extensions.Logging;
using RestLib.Configuration;
using RestLib.Logging;
using RestLib.Serialization;

namespace RestLib.Endpoints;

/// <summary>
/// Helper methods for JSON Merge Patch (RFC 7396) operations.
/// </summary>
internal static class PatchHelper
{
    /// <summary>
    /// Determines whether a merge-patch document attempts to modify a configured
    /// resource-key property.
    /// </summary>
    /// <typeparam name="TEntity">The representation type.</typeparam>
    /// <typeparam name="TKey">The resource key type.</typeparam>
    /// <param name="patchDocument">The JSON merge-patch document.</param>
    /// <param name="keyRouteParts">The configured key-route parts.</param>
    /// <param name="jsonOptions">The resource JSON serializer options.</param>
    /// <param name="patchPropertyName">The offending JSON property name.</param>
    /// <returns><c>true</c> when the document contains a key property.</returns>
    internal static bool TryGetPatchedKeyProperty<TEntity, TKey>(
        JsonElement patchDocument,
        IReadOnlyList<RestLibKeyRoutePart<TKey>> keyRouteParts,
        JsonSerializerOptions jsonOptions,
        out string? patchPropertyName)
        where TEntity : class
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(keyRouteParts);
        ArgumentNullException.ThrowIfNull(jsonOptions);

        patchPropertyName = null;
        if (patchDocument.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var keyPropertyNames = EntityKeyHelper.GetEntityKeyPropertyNames<TEntity, TKey>(keyRouteParts);
        if (keyPropertyNames.Count == 0)
        {
            return false;
        }

        var keyNameSet = keyPropertyNames.ToHashSet(StringComparer.Ordinal);
        var contract = JsonObjectContract.Get(typeof(TEntity), jsonOptions);

        foreach (var patchProperty in patchDocument.EnumerateObject())
        {
            if (contract.TryGetPatchMember(patchProperty.Name, out var member)
                && member.ClrName is not null
                && keyNameSet.Contains(member.ClrName))
            {
                patchPropertyName = patchProperty.Name;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Formats the client-facing error used when a merge patch names a key field.
    /// </summary>
    /// <param name="propertyName">The offending JSON property name.</param>
    /// <returns>The validation error detail.</returns>
    internal static string KeyModificationError(string propertyName) =>
        $"PATCH cannot modify immutable resource key field '{propertyName}'.";

    /// <summary>
    /// Previews a JSON Merge Patch (RFC 7396) by merging the patch document into
    /// the original entity without persisting. The merged result can be validated
    /// before the actual <c>PatchAsync</c> call.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="original">The current entity from the repository.</param>
    /// <param name="patchDocument">The JSON patch document.</param>
    /// <param name="jsonOptions">The JSON serializer options.</param>
    /// <param name="logger">Optional logger for recording deserialization failures.</param>
    /// <returns>The merged entity, or <c>null</c> if deserialization fails.</returns>
    internal static TEntity? PreviewPatch<TEntity>(
        TEntity original,
        JsonElement patchDocument,
        JsonSerializerOptions jsonOptions,
        ILogger? logger = null)
        where TEntity : class
    {
        if (patchDocument.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        try
        {
            var result = JsonMergePatch.Apply(original, patchDocument, jsonOptions);

            if (result is null && logger is not null)
            {
                RestLibLogMessages.PatchPreviewDeserializationNull(logger);
            }

            return result;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException)
        {
            if (logger is not null)
            {
                RestLibLogMessages.JsonDeserializationFailed(logger, ex);
            }

            return null;
        }
    }
}
