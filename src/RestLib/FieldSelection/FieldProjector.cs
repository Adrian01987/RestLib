using System.Text.Json;

namespace RestLib.FieldSelection;

/// <summary>
/// Projects entity objects to include only selected fields.
/// </summary>
internal static class FieldProjector
{
    /// <summary>
    /// Projects a single entity to a dictionary containing only the selected fields.
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

        // Serialize entity to JSON, then parse as a document
        var json = JsonSerializer.Serialize(entity, jsonOptions);
        using var doc = JsonDocument.Parse(json);

        var result = new Dictionary<string, JsonElement>(selectedFields.Count);

        foreach (var field in selectedFields)
        {
            // The serialized JSON uses the naming policy (snake_case), so look up by query field name
            if (doc.RootElement.TryGetProperty(field.QueryFieldName, out var value))
            {
                result[field.QueryFieldName] = value.Clone();
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
}
