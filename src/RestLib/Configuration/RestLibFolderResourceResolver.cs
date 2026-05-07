using System.Reflection;

namespace RestLib.Configuration;

/// <summary>
/// Resolves CLR types for folder-loaded JSON resource files.
/// </summary>
internal static class RestLibFolderResourceResolver
{
    /// <summary>
    /// Resolves the entity and key types for a folder-loaded resource.
    /// </summary>
    /// <param name="fileName">The JSON file path.</param>
    /// <param name="configuration">The resource configuration loaded from the file.</param>
    /// <param name="options">The folder loader options.</param>
    /// <returns>The resolved entity and key CLR types.</returns>
    internal static (Type EntityType, Type KeyType) Resolve(
        string fileName,
        RestLibJsonResourceConfiguration configuration,
        RestLibFolderOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);

        if (options.TypeResolver is not null)
        {
            return options.TypeResolver(fileName, configuration);
        }

        var entityType = ResolveEntityType(fileName, configuration, options);
        var keyProperty = ResolveKeyProperty(fileName, configuration, entityType);

        return (entityType, keyProperty.PropertyType);
    }

    private static Type ResolveEntityType(
        string fileName,
        RestLibJsonResourceConfiguration configuration,
        RestLibFolderOptions options)
    {
        var resourceName = configuration.Name;

        if (!string.IsNullOrWhiteSpace(configuration.EntityType))
        {
            var declaredType = Type.GetType(configuration.EntityType, throwOnError: false);
            if (declaredType is not null)
            {
                return declaredType;
            }

            var split = configuration.EntityType.Split(',', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (split.Length == 2)
            {
                var matchingAssembly = options.Assemblies.FirstOrDefault(assembly =>
                    string.Equals(assembly.GetName().Name, split[1], StringComparison.Ordinal)
                    || string.Equals(assembly.FullName, split[1], StringComparison.Ordinal));

                var registeredAssemblyType = matchingAssembly?.GetType(split[0], throwOnError: false, ignoreCase: false);
                if (registeredAssemblyType is not null)
                {
                    return registeredAssemblyType;
                }
            }

            throw new InvalidOperationException(
                $"Could not resolve entity type '{configuration.EntityType}' for resource '{resourceName}' in JSON file '{fileName}'.");
        }

        var simpleTypeName = Path.GetFileNameWithoutExtension(fileName);
        var matches = options.Assemblies
            .Where(a => a is not null)
            .SelectMany(a => a.GetExportedTypes())
            .Where(t => string.Equals(t.Name, simpleTypeName, StringComparison.Ordinal))
            .ToList();

        if (matches.Count == 1)
        {
            return matches[0];
        }

        if (matches.Count > 1)
        {
            throw new InvalidOperationException(
                $"Multiple public CLR types matched file '{fileName}' for resource '{resourceName}': {string.Join(", ", matches.Select(t => t.FullName))}. Configure EntityType or TypeResolver to disambiguate.");
        }

        throw new InvalidOperationException(
            $"Could not resolve a CLR entity type for resource '{resourceName}' in JSON file '{fileName}'. Set 'EntityType' or configure RestLibFolderOptions.TypeResolver.");
    }

    private static PropertyInfo ResolveKeyProperty(
        string fileName,
        RestLibJsonResourceConfiguration configuration,
        Type entityType)
    {
        var keyPropertyName = string.IsNullOrWhiteSpace(configuration.KeyProperty)
            ? "Id"
            : configuration.KeyProperty;

        var property = entityType.GetProperty(keyPropertyName!, BindingFlags.Public | BindingFlags.Instance);
        if (property is null)
        {
            throw new InvalidOperationException(
                $"Could not resolve key property '{keyPropertyName}' for resource '{configuration.Name}' in JSON file '{fileName}' on entity type '{entityType.FullName}'.");
        }

        if (property.GetMethod is null || !property.GetMethod.IsPublic)
        {
            throw new InvalidOperationException(
                $"Key property '{property.Name}' for resource '{configuration.Name}' in JSON file '{fileName}' is not publicly readable on entity type '{entityType.FullName}'.");
        }

        return property;
    }
}
