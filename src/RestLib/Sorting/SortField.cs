namespace RestLib.Sorting;

/// <summary>
/// Specifies the direction of a sort operation.
/// </summary>
public enum SortDirection
{
    /// <summary>Ascending order (smallest to largest).</summary>
    Asc,

    /// <summary>Descending order (largest to smallest).</summary>
    Desc
}

/// <summary>
/// Represents a single sort field with its direction.
/// </summary>
public class SortField
{
    /// <summary>
    /// Gets the C# property name (e.g., "Price").
    /// </summary>
    public required string PropertyName { get; init; }

    /// <summary>
    /// Gets the query parameter name in snake_case (e.g., "price").
    /// </summary>
    public required string QueryParameterName { get; init; }

    /// <summary>
    /// Gets the sort direction.
    /// </summary>
    public required SortDirection Direction { get; init; }
}

/// <summary>
/// Represents a sort validation error.
/// </summary>
public class SortValidationError
{
    /// <summary>
    /// Gets the raw field value that the client sent.
    /// </summary>
    public required string Field { get; init; }

    /// <summary>
    /// Gets the error message.
    /// </summary>
    public required string Message { get; init; }
}

/// <summary>
/// Represents the result of parsing a sort query parameter.
/// </summary>
public class SortParseResult
{
    /// <summary>
    /// Gets the successfully parsed sort fields.
    /// </summary>
    public IReadOnlyList<SortField> Fields { get; init; } = [];

    /// <summary>
    /// Gets any validation errors that occurred during parsing.
    /// </summary>
    public IReadOnlyList<SortValidationError> Errors { get; init; } = [];

    /// <summary>
    /// Gets whether parsing was successful (no errors).
    /// </summary>
    public bool IsValid => Errors.Count == 0;
}
