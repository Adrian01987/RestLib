using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using RestLib.Hooks;

namespace RestLib.Configuration;

/// <summary>
/// Applies a <see cref="RestLibJsonResourceConfiguration"/> onto an
/// <see cref="RestLibEndpointConfiguration{TEntity, TKey}"/>, translating
/// JSON-based resource settings into the strongly-typed configuration model.
/// </summary>
internal static class RestLibJsonResourceBuilder
{
    /// <summary>
    /// Applies the JSON resource configuration to the endpoint configuration.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="endpointConfiguration">The endpoint configuration to populate.</param>
    /// <param name="jsonConfiguration">The JSON-based resource configuration to apply.</param>
    public static void Apply<TEntity, TKey>(
        RestLibEndpointConfiguration<TEntity, TKey> endpointConfiguration,
        RestLibJsonResourceConfiguration jsonConfiguration)
        where TEntity : class
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(endpointConfiguration);
        ArgumentNullException.ThrowIfNull(jsonConfiguration);

        ApplyKeySelector(endpointConfiguration, jsonConfiguration);
        ApplyAuthorization(endpointConfiguration, jsonConfiguration);
        ApplyOperationSelection(endpointConfiguration, jsonConfiguration);
        ApplyFiltering(endpointConfiguration, jsonConfiguration);
        ApplySorting(endpointConfiguration, jsonConfiguration);
        ApplyFieldSelection(endpointConfiguration, jsonConfiguration);
        ApplyBatch(endpointConfiguration, jsonConfiguration);
        ApplyRateLimiting(endpointConfiguration, jsonConfiguration);
        ApplyOpenApi(endpointConfiguration, jsonConfiguration.OpenApi);
    }

    /// <summary>
    /// Builds a <see cref="RestLibHooks{TEntity, TKey}"/> instance from the
    /// JSON hook configuration, resolving named hooks via the service provider.
    /// Returns <c>null</c> when no hooks are configured.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="services">The service provider for resolving named hooks.</param>
    /// <param name="hookConfiguration">The JSON hook configuration, or <c>null</c>.</param>
    /// <returns>A configured hooks instance, or <c>null</c> if no hooks are configured.</returns>
    public static RestLibHooks<TEntity, TKey>? BuildHooks<TEntity, TKey>(
        IServiceProvider services,
        RestLibJsonHookConfiguration? hookConfiguration)
        where TEntity : class
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(services);

        if (hookConfiguration is null)
            return null;

        var resolver = services.GetService<IRestLibNamedHookResolver<TEntity, TKey>>();
        if (resolver is null)
        {
            throw new InvalidOperationException(
                $"JSON hook configuration requires an {nameof(IRestLibNamedHookResolver<TEntity, TKey>)} registration.");
        }

        var hooks = new RestLibHooks<TEntity, TKey>
        {
            OnRequestReceived = BuildStandardStage(resolver, hookConfiguration.OnRequestReceived),
            OnRequestValidated = BuildStandardStage(resolver, hookConfiguration.OnRequestValidated),
            BeforePersist = BuildStandardStage(resolver, hookConfiguration.BeforePersist),
            AfterPersist = BuildStandardStage(resolver, hookConfiguration.AfterPersist),
            BeforeResponse = BuildStandardStage(resolver, hookConfiguration.BeforeResponse),
            OnError = BuildErrorStage(resolver, hookConfiguration.OnError)
        };

        return hooks.HasAnyHooks ? hooks : null;
    }

    private static void ApplyKeySelector<TEntity, TKey>(
        RestLibEndpointConfiguration<TEntity, TKey> endpointConfiguration,
        RestLibJsonResourceConfiguration jsonConfiguration)
        where TEntity : class
        where TKey : notnull
    {
        var keyPropertyName = jsonConfiguration.KeyProperty;
        if (string.IsNullOrWhiteSpace(keyPropertyName))
            return;

        var property = typeof(TEntity).GetProperty(keyPropertyName, BindingFlags.Public | BindingFlags.Instance)
                       ?? throw new InvalidOperationException(
                           $"Key property '{keyPropertyName}' was not found on entity type '{typeof(TEntity).Name}'.");

        if (property.PropertyType != typeof(TKey))
        {
            throw new InvalidOperationException(
                $"Key property '{keyPropertyName}' on entity type '{typeof(TEntity).Name}' must be of type '{typeof(TKey).Name}'.");
        }

        var entityParameter = Expression.Parameter(typeof(TEntity), "entity");
        var propertyAccess = Expression.Property(entityParameter, property);
        var lambda = Expression.Lambda<Func<TEntity, TKey>>(propertyAccess, entityParameter);
        endpointConfiguration.KeySelector = lambda.Compile();
    }

    private static void ApplyAuthorization<TEntity, TKey>(
        RestLibEndpointConfiguration<TEntity, TKey> endpointConfiguration,
        RestLibJsonResourceConfiguration jsonConfiguration)
        where TEntity : class
        where TKey : notnull
    {
        if (jsonConfiguration.AllowAnonymousAll)
        {
            endpointConfiguration.AllowAnonymous();
        }

        if (jsonConfiguration.AllowAnonymous.Count > 0)
        {
            endpointConfiguration.AllowAnonymous([.. jsonConfiguration.AllowAnonymous]);
        }

        foreach (var policyEntry in jsonConfiguration.Policies)
        {
            var operation = ParseOperation(policyEntry.Key, nameof(jsonConfiguration.Policies));
            endpointConfiguration.RequirePolicy(operation, policyEntry.Value);
        }
    }

    private static void ApplyOperationSelection<TEntity, TKey>(
        RestLibEndpointConfiguration<TEntity, TKey> endpointConfiguration,
        RestLibJsonResourceConfiguration jsonConfiguration)
        where TEntity : class
        where TKey : notnull
    {
        var operations = jsonConfiguration.Operations;
        if (operations is null)
            return;

        if (operations.Include.Count > 0 && operations.Exclude.Count > 0)
        {
            throw new InvalidOperationException(
                $"Resource '{jsonConfiguration.Name}' cannot configure both include and exclude operations.");
        }

        if (operations.Include.Count > 0)
        {
            endpointConfiguration.IncludeOperations([.. operations.Include]);
        }

        if (operations.Exclude.Count > 0)
        {
            endpointConfiguration.ExcludeOperations([.. operations.Exclude]);
        }
    }

    private static void ApplyFiltering<TEntity, TKey>(
        RestLibEndpointConfiguration<TEntity, TKey> endpointConfiguration,
        RestLibJsonResourceConfiguration jsonConfiguration)
        where TEntity : class
        where TKey : notnull
    {
        // Properties with explicit operators (takes precedence)
        var operatorProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in jsonConfiguration.FilteringOperators)
        {
            operatorProperties.Add(entry.Key);
            var operators = entry.Value
                .SelectMany(ParseFilterOperators)
                .Distinct()
                .ToArray();
            endpointConfiguration.AllowFiltering(entry.Key, operators);
        }

        // Simple equality-only properties (skip any already configured via FilteringOperators)
        foreach (var propertyName in jsonConfiguration.Filtering)
        {
            if (!operatorProperties.Contains(propertyName))
            {
                endpointConfiguration.AllowFiltering(propertyName);
            }
        }
    }

    private static IEnumerable<Filtering.FilterOperator> ParseFilterOperators(string operatorName)
    {
        return operatorName.ToLowerInvariant() switch
        {
            "eq" => [Filtering.FilterOperator.Eq],
            "neq" => [Filtering.FilterOperator.Neq],
            "gt" => [Filtering.FilterOperator.Gt],
            "lt" => [Filtering.FilterOperator.Lt],
            "gte" => [Filtering.FilterOperator.Gte],
            "lte" => [Filtering.FilterOperator.Lte],
            "contains" => [Filtering.FilterOperator.Contains],
            "starts_with" => [Filtering.FilterOperator.StartsWith],
            "ends_with" => [Filtering.FilterOperator.EndsWith],
            "in" => [Filtering.FilterOperator.In],
            "equality" => Filtering.FilterOperators.Equality,
            "comparison" => Filtering.FilterOperators.Comparison,
            "string" => Filtering.FilterOperators.String,
            "all" => Filtering.FilterOperators.All,
            _ => throw new InvalidOperationException(
                $"'{operatorName}' is not a valid filter operator or preset. " +
                $"Valid operators: eq, neq, gt, lt, gte, lte, contains, starts_with, ends_with, in. " +
                $"Valid presets: equality, comparison, string, all.")
        };
    }

    private static void ApplySorting<TEntity, TKey>(
        RestLibEndpointConfiguration<TEntity, TKey> endpointConfiguration,
        RestLibJsonResourceConfiguration jsonConfiguration)
        where TEntity : class
        where TKey : notnull
    {
        if (jsonConfiguration.Sorting.Count == 0)
            return;

        endpointConfiguration.AllowSorting([.. jsonConfiguration.Sorting]);

        if (!string.IsNullOrWhiteSpace(jsonConfiguration.DefaultSort))
        {
            endpointConfiguration.DefaultSort(jsonConfiguration.DefaultSort);
        }
    }

    private static void ApplyFieldSelection<TEntity, TKey>(
        RestLibEndpointConfiguration<TEntity, TKey> endpointConfiguration,
        RestLibJsonResourceConfiguration jsonConfiguration)
        where TEntity : class
        where TKey : notnull
    {
        if (jsonConfiguration.FieldSelection.Count == 0)
            return;

        endpointConfiguration.AllowFieldSelection([.. jsonConfiguration.FieldSelection]);
    }

    private static void ApplyBatch<TEntity, TKey>(
        RestLibEndpointConfiguration<TEntity, TKey> endpointConfiguration,
        RestLibJsonResourceConfiguration jsonConfiguration)
        where TEntity : class
        where TKey : notnull
    {
        var batch = jsonConfiguration.Batch;
        if (batch is null)
            return;

        if (batch.Actions.Count == 0)
        {
            endpointConfiguration.EnableBatch();
        }
        else
        {
            endpointConfiguration.EnableBatch([.. batch.Actions]);
        }
    }

    private static void ApplyRateLimiting<TEntity, TKey>(
        RestLibEndpointConfiguration<TEntity, TKey> endpointConfiguration,
        RestLibJsonResourceConfiguration jsonConfiguration)
        where TEntity : class
        where TKey : notnull
    {
        var rateLimiting = jsonConfiguration.RateLimiting;
        if (rateLimiting is null)
            return;

        if (!string.IsNullOrWhiteSpace(rateLimiting.Default))
        {
            endpointConfiguration.UseRateLimiting(rateLimiting.Default);
        }

        foreach (var entry in rateLimiting.ByOperation)
        {
            var operation = ParseOperation(entry.Key, nameof(rateLimiting.ByOperation));
            endpointConfiguration.UseRateLimiting(entry.Value, operation);
        }

        if (rateLimiting.Disabled.Count > 0)
        {
            endpointConfiguration.DisableRateLimiting([.. rateLimiting.Disabled]);
        }
    }

    private static void ApplyOpenApi<TEntity, TKey>(
        RestLibEndpointConfiguration<TEntity, TKey> endpointConfiguration,
        RestLibJsonOpenApiConfiguration? openApiConfiguration)
        where TEntity : class
        where TKey : notnull
    {
        if (openApiConfiguration is null)
            return;

        endpointConfiguration.OpenApi.Tag = openApiConfiguration.Tag;
        endpointConfiguration.OpenApi.TagDescription = openApiConfiguration.TagDescription;
        endpointConfiguration.OpenApi.Deprecated = openApiConfiguration.Deprecated;
        endpointConfiguration.OpenApi.DeprecationMessage = openApiConfiguration.DeprecationMessage;

        ApplySummaries(endpointConfiguration.OpenApi.Summaries, openApiConfiguration.Summaries);
        ApplyDescriptions(endpointConfiguration.OpenApi.Descriptions, openApiConfiguration.Descriptions);
    }

    private static void ApplySummaries(
        RestLibOpenApiSummaries summaries,
        IReadOnlyDictionary<string, string> configuredSummaries)
    {
        foreach (var summaryEntry in configuredSummaries)
        {
            var operation = ParseOperation(summaryEntry.Key, nameof(configuredSummaries));
            switch (operation)
            {
                case RestLibOperation.GetAll:
                    summaries.GetAll = summaryEntry.Value;
                    break;
                case RestLibOperation.GetById:
                    summaries.GetById = summaryEntry.Value;
                    break;
                case RestLibOperation.Create:
                    summaries.Create = summaryEntry.Value;
                    break;
                case RestLibOperation.Update:
                    summaries.Update = summaryEntry.Value;
                    break;
                case RestLibOperation.Patch:
                    summaries.Patch = summaryEntry.Value;
                    break;
                case RestLibOperation.Delete:
                    summaries.Delete = summaryEntry.Value;
                    break;
            }
        }
    }

    private static void ApplyDescriptions(
        RestLibOpenApiDescriptions descriptions,
        IReadOnlyDictionary<string, string> configuredDescriptions)
    {
        foreach (var descriptionEntry in configuredDescriptions)
        {
            var operation = ParseOperation(descriptionEntry.Key, nameof(configuredDescriptions));
            switch (operation)
            {
                case RestLibOperation.GetAll:
                    descriptions.GetAll = descriptionEntry.Value;
                    break;
                case RestLibOperation.GetById:
                    descriptions.GetById = descriptionEntry.Value;
                    break;
                case RestLibOperation.Create:
                    descriptions.Create = descriptionEntry.Value;
                    break;
                case RestLibOperation.Update:
                    descriptions.Update = descriptionEntry.Value;
                    break;
                case RestLibOperation.Patch:
                    descriptions.Patch = descriptionEntry.Value;
                    break;
                case RestLibOperation.Delete:
                    descriptions.Delete = descriptionEntry.Value;
                    break;
            }
        }
    }

    private static RestLibHookDelegate<TEntity, TKey>? BuildStandardStage<TEntity, TKey>(
        IRestLibNamedHookResolver<TEntity, TKey> resolver,
        RestLibJsonHookStage? stage)
        where TEntity : class
        where TKey : notnull
    {
        if (stage is null)
            return null;

        var defaultHooks = ResolveStandardHooks(resolver, stage.Default);
        var byOperation = ResolveOperationHooks(resolver, stage.ByOperation);

        if (defaultHooks.Count == 0 && byOperation.Count == 0)
            return null;

        return async context =>
        {
            foreach (var hook in defaultHooks)
            {
                await hook(context);
                if (!context.ShouldContinue)
                    return;
            }

            if (byOperation.TryGetValue(context.Operation, out var operationHooks))
            {
                foreach (var hook in operationHooks)
                {
                    await hook(context);
                    if (!context.ShouldContinue)
                        return;
                }
            }
        };
    }

    private static RestLibErrorHookDelegate<TEntity, TKey>? BuildErrorStage<TEntity, TKey>(
        IRestLibNamedHookResolver<TEntity, TKey> resolver,
        RestLibJsonErrorHookStage? stage)
        where TEntity : class
        where TKey : notnull
    {
        if (stage is null)
            return null;

        var defaultHooks = ResolveErrorHooks(resolver, stage.Default);
        var byOperation = ResolveOperationErrorHooks(resolver, stage.ByOperation);

        if (defaultHooks.Count == 0 && byOperation.Count == 0)
            return null;

        return async context =>
        {
            foreach (var hook in defaultHooks)
            {
                await hook(context);
                if (context.Handled)
                    return;
            }

            if (byOperation.TryGetValue(context.Operation, out var operationHooks))
            {
                foreach (var hook in operationHooks)
                {
                    await hook(context);
                    if (context.Handled)
                        return;
                }
            }
        };
    }

    private static List<RestLibHookDelegate<TEntity, TKey>> ResolveStandardHooks<TEntity, TKey>(
        IRestLibNamedHookResolver<TEntity, TKey> resolver,
        IEnumerable<string> hookNames)
        where TEntity : class
        where TKey : notnull
    {
        var hooks = new List<RestLibHookDelegate<TEntity, TKey>>();
        foreach (var hookName in hookNames)
        {
            hooks.Add(resolver.Resolve(hookName));
        }

        return hooks;
    }

    private static Dictionary<RestLibOperation, List<RestLibHookDelegate<TEntity, TKey>>> ResolveOperationHooks<TEntity, TKey>(
        IRestLibNamedHookResolver<TEntity, TKey> resolver,
        IReadOnlyDictionary<string, List<string>> stageConfiguration)
        where TEntity : class
        where TKey : notnull
    {
        var hooks = new Dictionary<RestLibOperation, List<RestLibHookDelegate<TEntity, TKey>>>();
        foreach (var entry in stageConfiguration)
        {
            var operation = ParseOperation(entry.Key, nameof(stageConfiguration));
            hooks[operation] = entry.Value.Select(resolver.Resolve).ToList();
        }

        return hooks;
    }

    private static List<RestLibErrorHookDelegate<TEntity, TKey>> ResolveErrorHooks<TEntity, TKey>(
        IRestLibNamedHookResolver<TEntity, TKey> resolver,
        IEnumerable<string> hookNames)
        where TEntity : class
        where TKey : notnull
    {
        var hooks = new List<RestLibErrorHookDelegate<TEntity, TKey>>();
        foreach (var hookName in hookNames)
        {
            hooks.Add(resolver.ResolveError(hookName));
        }

        return hooks;
    }

    private static Dictionary<RestLibOperation, List<RestLibErrorHookDelegate<TEntity, TKey>>> ResolveOperationErrorHooks<TEntity, TKey>(
        IRestLibNamedHookResolver<TEntity, TKey> resolver,
        IReadOnlyDictionary<string, List<string>> stageConfiguration)
        where TEntity : class
        where TKey : notnull
    {
        var hooks = new Dictionary<RestLibOperation, List<RestLibErrorHookDelegate<TEntity, TKey>>>();
        foreach (var entry in stageConfiguration)
        {
            var operation = ParseOperation(entry.Key, nameof(stageConfiguration));
            hooks[operation] = entry.Value.Select(resolver.ResolveError).ToList();
        }

        return hooks;
    }

    private static RestLibOperation ParseOperation(string operationName, string parameterName)
    {
        if (Enum.TryParse<RestLibOperation>(operationName, true, out var operation))
        {
            return operation;
        }

        throw new InvalidOperationException(
            $"'{operationName}' is not a valid RestLib operation name for '{parameterName}'.");
    }
}
