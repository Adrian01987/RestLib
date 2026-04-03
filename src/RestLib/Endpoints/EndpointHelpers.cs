using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RestLib.Abstractions;
using RestLib.Caching;
using RestLib.Configuration;
using RestLib.Hooks;
using RestLib.Pagination;
using RestLib.Responses;
using RestLib.Serialization;

namespace RestLib.Endpoints;

/// <summary>
/// Result of initializing a hook pipeline for an endpoint request.
/// </summary>
/// <typeparam name="TEntity">The entity type being processed.</typeparam>
/// <typeparam name="TKey">The key type of the entity.</typeparam>
/// <param name="Pipeline">The hook pipeline, or <c>null</c> if no hooks are configured.</param>
/// <param name="Context">The hook context, or <c>null</c> if no hooks are configured.</param>
/// <param name="EarlyResult">An early result if <c>OnRequestReceived</c> short-circuited; otherwise <c>null</c>.</param>
internal readonly record struct PipelineInitResult<TEntity, TKey>(
    HookPipeline<TEntity, TKey>? Pipeline,
    HookContext<TEntity, TKey>? Context,
    IResult? EarlyResult)
    where TEntity : class;

/// <summary>
/// Shared helper methods used by the individual endpoint handlers.
/// </summary>
internal static class EndpointHelpers
{
    /// <summary>
    /// Creates a hook pipeline (if hooks are configured), builds a <see cref="HookContext{TEntity, TKey}"/>,
    /// and executes the <c>OnRequestReceived</c> stage. This consolidates the pipeline initialisation
    /// logic that is common across all endpoint handlers.
    /// </summary>
    /// <typeparam name="TEntity">The entity type being processed.</typeparam>
    /// <typeparam name="TKey">The key type of the entity.</typeparam>
    /// <param name="hooks">The hooks from the endpoint configuration, or <c>null</c>.</param>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="operation">The REST operation being performed.</param>
    /// <param name="resourceId">The optional resource identifier.</param>
    /// <param name="entity">The optional entity being processed.</param>
    /// <returns>
    /// A <see cref="PipelineInitResult{TEntity, TKey}"/> containing the pipeline, context, and
    /// an early result if the <c>OnRequestReceived</c> hook short-circuited.
    /// When no hooks are configured, both <c>Pipeline</c> and <c>Context</c> are <c>null</c>
    /// and <c>EarlyResult</c> is <c>null</c>.
    /// </returns>
    internal static async Task<PipelineInitResult<TEntity, TKey>> InitializePipelineAsync<TEntity, TKey>(
        RestLibHooks<TEntity, TKey>? hooks,
        HttpContext httpContext,
        RestLibOperation operation,
        TKey? resourceId = default,
        TEntity? entity = default)
        where TEntity : class
    {
        if (hooks is null)
        {
            return new PipelineInitResult<TEntity, TKey>(null, null, null);
        }

        var pipeline = new HookPipeline<TEntity, TKey>(hooks);
        var hookContext = pipeline.CreateContext(httpContext, operation, resourceId, entity);
        var earlyResult = await ExecuteHookAsync(pipeline.ExecuteOnRequestReceivedAsync, hookContext);

        return new PipelineInitResult<TEntity, TKey>(pipeline, hookContext, earlyResult);
    }

    /// <summary>
    /// Executes a hook stage and returns an early result if the pipeline was short-circuited.
    /// Returns <c>null</c> if the pipeline should continue normally.
    /// </summary>
    /// <typeparam name="TEntity">The entity type being processed.</typeparam>
    /// <typeparam name="TKey">The key type of the entity.</typeparam>
    /// <param name="hookExecutor">The hook executor delegate to run.</param>
    /// <param name="hookContext">The hook context for the current request.</param>
    /// <returns>An early result if the hook short-circuited; otherwise <c>null</c>.</returns>
    internal static async Task<IResult?> ExecuteHookAsync<TEntity, TKey>(
        Func<HookContext<TEntity, TKey>, Task<bool>> hookExecutor,
        HookContext<TEntity, TKey> hookContext)
        where TEntity : class
    {
        if (!await hookExecutor(hookContext))
        {
            return hookContext.EarlyResult ?? Results.StatusCode(StatusCodes.Status500InternalServerError);
        }

        return null;
    }

    /// <summary>
    /// Runs a hook stage if the pipeline and context are available.
    /// Returns <c>null</c> if the hook was skipped or the pipeline should continue;
    /// returns an <see cref="IResult"/> if the hook short-circuited processing.
    /// </summary>
    /// <typeparam name="TEntity">The entity type being processed.</typeparam>
    /// <typeparam name="TKey">The key type of the entity.</typeparam>
    /// <param name="pipeline">The hook pipeline, or null if no hooks are configured.</param>
    /// <param name="hookContext">The hook context, or null if the pipeline was not initialised.</param>
    /// <param name="stageSelector">A function that selects the hook stage to execute.</param>
    /// <returns>An early result if the hook short-circuited; otherwise <c>null</c>.</returns>
    internal static async Task<IResult?> RunHookStageAsync<TEntity, TKey>(
        HookPipeline<TEntity, TKey>? pipeline,
        HookContext<TEntity, TKey>? hookContext,
        Func<HookPipeline<TEntity, TKey>, Func<HookContext<TEntity, TKey>, Task<bool>>> stageSelector)
        where TEntity : class
    {
        if (pipeline is null || hookContext is null)
        {
            return null;
        }

        return await ExecuteHookAsync(stageSelector(pipeline), hookContext);
    }

    /// <summary>
    /// Executes the error hook pipeline and returns an error result if handled.
    /// Returns <c>null</c> if the error was not handled (caller should rethrow).
    /// </summary>
    /// <typeparam name="TEntity">The entity type being processed.</typeparam>
    /// <typeparam name="TKey">The key type of the entity.</typeparam>
    /// <param name="pipeline">The hook pipeline, or null if no hooks are configured.</param>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="operation">The operation that was being performed.</param>
    /// <param name="exception">The exception that occurred.</param>
    /// <param name="resourceId">The optional resource identifier.</param>
    /// <param name="entity">The optional entity being processed.</param>
    /// <returns>An error result if the hook handled the error; otherwise <c>null</c>.</returns>
    internal static async Task<IResult?> HandleErrorHookAsync<TEntity, TKey>(
        HookPipeline<TEntity, TKey>? pipeline,
        HttpContext httpContext,
        RestLibOperation operation,
        Exception exception,
        TKey? resourceId = default,
        TEntity? entity = default)
        where TEntity : class
    {
        if (pipeline is null) return null;

        var errorContext = pipeline.CreateErrorContext(httpContext, operation, exception, resourceId, entity);
        var (handled, errorResult) = await pipeline.ExecuteOnErrorAsync(errorContext);

        return handled && errorResult is not null ? errorResult : null;
    }

    /// <summary>
    /// Resolves the JSON serializer options and RestLib options from the request services.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <returns>A tuple containing the JSON serializer options and RestLib options.</returns>
    internal static (JsonSerializerOptions JsonOptions, RestLibOptions Options) ResolveOptions(
        HttpContext httpContext)
    {
        var jsonOptions = httpContext.RequestServices.GetService<JsonSerializerOptions>()
                          ?? RestLibJsonOptions.CreateDefault();
        var options = httpContext.RequestServices.GetService<RestLibOptions>()
                      ?? new RestLibOptions();
        return (jsonOptions, options);
    }

    /// <summary>
    /// Resolves the ETag generator from the service provider, falling back to a hash-based implementation.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="jsonOptions">The JSON serializer options for the hash-based fallback.</param>
    /// <returns>The resolved ETag generator.</returns>
    internal static IETagGenerator ResolveETagGenerator(
        HttpContext httpContext,
        JsonSerializerOptions jsonOptions)
    {
        return httpContext.RequestServices.GetService<IETagGenerator>()
               ?? new HashBasedETagGenerator(jsonOptions);
    }

    /// <summary>
    /// Gets the JSON serializer options from the service provider,
    /// falling back to RestLib defaults if not registered.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <returns>The resolved JSON serializer options.</returns>
    internal static JsonSerializerOptions GetJsonOptions(HttpContext httpContext)
    {
        return httpContext.RequestServices.GetService<JsonSerializerOptions>()
               ?? RestLibJsonOptions.CreateDefault();
    }

    /// <summary>
    /// Builds a standardized collection response with pagination links.
    /// Links are fully-qualified URLs and preserve any query filters.
    /// </summary>
    /// <typeparam name="T">The type of items in the collection.</typeparam>
    /// <param name="result">The paged result from the repository.</param>
    /// <param name="request">The current HTTP request (used for building URLs).</param>
    /// <param name="currentCursor">The current cursor value, if any.</param>
    /// <param name="limit">The effective page size.</param>
    /// <param name="options">The RestLib options.</param>
    /// <returns>A collection response with pagination links.</returns>
    internal static CollectionResponse<T> BuildCollectionResponse<T>(
        PagedResult<T> result,
        HttpRequest request,
        string? currentCursor,
        int limit,
        RestLibOptions options)
    {
        string? selfLink = null;
        string? firstLink = null;
        string? nextLink = null;
        string? prevLink = null;

        if (options.IncludePaginationLinks)
        {
            var baseUrl = $"{request.Scheme}://{request.Host}{request.Path}";

            // Extract query filters (all query params except cursor and limit)
            var filterParams = GetFilterQueryParams(request.Query);

            // Build self link (includes cursor if present, always includes limit and filters)
            selfLink = BuildPaginationUrl(baseUrl, currentCursor, limit, filterParams);

            // Build first link (no cursor, includes limit and filters)
            firstLink = BuildPaginationUrl(baseUrl, null, limit, filterParams);

            // Build next link if there are more items
            if (result.NextCursor is not null)
            {
                nextLink = BuildPaginationUrl(baseUrl, result.NextCursor, limit, filterParams);
            }

            // Note: prev link requires tracking the previous cursor, which we don't have in simple cursor pagination
            // For now, prev is null (could be enhanced with bidirectional cursors later)
        }

        return new CollectionResponse<T>
        {
            Items = result.Items,
            Self = selfLink,
            First = firstLink,
            Next = nextLink,
            Prev = prevLink
        };
    }

    /// <summary>
    /// Extracts filter query parameters (all params except cursor and limit).
    /// </summary>
    /// <param name="query">The query string collection.</param>
    /// <returns>A list of key-value pairs representing the filter query parameters.</returns>
    internal static IReadOnlyList<KeyValuePair<string, string>> GetFilterQueryParams(IQueryCollection query)
    {
        var filters = new List<KeyValuePair<string, string>>();
        foreach (var param in query)
        {
            // Skip pagination params - preserve all other filters
            if (string.Equals(param.Key, "cursor", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(param.Key, "limit", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var value in param.Value)
            {
                if (value is not null)
                {
                    filters.Add(new KeyValuePair<string, string>(param.Key, value));
                }
            }
        }

        return filters;
    }

    /// <summary>
    /// Builds a pagination URL with cursor, limit, and filter parameters.
    /// </summary>
    /// <param name="baseUrl">The base URL (scheme, host, path).</param>
    /// <param name="cursor">The cursor value, if any.</param>
    /// <param name="limit">The page size.</param>
    /// <param name="filterParams">Additional query filter parameters to preserve.</param>
    /// <returns>The fully-qualified pagination URL.</returns>
    internal static string BuildPaginationUrl(
        string baseUrl,
        string? cursor,
        int limit,
        IReadOnlyList<KeyValuePair<string, string>> filterParams)
    {
        var queryParams = new List<string>();

        // Add cursor if present
        if (!string.IsNullOrEmpty(cursor))
        {
            queryParams.Add($"cursor={Uri.EscapeDataString(cursor)}");
        }

        // Always add limit
        queryParams.Add($"limit={limit}");

        // Add all filter params (preserved from original request)
        foreach (var filter in filterParams)
        {
            queryParams.Add($"{Uri.EscapeDataString(filter.Key)}={Uri.EscapeDataString(filter.Value)}");
        }

        return queryParams.Count > 0 ? $"{baseUrl}?{string.Join("&", queryParams)}" : baseUrl;
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
    {
        if (keySelector is not null)
        {
            return keySelector(entity);
        }

        // Fall back to reflection: look for 'Id' property
        var idProperty = typeof(TEntity).GetProperty("Id");
        if (idProperty is not null && idProperty.PropertyType == typeof(TKey))
        {
            return (TKey?)idProperty.GetValue(entity);
        }

        return default;
    }
}
