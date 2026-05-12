using System.Text.Json;
using RestLib.Configuration;

namespace RestLib.Validation;

/// <summary>
/// Runs all resource-level validation for a RestLib request.
/// </summary>
internal static class RestLibResourceValidator
{
    /// <summary>
    /// Validates an entity using Data Annotations plus any JSON-declared rules on the resource.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="entity">The entity to validate.</param>
    /// <param name="configuration">The endpoint configuration.</param>
    /// <param name="namingPolicy">The JSON naming policy for error keys.</param>
    /// <returns>The merged validation result.</returns>
    internal static EntityValidationResult Validate<TEntity, TKey>(
        TEntity entity,
        RestLibEndpointConfiguration<TEntity, TKey> configuration,
        JsonNamingPolicy? namingPolicy)
        where TEntity : class
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(configuration);

        var annotationResult = EntityValidator.Validate(entity, namingPolicy);
        if (!configuration.HasJsonValidationRules)
        {
            return annotationResult;
        }

        var jsonResult = JsonValidationRuleValidator.Validate(entity, configuration.JsonValidationRules, namingPolicy);
        if (annotationResult.IsValid && jsonResult.IsValid)
        {
            return EntityValidationResult.Success();
        }

        var merged = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        MergeErrors(annotationResult.Errors, merged);
        MergeErrors(jsonResult.Errors, merged);

        return EntityValidationResult.Failed(
            merged.ToDictionary(entry => entry.Key, entry => entry.Value.ToArray(), StringComparer.OrdinalIgnoreCase));
    }

    private static void MergeErrors(
        IReadOnlyDictionary<string, string[]> source,
        IDictionary<string, List<string>> destination)
    {
        foreach (var entry in source)
        {
            if (!destination.TryGetValue(entry.Key, out var messages))
            {
                messages = new List<string>();
                destination[entry.Key] = messages;
            }

            foreach (var message in entry.Value)
            {
                if (!messages.Contains(message, StringComparer.Ordinal))
                {
                    messages.Add(message);
                }
            }
        }
    }
}
