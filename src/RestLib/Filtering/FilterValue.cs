namespace RestLib.Filtering;

/// <summary>
/// Represents a single filter value from a query parameter.
/// </summary>
public class FilterValue
{
  /// <summary>
  /// Gets the C# property name (e.g., "CategoryId").
  /// </summary>
  public required string PropertyName { get; init; }

  /// <summary>
  /// Gets the query parameter name (e.g., "category_id").
  /// </summary>
  public required string QueryParameterName { get; init; }

  /// <summary>
  /// Gets the property type for conversion.
  /// </summary>
  public required Type PropertyType { get; init; }

  /// <summary>
  /// Gets the raw string value from the query string.
  /// </summary>
  public required string RawValue { get; init; }

  /// <summary>
  /// Gets the converted/typed value, or null if conversion failed.
  /// </summary>
  public object? TypedValue { get; init; }
}

/// <summary>
/// Represents filter parsing result including any errors.
/// </summary>
public class FilterParseResult
{
  /// <summary>
  /// Gets the successfully parsed filter values.
  /// </summary>
  public required IReadOnlyList<FilterValue> Values { get; init; }

  /// <summary>
  /// Gets any validation errors that occurred during parsing.
  /// </summary>
  public required IReadOnlyList<FilterValidationError> Errors { get; init; }

  /// <summary>
  /// Gets whether parsing was successful (no errors).
  /// </summary>
  public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Represents a filter validation error.
/// </summary>
public class FilterValidationError
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
  /// Gets the expected type for the parameter.
  /// </summary>
  public required Type ExpectedType { get; init; }

  /// <summary>
  /// Gets the error message.
  /// </summary>
  public required string Message { get; init; }
}
