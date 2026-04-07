using System.Collections.Concurrent;
using System.Reflection;

namespace RestLib.Endpoints;

/// <summary>
/// Helper methods for extracting entity keys via configured selectors or reflection.
/// </summary>
internal static class EntityKeyHelper
{
    /// <summary>
    /// Cache for reflected "Id" property lookups, keyed by entity type.
    /// Avoids repeated reflection when no explicit key selector is configured.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> IdPropertyCache = new();

    /// <summary>
    /// Extracts the key from an entity using the configured key selector or reflection.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="entity">The entity to extract the key from.</param>
    /// <param name="keySelector">An optional key selector function.</param>
    /// <returns>The extracted key value, or default if not found.</returns>
    internal static TKey? GetEntityKey<TEntity, TKey>(TEntity entity, Func<TEntity, TKey>? keySelector)
        where TEntity : class
        where TKey : notnull
    {
        if (keySelector is not null)
        {
            return keySelector(entity);
        }

        // Fall back to reflection: look for 'Id' property (cached)
        var idProperty = IdPropertyCache.GetOrAdd(typeof(TEntity), t => t.GetProperty("Id"));
        if (idProperty is not null && idProperty.PropertyType == typeof(TKey))
        {
            return (TKey?)idProperty.GetValue(entity);
        }

        return default;
    }
}
