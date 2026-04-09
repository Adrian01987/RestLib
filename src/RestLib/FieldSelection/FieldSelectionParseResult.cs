namespace RestLib.FieldSelection;

/// <summary>
/// Represents a single validated field from the fields query parameter.
/// </summary>
public class SelectedField
{
    /// <summary>
    /// Gets the C# property name (e.g., "CategoryId").
    /// </summary>
    public required string PropertyName { get; init; }

    /// <summary>
    /// Gets the snake_case query parameter name (e.g., "category_id").
    /// </summary>
    public required string QueryParameterName { get; init; }
}

/// <summary>
/// Represents a field selection validation error.
/// </summary>
public class FieldSelectionValidationError
{
    /// <summary>
    /// Gets the invalid field name that was requested.
    /// </summary>
    public required string Field { get; init; }

    /// <summary>
    /// Gets the error message.
    /// </summary>
    public required string Message { get; init; }
}

/// <summary>
/// Represents the result of parsing the fields query parameter.
/// </summary>
public class FieldSelectionParseResult
{
    /// <summary>
    /// Gets the successfully parsed selected fields.
    /// </summary>
    public IReadOnlyList<SelectedField> Fields { get; init; } = [];

    /// <summary>
    /// Gets any validation errors that occurred during parsing.
    /// </summary>
    public IReadOnlyList<FieldSelectionValidationError> Errors { get; init; } = [];

    /// <summary>
    /// Gets whether parsing was successful (no errors).
    /// </summary>
    public bool IsValid => Errors.Count == 0;
}
