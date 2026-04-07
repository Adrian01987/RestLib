using System.Text.Json;

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
    /// <returns>The merged entity, or <c>null</c> if deserialization fails.</returns>
    internal static TEntity? PreviewPatch<TEntity>(
        TEntity original,
        JsonElement patchDocument,
        JsonSerializerOptions jsonOptions)
        where TEntity : class
    {
        // Serialize original entity to a mutable dictionary
        var originalJson = JsonSerializer.SerializeToUtf8Bytes(original, jsonOptions);
        using var originalDoc = JsonDocument.Parse(originalJson);

        var merged = new Dictionary<string, JsonElement>();

        // Start with all original properties
        foreach (var prop in originalDoc.RootElement.EnumerateObject())
        {
            merged[prop.Name] = prop.Value.Clone();
        }

        // Apply patch values (overwrite originals, add new ones)
        foreach (var prop in patchDocument.EnumerateObject())
        {
            merged[prop.Name] = prop.Value.Clone();
        }

        // Serialize merged dictionary and deserialize back to entity
        var mergedJson = JsonSerializer.SerializeToUtf8Bytes(merged, jsonOptions);
        return JsonSerializer.Deserialize<TEntity>(mergedJson, jsonOptions);
    }
}
