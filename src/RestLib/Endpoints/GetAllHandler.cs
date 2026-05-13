using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RestLib.Abstractions;
using RestLib.Configuration;
using RestLib.FieldSelection;
using RestLib.Filtering;
using RestLib.Hooks;
using RestLib.Hypermedia;
using RestLib.Logging;
using RestLib.Mapping;
using RestLib.Pagination;
using RestLib.Search;
using RestLib.Sorting;

namespace RestLib.Endpoints;

/// <summary>
/// Handles GET requests for paginated collections.
/// </summary>
internal static class GetAllHandler
{
    /// <summary>
    /// Creates the delegate for the GetAll endpoint.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="config">The endpoint configuration.</param>
    /// <returns>The request delegate.</returns>
    internal static Func<IRepository<TEntity, TKey>, HttpContext, string?, int?, CancellationToken, Task<IResult>>
        CreateDelegate<TEntity, TKey>(RestLibEndpointConfiguration<TEntity, TKey> config)
        where TEntity : class
        where TKey : notnull
    {
        return async (
            repository,
            httpContext,
            cursor,
            limit,
            ct) =>
        {
            var (jsonOptions, options) = OptionsResolver.ResolveOptions(httpContext);
            var logger = RestLibLoggerResolver.ResolveLogger(httpContext, "RestLib.GetAll");

            RestLibLogMessages.GetAllRequestReceived(logger, cursor?.Length ?? 0, limit);

            // Initialize hook pipeline and run OnRequestReceived
            var (pipeline, hookContext, pipelineEarlyResult) = await HookHelper.InitializePipelineAsync<TEntity, TKey>(
                config.Hooks, httpContext, RestLibOperation.GetAll, logger: logger);
            if (pipelineEarlyResult is not null) return pipelineEarlyResult;

            try
            {
                // Validate cursor if provided
                if (!string.IsNullOrEmpty(cursor))
                {
                    if (cursor.Length > options.MaxCursorLength)
                    {
                        return Responses.ProblemDetailsResult.InvalidCursor(
                            cursor,
                            httpContext.Request.Path,
                            jsonOptions,
                            $"The cursor exceeds the maximum allowed length of {options.MaxCursorLength} characters.",
                            logger,
                            options);
                    }

                    if (!CursorEncoder.IsValid(cursor))
                    {
                        return Responses.ProblemDetailsResult.InvalidCursor(
                            cursor,
                            httpContext.Request.Path,
                            jsonOptions,
                            logger: logger,
                            options: options);
                    }
                }

                // Validate limit if provided
                if (limit.HasValue && (limit.Value < 1 || limit.Value > options.MaxPageSize))
                {
                    return Responses.ProblemDetailsResult.InvalidLimit(
                        limit.Value,
                        1,
                        options.MaxPageSize,
                        httpContext.Request.Path,
                        jsonOptions,
                        logger,
                        options);
                }

                // Parse and validate filters
                IReadOnlyList<FilterValue> filterValues = [];
                if (config.HasFilters)
                {
                    var filterResult = FilterParser.Parse(httpContext.Request.Query, config.FilterConfiguration, options.MaxFilterInListSize);
                    if (!filterResult.IsValid)
                    {
                        return Responses.ProblemDetailsResult.InvalidFilters(
                            filterResult.Errors,
                            httpContext.Request.Path,
                            jsonOptions,
                            logger,
                            options);
                    }

                    filterValues = filterResult.Filters;
                }

                // Parse and validate sort
                IReadOnlyList<SortField> sortFields = [];
                if (config.HasSorting)
                {
                    var rawSort = httpContext.Request.Query["sort"].FirstOrDefault();
                    if (!string.IsNullOrEmpty(rawSort))
                    {
                        var sortResult = SortParser.Parse(rawSort, config.SortConfiguration);
                        if (!sortResult.IsValid)
                        {
                            return Responses.ProblemDetailsResult.InvalidSort(
                                sortResult.Errors,
                                httpContext.Request.Path,
                                jsonOptions,
                                logger,
                                options);
                        }

                        sortFields = sortResult.Fields;
                    }
                    else if (config.SortConfiguration.DefaultSortFields is { Count: > 0 } defaults)
                    {
                        sortFields = defaults;
                    }
                }

                // Parse and validate field selection
                IReadOnlyList<SelectedField> selectedFields = [];
                if (config.HasFieldSelection)
                {
                    var rawFields = httpContext.Request.Query["fields"].FirstOrDefault();
                    if (!string.IsNullOrEmpty(rawFields))
                    {
                        var fieldsResult = FieldSelectionParser.Parse(rawFields, config.FieldSelectionConfiguration);
                        if (!fieldsResult.IsValid)
                        {
                            return Responses.ProblemDetailsResult.InvalidFields(
                                fieldsResult.Errors,
                                httpContext.Request.Path,
                                jsonOptions,
                                logger,
                                options);
                        }

                        selectedFields = fieldsResult.Fields;
                    }
                }

                SearchRequest? search = null;
                if (config.HasSearch)
                {
                    var searchResult = SearchParser.Parse(httpContext.Request.Query, config.SearchConfiguration);
                    if (!searchResult.IsValid)
                    {
                        return Responses.ProblemDetailsResult.InvalidSearch(
                            searchResult.Errors,
                            httpContext.Request.Path,
                            jsonOptions,
                            logger,
                            options);
                    }

                    search = searchResult.Search;
                }

                // OnRequestValidated hook
                var onValidatedResult = await HookHelper.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteOnRequestValidatedAsync);
                if (onValidatedResult is not null) return onValidatedResult;

                var effectiveLimit = Math.Clamp(limit ?? options.DefaultPageSize, 1, options.MaxPageSize);
                var paginationRequest = new PaginationRequest
                {
                    Cursor = cursor,
                    Limit = effectiveLimit,
                    Filters = filterValues,
                    SortFields = sortFields,
                    Search = search
                };

                PagedResult<TEntity> result;
                try
                {
                    if (selectedFields.Count > 0 &&
                        ShouldUseProjectionPushdown(options, config) &&
                        repository is IFieldSelectionProjectionRepository<TEntity, TKey> projectionRepository)
                    {
                        result = await projectionRepository.GetAllProjectedAsync(paginationRequest, selectedFields, ct)
                            ?? await repository.GetAllAsync(paginationRequest, ct);
                    }
                    else
                    {
                        result = await repository.GetAllAsync(paginationRequest, ct);
                    }
                }
                catch (Exception ex) when (IsEfCoreInvalidCursorException(ex))
                {
                    return Responses.ProblemDetailsResult.InvalidCursor(
                        cursor ?? string.Empty,
                        httpContext.Request.Path,
                        jsonOptions,
                        ex.Message,
                        logger,
                        options);
                }

                // If the repository supports counting, get the total count
                long? totalCount = null;
                if (repository is IQueryCountableRepository<TEntity, TKey> queryCountable)
                {
                    totalCount = await queryCountable.CountAsync(paginationRequest, ct);
                }
                else if (search is null && repository is ICountableRepository<TEntity, TKey> countable)
                {
                    totalCount = await countable.CountAsync(filterValues, ct);
                }

                var response = PaginationHelper.BuildCollectionResponse(result, httpContext.Request, cursor, effectiveLimit, options, totalCount);

                RestLibLogMessages.GetAllResponse(logger, response.Items.Count, response.Next is not null);

                // BeforeResponse hook
                var beforeResponseResult = await HookHelper.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteBeforeResponseAsync);
                if (beforeResponseResult is not null) return beforeResponseResult;

                // Apply field selection projection if requested
                if (selectedFields.Count > 0)
                {
                    var projectedItems = FieldProjector.ProjectMany(
                        response.Items,
                        selectedFields,
                        jsonOptions,
                        config.FieldSelectionConfiguration.ResponseShape);

                    // Inject per-item HATEOAS links into projected dictionaries
                    if (options.EnableHateoas && projectedItems is not null)
                    {
                        var collectionPath = httpContext.Request.Path.ToString();
                        var customLinksProvider = httpContext.RequestServices.GetService<IHateoasLinkProvider<TEntity, TKey>>();
                        HateoasHelper.InjectLinksIntoProjectedCollection(
                            projectedItems, response.Items, config, httpContext.Request, collectionPath, jsonOptions, customLinksProvider);
                    }

                    var projectedResponse = new
                    {
                        items = projectedItems,
                        total_count = response.TotalCount,
                        self = response.Self,
                        first = response.First,
                        next = response.Next,
                        prev = response.Prev
                    };
                    return Results.Json(projectedResponse, jsonOptions);
                }

                // Inject per-item HATEOAS links into full entities
                if (options.EnableHateoas)
                {
                    var collectionPath = httpContext.Request.Path.ToString();
                    var customLinksProvider = httpContext.RequestServices.GetService<IHateoasLinkProvider<TEntity, TKey>>();
                    var wrappedItems = HateoasHelper.WrapCollectionWithLinks(
                        response.Items, config, httpContext.Request, collectionPath, jsonOptions, customLinksProvider);
                    var hateoasResponse = new
                    {
                        items = wrappedItems,
                        total_count = response.TotalCount,
                        self = response.Self,
                        first = response.First,
                        next = response.Next,
                        prev = response.Prev
                    };
                    return Results.Json(hateoasResponse, jsonOptions);
                }

                return Results.Json(response, jsonOptions);
            }
            catch (Exception ex)
            {
                RestLibLogMessages.EndpointUnhandledException(logger, nameof(RestLibOperation.GetAll), ex);
                var errorResult = await HookHelper.HandleErrorHookAsync(pipeline, httpContext, RestLibOperation.GetAll, ex, logger: logger);
                if (errorResult is not null) return errorResult;
                throw;
            }
        };
    }

    /// <summary>
    /// Creates the delegate for the mapped GetAll endpoint.
    /// </summary>
    /// <typeparam name="TApiModel">The API model type.</typeparam>
    /// <typeparam name="TDbModel">The DB model type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="config">The endpoint configuration.</param>
    /// <returns>The request delegate.</returns>
    internal static Func<HttpContext, string?, int?, CancellationToken, Task<IResult>>
        CreateMappedDelegate<TApiModel, TDbModel, TKey>(
            RestLibEndpointConfiguration<TApiModel, TDbModel, TKey> config)
        where TApiModel : class
        where TDbModel : class
        where TKey : notnull
    {
        return async (
            httpContext,
            cursor,
            limit,
            ct) =>
        {
            var (jsonOptions, options) = OptionsResolver.ResolveOptions(httpContext);
            var logger = RestLibLoggerResolver.ResolveLogger(httpContext, "RestLib.GetAll");
            var repository = httpContext.RequestServices.GetRequiredService<IRepository<TDbModel, TKey>>();
            var mapper = RestLibMapperResolver.Resolve<TApiModel, TDbModel>(
                httpContext.RequestServices,
                config.MapperName,
                config.UseAutoMapper,
                config.ResourceName);

            RestLibLogMessages.GetAllRequestReceived(logger, cursor?.Length ?? 0, limit);

            if (config.UsesDbModelHooks)
            {
                var (pipeline, hookContext, pipelineEarlyResult) = await HookHelper.InitializePipelineAsync<TDbModel, TKey>(
                    config.DbModelHooks,
                    httpContext,
                    RestLibOperation.GetAll,
                    logger: logger);
                if (pipelineEarlyResult is not null) return pipelineEarlyResult;

                try
                {
                    return await ExecuteMappedGetAllAsync<TApiModel, TDbModel, TDbModel, TKey>(
                        repository,
                        mapper,
                        httpContext,
                        cursor,
                        limit,
                        ct,
                        jsonOptions,
                        options,
                        logger,
                        config,
                        pipeline,
                        hookContext);
                }
                catch (Exception ex)
                {
                    RestLibLogMessages.EndpointUnhandledException(logger, nameof(RestLibOperation.GetAll), ex);
                    var errorResult = await HookHelper.HandleErrorHookAsync(
                        pipeline,
                        httpContext,
                        RestLibOperation.GetAll,
                        ex,
                        logger: logger);
                    if (errorResult is not null) return errorResult;
                    throw;
                }
            }

            var (apiPipeline, apiHookContext, apiPipelineEarlyResult) = await HookHelper.InitializePipelineAsync<TApiModel, TKey>(
                config.Hooks,
                httpContext,
                RestLibOperation.GetAll,
                logger: logger);
            if (apiPipelineEarlyResult is not null) return apiPipelineEarlyResult;

            try
            {
                return await ExecuteMappedGetAllAsync<TApiModel, TDbModel, TApiModel, TKey>(
                    repository,
                    mapper,
                    httpContext,
                    cursor,
                    limit,
                    ct,
                    jsonOptions,
                    options,
                    logger,
                    config,
                    apiPipeline,
                    apiHookContext);
            }
            catch (Exception ex)
            {
                RestLibLogMessages.EndpointUnhandledException(logger, nameof(RestLibOperation.GetAll), ex);
                var errorResult = await HookHelper.HandleErrorHookAsync(
                    apiPipeline,
                    httpContext,
                    RestLibOperation.GetAll,
                    ex,
                    logger: logger);
                if (errorResult is not null) return errorResult;
                throw;
            }
        };
    }

    private static async Task<IResult> ExecuteMappedGetAllAsync<TApiModel, TDbModel, THookModel, TKey>(
        IRepository<TDbModel, TKey> repository,
        IRestLibMapper<TApiModel, TDbModel> mapper,
        HttpContext httpContext,
        string? cursor,
        int? limit,
        CancellationToken ct,
        JsonSerializerOptions jsonOptions,
        RestLibOptions options,
        Microsoft.Extensions.Logging.ILogger logger,
        RestLibEndpointConfiguration<TApiModel, TDbModel, TKey> config,
        HookPipeline<THookModel, TKey>? pipeline,
        HookContext<THookModel, TKey>? hookContext)
        where TApiModel : class
        where TDbModel : class
        where THookModel : class
        where TKey : notnull
    {
        if (!string.IsNullOrEmpty(cursor))
        {
            if (cursor.Length > options.MaxCursorLength)
            {
                return Responses.ProblemDetailsResult.InvalidCursor(
                    cursor,
                    httpContext.Request.Path,
                    jsonOptions,
                    $"The cursor exceeds the maximum allowed length of {options.MaxCursorLength} characters.",
                    logger,
                    options);
            }

            if (!CursorEncoder.IsValid(cursor))
            {
                return Responses.ProblemDetailsResult.InvalidCursor(
                    cursor,
                    httpContext.Request.Path,
                    jsonOptions,
                    logger: logger,
                    options: options);
            }
        }

        if (limit.HasValue && (limit.Value < 1 || limit.Value > options.MaxPageSize))
        {
            return Responses.ProblemDetailsResult.InvalidLimit(
                limit.Value,
                1,
                options.MaxPageSize,
                httpContext.Request.Path,
                jsonOptions,
                logger,
                options);
        }

        IReadOnlyList<FilterValue> filterValues = [];
        if (config.HasFilters)
        {
            var filterResult = FilterParser.Parse(httpContext.Request.Query, config.FilterConfiguration, options.MaxFilterInListSize);
            if (!filterResult.IsValid)
            {
                return Responses.ProblemDetailsResult.InvalidFilters(
                    filterResult.Errors,
                    httpContext.Request.Path,
                    jsonOptions,
                    logger,
                    options);
            }

            filterValues = filterResult.Filters;
        }

        IReadOnlyList<SortField> sortFields = [];
        if (config.HasSorting)
        {
            var rawSort = httpContext.Request.Query["sort"].FirstOrDefault();
            if (!string.IsNullOrEmpty(rawSort))
            {
                var sortResult = SortParser.Parse(rawSort, config.SortConfiguration);
                if (!sortResult.IsValid)
                {
                    return Responses.ProblemDetailsResult.InvalidSort(
                        sortResult.Errors,
                        httpContext.Request.Path,
                        jsonOptions,
                        logger,
                        options);
                }

                sortFields = sortResult.Fields;
            }
            else if (config.SortConfiguration.DefaultSortFields is { Count: > 0 } defaults)
            {
                sortFields = defaults;
            }
        }

        IReadOnlyList<SelectedField> selectedFields = [];
        if (config.HasFieldSelection)
        {
            var rawFields = httpContext.Request.Query["fields"].FirstOrDefault();
            if (!string.IsNullOrEmpty(rawFields))
            {
                var fieldsResult = FieldSelectionParser.Parse(rawFields, config.FieldSelectionConfiguration);
                if (!fieldsResult.IsValid)
                {
                    return Responses.ProblemDetailsResult.InvalidFields(
                        fieldsResult.Errors,
                        httpContext.Request.Path,
                        jsonOptions,
                        logger,
                        options);
                }

                selectedFields = fieldsResult.Fields;
            }
        }

        SearchRequest? search = null;
        if (config.HasSearch)
        {
            var searchResult = SearchParser.Parse(httpContext.Request.Query, config.SearchConfiguration);
            if (!searchResult.IsValid)
            {
                return Responses.ProblemDetailsResult.InvalidSearch(
                    searchResult.Errors,
                    httpContext.Request.Path,
                    jsonOptions,
                    logger,
                    options);
            }

            search = searchResult.Search;
        }

        var onValidatedResult = await HookHelper.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteOnRequestValidatedAsync);
        if (onValidatedResult is not null) return onValidatedResult;

        var effectiveLimit = Math.Clamp(limit ?? options.DefaultPageSize, 1, options.MaxPageSize);
        var paginationRequest = new PaginationRequest
        {
            Cursor = cursor,
            Limit = effectiveLimit,
            Filters = filterValues,
            SortFields = sortFields,
            Search = search
        };

        PagedResult<TDbModel> dbResult;
        try
        {
            dbResult = await repository.GetAllAsync(paginationRequest, ct);
        }
        catch (Exception ex) when (IsEfCoreInvalidCursorException(ex))
        {
            return Responses.ProblemDetailsResult.InvalidCursor(
                cursor ?? string.Empty,
                httpContext.Request.Path,
                jsonOptions,
                ex.Message,
                logger,
                options);
        }

        long? totalCount = null;
        if (repository is IQueryCountableRepository<TDbModel, TKey> queryCountable)
        {
            totalCount = await queryCountable.CountAsync(paginationRequest, ct);
        }
        else if (search is null && repository is ICountableRepository<TDbModel, TKey> countable)
        {
            totalCount = await countable.CountAsync(filterValues, ct);
        }

        var apiItems = dbResult.Items.Select(mapper.ToApi).ToList();
        var response = PaginationHelper.BuildCollectionResponse(
            new PagedResult<TApiModel>
            {
                Items = apiItems,
                NextCursor = dbResult.NextCursor
            },
            httpContext.Request,
            cursor,
            effectiveLimit,
            options,
            totalCount);

        RestLibLogMessages.GetAllResponse(logger, response.Items.Count, response.Next is not null);

        var beforeResponseResult = await HookHelper.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteBeforeResponseAsync);
        if (beforeResponseResult is not null) return beforeResponseResult;

        if (selectedFields.Count > 0)
        {
            var projectedItems = FieldProjector.ProjectMany(
                response.Items,
                selectedFields,
                jsonOptions,
                config.FieldSelectionConfiguration.ResponseShape);

            if (options.EnableHateoas && projectedItems is not null)
            {
                var collectionPath = httpContext.Request.Path.ToString();
                var customLinksProvider = httpContext.RequestServices.GetService<IHateoasLinkProvider<TApiModel, TKey>>();
                HateoasHelper.InjectLinksIntoProjectedCollection(
                    projectedItems,
                    response.Items,
                    config,
                    httpContext.Request,
                    collectionPath,
                    jsonOptions,
                    customLinksProvider);
            }

            var projectedResponse = new
            {
                items = projectedItems,
                total_count = response.TotalCount,
                self = response.Self,
                first = response.First,
                next = response.Next,
                prev = response.Prev
            };
            return Results.Json(projectedResponse, jsonOptions);
        }

        if (options.EnableHateoas)
        {
            var collectionPath = httpContext.Request.Path.ToString();
            var customLinksProvider = httpContext.RequestServices.GetService<IHateoasLinkProvider<TApiModel, TKey>>();
            var wrappedItems = HateoasHelper.WrapCollectionWithLinks(
                response.Items,
                config,
                httpContext.Request,
                collectionPath,
                jsonOptions,
                customLinksProvider);
            var hateoasResponse = new
            {
                items = wrappedItems,
                total_count = response.TotalCount,
                self = response.Self,
                first = response.First,
                next = response.Next,
                prev = response.Prev
            };
            return Results.Json(hateoasResponse, jsonOptions);
        }

        return Results.Json(response, jsonOptions);
    }

    private static bool IsEfCoreInvalidCursorException(Exception exception)
    {
        return exception.GetType().FullName == "RestLib.EntityFrameworkCore.EfCoreInvalidCursorException";
    }

    private static bool ShouldUseProjectionPushdown<TEntity, TKey>(
        RestLibOptions options,
        RestLibEndpointConfiguration<TEntity, TKey> config)
        where TEntity : class
        where TKey : notnull
    {
        // The EF Core projection-capability path also handles nested field
        // selections by loading required navigations before falling back.
        return !options.EnableHateoas &&
            !options.EnableETagSupport &&
            config.Hooks is null;
    }
}
