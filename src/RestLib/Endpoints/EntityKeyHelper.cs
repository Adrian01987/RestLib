using System.Collections.Concurrent;
using System.Reflection;
using RestLib.Configuration;
using RestLib.Internal;

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
    /// Cache for writable key property lookups used when RestLib needs to assign
    /// a route key back onto an entity instance.
    /// </summary>
    private static readonly ConcurrentDictionary<(Type EntityType, Type KeyType, string? PreferredPropertyName), PropertyInfo?> WritableKeyPropertyCache = new();

    /// <summary>
    /// Validates at registration time that a key can be extracted from <typeparamref name="TEntity"/>.
    /// Throws <see cref="InvalidOperationException"/> when neither a key selector is configured
    /// nor a public <c>Id</c> property of type <typeparamref name="TKey"/> exists.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="keySelector">An optional key selector function.</param>
    internal static void ValidateKeyExtraction<TEntity, TKey>(Func<TEntity, TKey>? keySelector)
        where TEntity : class
        where TKey : notnull
    {
        if (keySelector is not null)
        {
            return;
        }

        var idProperty = IdPropertyCache.GetOrAdd(typeof(TEntity), t => t.GetProperty("Id"));
        if (idProperty is not null && idProperty.PropertyType == typeof(TKey))
        {
            return;
        }

        throw new InvalidOperationException(
            $"RestLib cannot extract a key from entity type '{typeof(TEntity).Name}'. " +
            $"No 'Id' property of type '{typeof(TKey).Name}' was found and no KeySelector was configured. " +
            $"Either add a public 'Id' property of type '{typeof(TKey).Name}' to '{typeof(TEntity).Name}', " +
            $"or set KeySelector in the endpoint configuration (e.g., cfg.KeySelector = e => e.YourKeyProperty).");
    }

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

    /// <summary>
    /// Attempts to assign the key value onto an entity using a configured key
    /// property, the conventional <c>Id</c> property, or a single writable key-
    /// typed property when that is unambiguous.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="entity">The entity to update.</param>
    /// <param name="key">The key value to assign.</param>
    /// <param name="preferredPropertyName">The preferred key property name, if known.</param>
    /// <returns><c>true</c> if the key was assigned; otherwise <c>false</c>.</returns>
    internal static bool TrySetEntityKey<TEntity, TKey>(
        TEntity entity,
        TKey key,
        string? preferredPropertyName = null)
        where TEntity : class
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(entity);

        var property = WritableKeyPropertyCache.GetOrAdd(
            (typeof(TEntity), typeof(TKey), preferredPropertyName),
            static entry => ResolveWritableKeyProperty(entry.EntityType, entry.KeyType, entry.PreferredPropertyName));

        if (property is null)
        {
            return false;
        }

        property.SetValue(entity, key);
        return true;
    }

    /// <summary>
    /// Attempts to assign each configured key part back onto an entity instance.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="entity">The entity to update.</param>
    /// <param name="key">The key value to assign.</param>
    /// <param name="keyRouteParts">The configured key-route parts.</param>
    /// <returns><c>true</c> if all key parts were assigned; otherwise <c>false</c>.</returns>
    internal static bool TrySetEntityKeyParts<TEntity, TKey>(
        TEntity entity,
        TKey key,
        IReadOnlyList<RestLibKeyRoutePart<TKey>> keyRouteParts)
        where TEntity : class
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(keyRouteParts);

        if (keyRouteParts.Count <= 1)
        {
            return TrySetEntityKey(entity, key, keyRouteParts.FirstOrDefault()?.PropertyName);
        }

        var assignedAny = false;
        foreach (var keyPart in keyRouteParts)
        {
            var property = entity.GetType().GetProperty(
                keyPart.PropertyName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (property is null || !property.CanWrite || property.PropertyType != keyPart.PropertyType)
            {
                return false;
            }

            property.SetValue(entity, keyPart.GetKeyPartValue(key));
            assignedAny = true;
        }

        return assignedAny;
    }

    /// <summary>
    /// Formats a key for logs and problem details using configured route-part metadata.
    /// </summary>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="key">The key value.</param>
    /// <param name="keyRouteParts">The configured key-route parts.</param>
    /// <returns>The formatted key string.</returns>
    internal static string FormatKeyForDisplay<TKey>(TKey key, IReadOnlyList<RestLibKeyRoutePart<TKey>> keyRouteParts)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(keyRouteParts);

        if (keyRouteParts.Count <= 1)
        {
            return RestLibKeyConversion.FormatDisplayValue(key);
        }

        return string.Join(
            ", ",
            keyRouteParts.Select(part =>
                $"{part.RouteParameterName}='{RestLibKeyConversion.FormatDisplayValue(part.GetKeyPartValue(key))}'"));
    }

    /// <summary>
    /// Formats a key as a route suffix suitable for Location headers and HATEOAS links.
    /// </summary>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="key">The key value.</param>
    /// <param name="keyRouteParts">The configured key-route parts.</param>
    /// <returns>The escaped path suffix beginning with '/'.</returns>
    internal static string FormatKeyPath<TKey>(TKey key, IReadOnlyList<RestLibKeyRoutePart<TKey>> keyRouteParts)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(keyRouteParts);

        if (keyRouteParts.Count == 0)
        {
            return string.Empty;
        }

        if (keyRouteParts.Count == 1)
        {
            return $"/{RestLibKeyConversion.FormatRouteSegment(key)}";
        }

        return string.Concat(keyRouteParts.Select(part =>
            $"/{RestLibKeyConversion.FormatRouteSegment(part.GetKeyPartValue(key))}"));
    }

    private static PropertyInfo? ResolveWritableKeyProperty(
        Type entityType,
        Type keyType,
        string? preferredPropertyName)
    {
        if (!string.IsNullOrWhiteSpace(preferredPropertyName))
        {
            var preferredProperty = entityType.GetProperty(
                preferredPropertyName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (preferredProperty is null)
            {
                return null;
            }

            return preferredProperty.CanWrite && preferredProperty.PropertyType == keyType
                ? preferredProperty
                : null;
        }

        var idProperty = entityType.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
        if (idProperty is not null && idProperty.CanWrite && idProperty.PropertyType == keyType)
        {
            return idProperty;
        }

        var candidates = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanWrite && property.PropertyType == keyType)
            .ToArray();

        return candidates.Length == 1 ? candidates[0] : null;
    }
}
