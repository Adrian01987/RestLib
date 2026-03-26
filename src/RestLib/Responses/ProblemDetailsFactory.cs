using Microsoft.AspNetCore.Http;
using RestLib.Filtering;
using RestLib.Sorting;

namespace RestLib.Responses;

/// <summary>
/// Factory for creating standardized Problem Details responses.
/// </summary>
public static class ProblemDetailsFactory
{
  /// <summary>
  /// Creates a 404 Not Found problem details response.
  /// </summary>
  /// <param name="entityName">The name of the entity type.</param>
  /// <param name="id">The requested resource ID.</param>
  /// <param name="instance">The request path.</param>
  public static RestLibProblemDetails NotFound(string entityName, object id, string? instance = null)
  {
    return new RestLibProblemDetails
    {
      Type = ProblemTypes.NotFound,
      Title = "Resource Not Found",
      Status = StatusCodes.Status404NotFound,
      Detail = $"{entityName} with ID '{id}' does not exist.",
      Instance = instance
    };
  }

  /// <summary>
  /// Creates a 400 Validation Failed problem details response.
  /// </summary>
  /// <param name="errors">Dictionary of field names to error messages.</param>
  /// <param name="instance">The request path.</param>
  public static RestLibProblemDetails ValidationFailed(
      IDictionary<string, string[]> errors,
      string? instance = null)
  {
    return new RestLibProblemDetails
    {
      Type = ProblemTypes.ValidationFailed,
      Title = "Validation Failed",
      Status = StatusCodes.Status400BadRequest,
      Detail = "One or more validation errors occurred.",
      Instance = instance,
      Errors = errors
    };
  }

  /// <summary>
  /// Creates a 400 Bad Request problem details response.
  /// </summary>
  /// <param name="detail">Description of what went wrong.</param>
  /// <param name="instance">The request path.</param>
  public static RestLibProblemDetails BadRequest(string detail, string? instance = null)
  {
    return new RestLibProblemDetails
    {
      Type = ProblemTypes.BadRequest,
      Title = "Bad Request",
      Status = StatusCodes.Status400BadRequest,
      Detail = detail,
      Instance = instance
    };
  }

  /// <summary>
  /// Creates a 400 Invalid Cursor problem details response.
  /// </summary>
  /// <param name="cursor">The invalid cursor value.</param>
  /// <param name="instance">The request path.</param>
  public static RestLibProblemDetails InvalidCursor(string cursor, string? instance = null)
  {
    return new RestLibProblemDetails
    {
      Type = ProblemTypes.InvalidCursor,
      Title = "Invalid Cursor",
      Status = StatusCodes.Status400BadRequest,
      Detail = "The provided cursor is not a valid pagination cursor.",
      Instance = instance
    };
  }

  /// <summary>
  /// Creates a 400 Invalid Limit problem details response.
  /// </summary>
  /// <param name="limit">The invalid limit value.</param>
  /// <param name="minLimit">The minimum allowed limit.</param>
  /// <param name="maxLimit">The maximum allowed limit.</param>
  /// <param name="instance">The request path.</param>
  public static RestLibProblemDetails InvalidLimit(int limit, int minLimit, int maxLimit, string? instance = null)
  {
    return new RestLibProblemDetails
    {
      Type = ProblemTypes.InvalidLimit,
      Title = "Invalid Limit",
      Status = StatusCodes.Status400BadRequest,
      Detail = $"The limit value '{limit}' is invalid. Limit must be between {minLimit} and {maxLimit}.",
      Instance = instance
    };
  }

  /// <summary>
  /// Creates a 400 Invalid Filter problem details response.
  /// </summary>
  /// <param name="errors">The filter validation errors.</param>
  /// <param name="instance">The request path.</param>
  public static RestLibProblemDetails InvalidFilters(
      IReadOnlyList<FilterValidationError> errors,
      string? instance = null)
  {
    var errorDict = errors.ToDictionary(
        e => e.ParameterName,
        e => new[] { e.Message });

    return new RestLibProblemDetails
    {
      Type = ProblemTypes.InvalidFilter,
      Title = "Invalid Filter Value",
      Status = StatusCodes.Status400BadRequest,
      Detail = errors.Count == 1
          ? $"The filter parameter '{errors[0].ParameterName}' has an invalid value."
          : $"Multiple filter parameters have invalid values.",
      Instance = instance,
      Errors = errorDict
    };
  }

  /// <summary>
  /// Creates a 400 Invalid Sort problem details response.
  /// </summary>
  /// <param name="errors">The sort validation errors.</param>
  /// <param name="instance">The request path.</param>
  public static RestLibProblemDetails InvalidSort(
      IReadOnlyList<SortValidationError> errors,
      string? instance = null)
  {
    var errorDict = errors.ToDictionary(
        e => e.Field,
        e => new[] { e.Message });

    return new RestLibProblemDetails
    {
      Type = ProblemTypes.InvalidSort,
      Title = "Invalid Sort Parameter",
      Status = StatusCodes.Status400BadRequest,
      Detail = errors.Count == 1
          ? $"The sort field '{errors[0].Field}' is invalid."
          : "One or more sort fields are invalid.",
      Instance = instance,
      Errors = errorDict
    };
  }

  /// <summary>
  /// Creates a 409 Conflict problem details response.
  /// </summary>
  /// <param name="detail">Description of the conflict.</param>
  /// <param name="instance">The request path.</param>
  public static RestLibProblemDetails Conflict(string detail, string? instance = null)
  {
    return new RestLibProblemDetails
    {
      Type = ProblemTypes.Conflict,
      Title = "Conflict",
      Status = StatusCodes.Status409Conflict,
      Detail = detail,
      Instance = instance
    };
  }

  /// <summary>
  /// Creates a 412 Precondition Failed problem details response.
  /// </summary>
  /// <param name="detail">Description of the precondition failure.</param>
  /// <param name="instance">The request path.</param>
  public static RestLibProblemDetails PreconditionFailed(string detail, string? instance = null)
  {
    return new RestLibProblemDetails
    {
      Type = ProblemTypes.PreconditionFailed,
      Title = "Precondition Failed",
      Status = StatusCodes.Status412PreconditionFailed,
      Detail = detail,
      Instance = instance
    };
  }

  /// <summary>
  /// Creates a 500 Internal Server Error problem details response.
  /// </summary>
  /// <param name="detail">Optional detail (only include in development).</param>
  /// <param name="instance">The request path.</param>
  public static RestLibProblemDetails InternalError(string? detail = null, string? instance = null)
  {
    return new RestLibProblemDetails
    {
      Type = ProblemTypes.InternalError,
      Title = "Internal Server Error",
      Status = StatusCodes.Status500InternalServerError,
      Detail = detail ?? "An unexpected error occurred.",
      Instance = instance
    };
  }
}
