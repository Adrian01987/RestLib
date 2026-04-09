using System.Buffers;
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
        // Serialize original entity to a JSON document
        var originalJson = JsonSerializer.SerializeToUtf8Bytes(original, jsonOptions);
        using var originalDoc = JsonDocument.Parse(originalJson);

        // Collect patch property names for O(1) lookups
        var patchPropertyNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var prop in patchDocument.EnumerateObject())
        {
            patchPropertyNames.Add(prop.Name);
        }

        // Merge original + patch into a single JSON buffer using Utf8JsonWriter
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            // Write original properties not overridden by the patch
            foreach (var prop in originalDoc.RootElement.EnumerateObject())
            {
                if (!patchPropertyNames.Contains(prop.Name))
                {
                    prop.WriteTo(writer);
                }
            }

            // Write all patch properties (overrides + additions)
            foreach (var prop in patchDocument.EnumerateObject())
            {
                prop.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        // Deserialize the merged JSON back to the entity type
        return JsonSerializer.Deserialize<TEntity>(buffer.WrittenSpan, jsonOptions);
    }
}
