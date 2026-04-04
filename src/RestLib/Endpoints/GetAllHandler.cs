using System.Text.Json;
using Microsoft.AspNetCore.Http;
using RestLib.Abstractions;
using RestLib.Configuration;
using RestLib.FieldSelection;
using RestLib.Filtering;
using RestLib.Hooks;
using RestLib.Pagination;
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
    {
        return async (
            repository,
            httpContext,
            cursor,
            limit,
            ct) =>
        {
            var (jsonOptions, options) = EndpointHelpers.ResolveOptions(httpContext);

            // Initialize hook pipeline and run OnRequestReceived
            var (pipeline, hookContext, pipelineEarlyResult) = await EndpointHelpers.InitializePipelineAsync<TEntity, TKey>(
                config.Hooks, httpContext, RestLibOperation.GetAll);
            if (pipelineEarlyResult is not null) return pipelineEarlyResult;

            try
            {
                // Validate cursor if provided
                if (!string.IsNullOrEmpty(cursor) && !CursorEncoder.IsValid(cursor))
                {
                    return Responses.ProblemDetailsResult.InvalidCursor(
                        cursor,
                        httpContext.Request.Path,
                        jsonOptions);
                }

                // Validate limit if provided
                if (limit.HasValue && (limit.Value < 1 || limit.Value > options.MaxPageSize))
                {
                    return Responses.ProblemDetailsResult.InvalidLimit(
                        limit.Value,
                        1,
                        options.MaxPageSize,
                        httpContext.Request.Path,
                        jsonOptions);
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
                            jsonOptions);
                    }

                    filterValues = filterResult.Values;
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
                                jsonOptions);
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
                                jsonOptions);
                        }

                        selectedFields = fieldsResult.Fields;
                    }
                }

                // OnRequestValidated hook
                var onValidatedResult = await EndpointHelpers.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteOnRequestValidatedAsync);
                if (onValidatedResult is not null) return onValidatedResult;

                var effectiveLimit = Math.Clamp(limit ?? options.DefaultPageSize, 1, options.MaxPageSize);
                var paginationRequest = new PaginationRequest
                {
                    Cursor = cursor,
                    Limit = effectiveLimit,
                    Filters = filterValues,
                    SortFields = sortFields
                };

                var result = await repository.GetAllAsync(paginationRequest, ct);

                var response = EndpointHelpers.BuildCollectionResponse(result, httpContext.Request, cursor, effectiveLimit, options);

                // BeforeResponse hook
                var beforeResponseResult = await EndpointHelpers.RunHookStageAsync(pipeline, hookContext, p => p.ExecuteBeforeResponseAsync);
                if (beforeResponseResult is not null) return beforeResponseResult;

                // Apply field selection projection if requested
                if (selectedFields.Count > 0)
                {
                    var projectedItems = FieldProjector.ProjectMany(response.Items, selectedFields, jsonOptions);
                    var projectedResponse = new
                    {
                        items = projectedItems,
                        self = response.Self,
                        first = response.First,
                        next = response.Next,
                        prev = response.Prev
                    };
                    return Results.Json(projectedResponse, jsonOptions);
                }

                return Results.Json(response, jsonOptions);
            }
            catch (Exception ex)
            {
                var errorResult = await EndpointHelpers.HandleErrorHookAsync(pipeline, httpContext, RestLibOperation.GetAll, ex);
                if (errorResult is not null) return errorResult;
                throw;
            }
        };
    }
}
