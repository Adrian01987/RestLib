using Microsoft.AspNetCore.Http;

namespace RestLib.Search;

/// <summary>
/// Parses and validates the search query parameter.
/// </summary>
internal static class SearchParser
{
    /// <summary>
    /// Parses a configured search query parameter from the request.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="query">The request query collection.</param>
    /// <param name="configuration">The search configuration.</param>
    /// <returns>The parse result.</returns>
    internal static SearchParseResult Parse<TEntity>(
        IQueryCollection query,
        SearchConfiguration<TEntity> configuration)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(configuration);

        var rawValues = query[configuration.QueryParameterName];
        var nonEmptyValues = new List<string>();

        foreach (var rawValue in rawValues)
        {
            if (!string.IsNullOrWhiteSpace(rawValue))
            {
                nonEmptyValues.Add(rawValue.Trim());
            }
        }

        if (nonEmptyValues.Count == 0)
        {
            return new SearchParseResult();
        }

        if (nonEmptyValues.Count > 1)
        {
            return new SearchParseResult
            {
                Errors =
                [
                    new SearchValidationError
                    {
                        ParameterName = configuration.QueryParameterName,
                        ProvidedValue = string.Join(", ", nonEmptyValues),
                        Message = $"Multiple values for search parameter '{configuration.QueryParameterName}' are not supported. Provide a single value."
                    }
                ]
            };
        }

        return new SearchParseResult
        {
            Search = new SearchRequest
            {
                Term = nonEmptyValues[0],
                QueryParameterName = configuration.QueryParameterName,
                CaseSensitive = configuration.CaseSensitive,
                Properties = configuration.Properties
                    .Select(property => new SearchPropertyConfiguration
                    {
                        PropertyName = property.PropertyName,
                        QueryParameterName = property.QueryParameterName
                    })
                    .ToList()
            }
        };
    }
}
