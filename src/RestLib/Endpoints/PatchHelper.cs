using System.Text.Json;
using Microsoft.Extensions.Logging;
using RestLib.Logging;
using RestLib.Serialization;

namespace RestLib.Endpoints;

/// <summary>
/// Helper methods for JSON Merge Patch (RFC 7396) operations.
/// </summary>
internal static class PatchHelper
{
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
