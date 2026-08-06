using Microsoft.AspNetCore.Http;
using RestLib.Configuration;
using RestLib.FieldSelection;
using RestLib.Filtering;
using RestLib.Pagination;
using RestLib.Responses;
using RestLib.Search;
using RestLib.Sorting;

namespace RestLib.Endpoints;

/// <summary>
/// Contains the validated repository request and response projection settings for a collection query.
/// </summary>
internal readonly record struct CollectionQueryPlan(
    PaginationRequest PaginationRequest,
    IReadOnlyList<SelectedField> SelectedFields,
    int EffectiveLimit);

/// <summary>
/// Contains parsed collection-query values or the HTTP result for the first validation failure.
/// </summary>
internal readonly record struct CollectionQueryPreparation(
    string? Cursor,
    int? RequestedLimit,
    IReadOnlyList<FilterValue> Filters,
    IReadOnlyList<SortField> SortFields,
    IReadOnlyList<SelectedField> SelectedFields,
    SearchRequest? Search,
    IResult? ErrorResult)
{
    /// <summary>
    /// Applies post-validation pagination defaults and creates the repository query plan.
    /// </summary>
    /// <param name="options">The current RestLib options.</param>
    /// <returns>The repository query plan.</returns>
    internal CollectionQueryPlan CreatePlan(RestLibOptions options)
    {
        var effectiveLimit = Math.Clamp(RequestedLimit ?? options.DefaultPageSize, 1, options.MaxPageSize);
        var paginationRequest = new PaginationRequest
        {
            Cursor = Cursor,
            Limit = effectiveLimit,
            Filters = Filters,
            SortFields = SortFields,
            Search = Search
        };

        return new CollectionQueryPlan(paginationRequest, SelectedFields, effectiveLimit);
    }
}

/// <summary>
/// Validates and parses collection-query inputs shared by mapped and unmapped endpoints.
/// </summary>
internal static class CollectionQueryCoordinator
{
    /// <summary>
    /// Validates and parses one collection request in HTTP contract order.
    /// </summary>
    /// <typeparam name="TEntity">The configured API entity type.</typeparam>
    /// <typeparam name="TKey">The resource key type.</typeparam>
    /// <param name="request">The current HTTP request.</param>
    /// <param name="cursor">The bound cursor value.</param>
    /// <param name="limit">The bound page-size value.</param>
    /// <param name="options">The current RestLib options.</param>
    /// <param name="config">The endpoint configuration.</param>
    /// <param name="problems">The endpoint-scoped Problem Details responder.</param>
    /// <returns>The parsed values or the first validation failure.</returns>
    internal static CollectionQueryPreparation Prepare<TEntity, TKey>(
        HttpRequest request,
        string? cursor,
        int? limit,
        RestLibOptions options,
        RestLibEndpointConfiguration<TEntity, TKey> config,
        ProblemDetailsResponder problems)
        where TEntity : class
        where TKey : notnull
    {
        if (!string.IsNullOrEmpty(cursor))
        {
            if (cursor.Length > options.MaxCursorLength)
            {
                return Failure(
                    cursor,
                    limit,
                    problems.Create(ProblemDetailsFactory.InvalidCursor(
                        cursor,
                        request.Path,
                        $"The cursor exceeds the maximum allowed length of {options.MaxCursorLength} characters.")));
            }

            if (!CursorEncoder.IsValid(cursor))
            {
                return Failure(
                    cursor,
                    limit,
                    problems.Create(ProblemDetailsFactory.InvalidCursor(cursor, request.Path)));
            }
        }

        if (limit.HasValue && (limit.Value < 1 || limit.Value > options.MaxPageSize))
        {
            return Failure(
                cursor,
                limit,
                problems.Create(ProblemDetailsFactory.InvalidLimit(
                    limit.Value,
                    1,
                    options.MaxPageSize,
                    request.Path)));
        }

        IReadOnlyList<FilterValue> filterValues = [];
        if (config.HasFilters)
        {
            var filterResult = FilterParser.Parse(request.Query, config.FilterConfiguration, options.MaxFilterInListSize);
            if (!filterResult.IsValid)
            {
                return Failure(
                    cursor,
                    limit,
                    problems.Create(ProblemDetailsFactory.InvalidFilters(filterResult.Errors, request.Path)));
            }

            filterValues = filterResult.Filters;
        }

        IReadOnlyList<SortField> sortFields = [];
        if (config.HasSorting)
        {
            var rawSort = request.Query["sort"].FirstOrDefault();
            if (!string.IsNullOrEmpty(rawSort))
            {
                var sortResult = SortParser.Parse(rawSort, config.SortConfiguration);
                if (!sortResult.IsValid)
                {
                    return Failure(
                        cursor,
                        limit,
                        problems.Create(ProblemDetailsFactory.InvalidSort(sortResult.Errors, request.Path)));
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
            var rawFields = request.Query["fields"].FirstOrDefault();
            if (!string.IsNullOrEmpty(rawFields))
            {
                var fieldsResult = FieldSelectionParser.Parse(rawFields, config.FieldSelectionConfiguration);
                if (!fieldsResult.IsValid)
                {
                    return Failure(
                        cursor,
                        limit,
                        problems.Create(ProblemDetailsFactory.InvalidFields(fieldsResult.Errors, request.Path)));
                }

                selectedFields = fieldsResult.Fields;
            }
        }

        SearchRequest? search = null;
        if (config.HasSearch)
        {
            var searchResult = SearchParser.Parse(request.Query, config.SearchConfiguration);
            if (!searchResult.IsValid)
            {
                return Failure(
                    cursor,
                    limit,
                    problems.Create(ProblemDetailsFactory.InvalidSearch(searchResult.Errors, request.Path)));
            }

            search = searchResult.Search;
        }

        return new CollectionQueryPreparation(
            cursor,
            limit,
            filterValues,
            sortFields,
            selectedFields,
            search,
            ErrorResult: null);
    }

    private static CollectionQueryPreparation Failure(string? cursor, int? limit, IResult errorResult)
    {
        return new CollectionQueryPreparation(cursor, limit, [], [], [], Search: null, errorResult);
    }
}
