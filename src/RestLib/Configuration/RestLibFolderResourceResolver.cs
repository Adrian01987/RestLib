using System.Reflection;

namespace RestLib.Configuration;

/// <summary>
/// Resolves CLR types for folder-loaded JSON resource files.
/// </summary>
internal readonly record struct RestLibResolvedResourceTypes(
    Type ApiType,
    Type DbType,
    Type KeyType,
    bool HasSeparateDbType);

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
    /// <returns>The resolved API, DB, and key CLR types.</returns>
    internal static RestLibResolvedResourceTypes Resolve(
        string fileName,
        RestLibJsonResourceConfiguration configuration,
        RestLibFolderOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);

        if (options.TypeResolver is not null)
        {
            var (entityType, keyType) = options.TypeResolver(fileName, configuration);
            var resolvedDbType = ResolveDbType(fileName, configuration, options) ?? entityType;
            ValidateDbKeyConfiguration(fileName, configuration, resolvedDbType, keyType);
            return new RestLibResolvedResourceTypes(entityType, resolvedDbType, keyType, entityType != resolvedDbType);
        }

        var apiType = ResolveEntityType(fileName, configuration, options);
        var dbType = ResolveDbType(fileName, configuration, options) ?? apiType;

        if (configuration.Key is not null)
        {
            var keyProperties = ResolveCompositeKeyProperties(fileName, configuration, apiType);
            var keyType = typeof(RestLibCompositeKey<,>).MakeGenericType(
                keyProperties[0].PropertyType,
                keyProperties[1].PropertyType);
            ValidateDbCompositeKeyProperties(fileName, configuration, dbType, keyType);
            return new RestLibResolvedResourceTypes(apiType, dbType, keyType, apiType != dbType);
        }

        var keyProperty = ResolveKeyProperty(fileName, configuration, apiType);

        ValidateDbKeyProperty(fileName, configuration, dbType, keyProperty.Name, keyProperty.PropertyType);

        return new RestLibResolvedResourceTypes(apiType, dbType, keyProperty.PropertyType, apiType != dbType);
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

    private static Type? ResolveDbType(
        string fileName,
        RestLibJsonResourceConfiguration configuration,
        RestLibFolderOptions options)
    {
        var dbTypeName = configuration.Mapping?.DbType;
        if (string.IsNullOrWhiteSpace(dbTypeName))
        {
            if (configuration.Mapping is not null)
            {
                throw new InvalidOperationException(
                    $"Resource '{configuration.Name}' in JSON file '{fileName}' declares a Mapping section but does not set Mapping.DbType.");
            }

            return null;
        }

        return ResolveConfiguredType(fileName, configuration.Name, dbTypeName, "DB model", options);
    }

    private static Type ResolveConfiguredType(
        string fileName,
        string resourceName,
        string configuredTypeName,
        string typeRole,
        RestLibFolderOptions options)
    {
        var declaredType = Type.GetType(configuredTypeName, throwOnError: false);
        if (declaredType is not null)
        {
            return declaredType;
        }

        var split = configuredTypeName.Split(',', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
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
            $"Could not resolve {typeRole} type '{configuredTypeName}' for resource '{resourceName}' in JSON file '{fileName}'.");
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

    private static PropertyInfo[] ResolveCompositeKeyProperties(
        string fileName,
        RestLibJsonResourceConfiguration configuration,
        Type entityType)
    {
        var keyConfiguration = configuration.Key
            ?? throw new InvalidOperationException(
                $"Resource '{configuration.Name}' in JSON file '{fileName}' does not configure Key.");

        if (keyConfiguration.Properties.Count != 2)
        {
            throw new InvalidOperationException(
                $"Resource '{configuration.Name}' in JSON file '{fileName}' must configure exactly two Key.Properties values.");
        }

        if (keyConfiguration.RouteParameters.Count != 2)
        {
            throw new InvalidOperationException(
                $"Resource '{configuration.Name}' in JSON file '{fileName}' must configure exactly two Key.RouteParameters values.");
        }

        return
        [
            ResolveNamedProperty(fileName, configuration, entityType, keyConfiguration.Properties[0]),
            ResolveNamedProperty(fileName, configuration, entityType, keyConfiguration.Properties[1])
        ];
    }

    private static PropertyInfo ResolveNamedProperty(
        string fileName,
        RestLibJsonResourceConfiguration configuration,
        Type entityType,
        string propertyName)
    {
        var property = entityType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property is null)
        {
            throw new InvalidOperationException(
                $"Could not resolve key property '{propertyName}' for resource '{configuration.Name}' in JSON file '{fileName}' on entity type '{entityType.FullName}'.");
        }

        if (property.GetMethod is null || !property.GetMethod.IsPublic)
        {
            throw new InvalidOperationException(
                $"Key property '{property.Name}' for resource '{configuration.Name}' in JSON file '{fileName}' is not publicly readable on entity type '{entityType.FullName}'.");
        }

        return property;
    }

    private static void ValidateDbKeyConfiguration(
        string fileName,
        RestLibJsonResourceConfiguration configuration,
        Type dbType,
        Type keyType)
    {
        if (configuration.Key is not null)
        {
            ValidateDbCompositeKeyProperties(fileName, configuration, dbType, keyType);
            return;
        }

        ValidateDbKeyProperty(fileName, configuration, dbType, keyType);
    }

    private static void ValidateDbCompositeKeyProperties(
        string fileName,
        RestLibJsonResourceConfiguration configuration,
        Type dbType,
        Type keyType)
    {
        if (!keyType.IsGenericType || keyType.GetGenericTypeDefinition() != typeof(RestLibCompositeKey<,>))
        {
            throw new InvalidOperationException(
                $"Resource '{configuration.Name}' in JSON file '{fileName}' configures a composite Key, but resolved key type '{keyType.FullName}' is not RestLibCompositeKey<TFirst, TSecond>.");
        }

        var keyConfiguration = configuration.Key
            ?? throw new InvalidOperationException(
                $"Resource '{configuration.Name}' in JSON file '{fileName}' does not configure Key.");
        var keyPartTypes = keyType.GetGenericArguments();
        for (var index = 0; index < keyConfiguration.Properties.Count; index++)
        {
            ValidateDbKeyProperty(
                fileName,
                configuration,
                dbType,
                keyConfiguration.Properties[index],
                keyPartTypes[index]);
        }
    }

    private static void ValidateDbKeyProperty(
        string fileName,
        RestLibJsonResourceConfiguration configuration,
        Type dbType,
        Type keyType)
    {
        ValidateDbKeyProperty(
            fileName,
            configuration,
            dbType,
            string.IsNullOrWhiteSpace(configuration.KeyProperty) ? "Id" : configuration.KeyProperty!,
            keyType);
    }

    private static void ValidateDbKeyProperty(
        string fileName,
        RestLibJsonResourceConfiguration configuration,
        Type dbType,
        string keyPropertyName,
        Type keyType)
    {
        var property = dbType.GetProperty(keyPropertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property is null)
        {
            throw new InvalidOperationException(
                $"Could not resolve key property '{keyPropertyName}' for resource '{configuration.Name}' in JSON file '{fileName}' on DB model type '{dbType.FullName}'.");
        }

        if (property.GetMethod is null || !property.GetMethod.IsPublic)
        {
            throw new InvalidOperationException(
                $"Key property '{property.Name}' for resource '{configuration.Name}' in JSON file '{fileName}' is not publicly readable on DB model type '{dbType.FullName}'.");
        }

        if (property.PropertyType != keyType)
        {
            throw new InvalidOperationException(
                $"Key property '{property.Name}' for resource '{configuration.Name}' in JSON file '{fileName}' must be of type '{keyType.FullName}' on DB model type '{dbType.FullName}'.");
        }
    }
}
