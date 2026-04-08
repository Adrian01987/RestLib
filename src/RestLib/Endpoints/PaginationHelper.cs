using Microsoft.AspNetCore.Http;
using RestLib.Configuration;
using RestLib.Pagination;
using RestLib.Responses;

namespace RestLib.Endpoints;

/// <summary>
/// Helper methods for building paginated collection responses and pagination URLs.
/// </summary>
internal static class PaginationHelper
{
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
    /// <param name="totalCount">
    /// The total number of matching entities, or <c>null</c> when the repository
    /// does not implement <see cref="Abstractions.ICountableRepository{TEntity, TKey}"/>.
    /// </param>
    /// <returns>A collection response with pagination links.</returns>
    internal static CollectionResponse<T> BuildCollectionResponse<T>(
        PagedResult<T> result,
        HttpRequest request,
        string? currentCursor,
        int limit,
        RestLibOptions options,
        long? totalCount = null)
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
            TotalCount = totalCount,
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
    private static IReadOnlyList<KeyValuePair<string, string>> GetFilterQueryParams(IQueryCollection query)
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
    private static string BuildPaginationUrl(
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
}
