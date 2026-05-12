namespace RestLib.Search;

/// <summary>
/// Represents a search request for a collection query.
/// </summary>
public class SearchRequest
{
    /// <summary>
    /// Gets the trimmed search term.
    /// </summary>
    public required string Term { get; init; }

    /// <summary>
    /// Gets the query parameter name used for search.
    /// </summary>
    public required string QueryParameterName { get; init; }

    /// <summary>
    /// Gets a value indicating whether matching is case-sensitive.
    /// </summary>
    public bool CaseSensitive { get; init; }

    /// <summary>
    /// Gets the configured searchable properties.
    /// </summary>
    public IReadOnlyList<SearchPropertyConfiguration> Properties { get; init; } = [];
}

/// <summary>
/// Represents a search validation error.
/// </summary>
public class SearchValidationError
{
    /// <summary>
    /// Gets the query parameter name that had the error.
    /// </summary>
    public required string ParameterName { get; init; }

    /// <summary>
    /// Gets the invalid value that was provided.
    /// </summary>
    public required string ProvidedValue { get; init; }

    /// <summary>
    /// Gets the error message.
    /// </summary>
    public required string Message { get; init; }
}

/// <summary>
/// Represents the result of parsing a search query parameter.
/// </summary>
internal sealed class SearchParseResult
{
    /// <summary>
    /// Gets the parsed search request, if one was provided.
    /// </summary>
    internal SearchRequest? Search { get; init; }

    /// <summary>
    /// Gets any validation errors that occurred during parsing.
    /// </summary>
    internal IReadOnlyList<SearchValidationError> Errors { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether parsing was successful.
    /// </summary>
    internal bool IsValid => Errors.Count == 0;
}
