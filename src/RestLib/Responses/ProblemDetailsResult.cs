using System.Text.Json;
using Microsoft.AspNetCore.Http;
using RestLib.FieldSelection;
using RestLib.Filtering;
using RestLib.Sorting;

namespace RestLib.Responses;

/// <summary>
/// Helper for returning Problem Details responses with correct content type.
/// </summary>
public static class ProblemDetailsResult
{
  private const string ProblemJsonContentType = "application/problem+json";

  /// <summary>
  /// Creates an IResult that returns the problem details with the correct content type.
  /// </summary>
  /// <param name="problem">The problem details to return.</param>
  /// <param name="jsonOptions">Optional JSON serializer options.</param>
  public static IResult Create(RestLibProblemDetails problem, JsonSerializerOptions? jsonOptions = null)
  {
    return Results.Json(
        problem,
        jsonOptions,
        contentType: ProblemJsonContentType,
        statusCode: problem.Status);
  }

  /// <summary>
  /// Creates a 404 Not Found result.
  /// </summary>
  public static IResult NotFound(
      string entityName,
      object id,
      string? instance = null,
      JsonSerializerOptions? jsonOptions = null)
  {
    var problem = ProblemDetailsFactory.NotFound(entityName, id, instance);
    return Create(problem, jsonOptions);
  }

  /// <summary>
  /// Creates a 400 Validation Failed result.
  /// </summary>
  public static IResult ValidationFailed(
      IDictionary<string, string[]> errors,
      string? instance = null,
      JsonSerializerOptions? jsonOptions = null)
  {
    var problem = ProblemDetailsFactory.ValidationFailed(errors, instance);
    return Create(problem, jsonOptions);
  }

  /// <summary>
  /// Creates a 400 Bad Request result.
  /// </summary>
  public static IResult BadRequest(
      string detail,
      string? instance = null,
      JsonSerializerOptions? jsonOptions = null)
  {
    var problem = ProblemDetailsFactory.BadRequest(detail, instance);
    return Create(problem, jsonOptions);
  }

  /// <summary>
  /// Creates a 400 Invalid Cursor result.
  /// </summary>
  public static IResult InvalidCursor(
      string cursor,
      string? instance = null,
      JsonSerializerOptions? jsonOptions = null)
  {
    var problem = ProblemDetailsFactory.InvalidCursor(cursor, instance);
    return Create(problem, jsonOptions);
  }

  /// <summary>
  /// Creates a 400 Invalid Limit result.
  /// </summary>
  public static IResult InvalidLimit(
      int limit,
      int minLimit,
      int maxLimit,
      string? instance = null,
      JsonSerializerOptions? jsonOptions = null)
  {
    var problem = ProblemDetailsFactory.InvalidLimit(limit, minLimit, maxLimit, instance);
    return Create(problem, jsonOptions);
  }

  /// <summary>
  /// Creates a 400 Invalid Filters result.
  /// </summary>
  public static IResult InvalidFilters(
      IReadOnlyList<FilterValidationError> errors,
      string? instance = null,
      JsonSerializerOptions? jsonOptions = null)
  {
    var problem = ProblemDetailsFactory.InvalidFilters(errors, instance);
    return Create(problem, jsonOptions);
  }

  /// <summary>
  /// Creates a 400 Invalid Sort result.
  /// </summary>
  public static IResult InvalidSort(
      IReadOnlyList<SortValidationError> errors,
      string? instance = null,
      JsonSerializerOptions? jsonOptions = null)
  {
    var problem = ProblemDetailsFactory.InvalidSort(errors, instance);
    return Create(problem, jsonOptions);
  }

  /// <summary>
  /// Creates a 400 Invalid Fields result.
  /// </summary>
  public static IResult InvalidFields(
      IReadOnlyList<FieldSelectionValidationError> errors,
      string? instance = null,
      JsonSerializerOptions? jsonOptions = null)
  {
    var problem = ProblemDetailsFactory.InvalidFields(errors, instance);
    return Create(problem, jsonOptions);
  }

  /// <summary>
  /// Creates a 409 Conflict result.
  /// </summary>
  public static IResult Conflict(
      string detail,
      string? instance = null,
      JsonSerializerOptions? jsonOptions = null)
  {
    var problem = ProblemDetailsFactory.Conflict(detail, instance);
    return Create(problem, jsonOptions);
  }

  /// <summary>
  /// Creates a 412 Precondition Failed result.
  /// </summary>
  public static IResult PreconditionFailed(
      string detail,
      string? instance = null,
      JsonSerializerOptions? jsonOptions = null)
  {
    var problem = ProblemDetailsFactory.PreconditionFailed(detail, instance);
    return Create(problem, jsonOptions);
  }

  /// <summary>
  /// Creates a 500 Internal Server Error result.
  /// </summary>
  public static IResult InternalError(
      string? detail = null,
      string? instance = null,
      JsonSerializerOptions? jsonOptions = null)
  {
    var problem = ProblemDetailsFactory.InternalError(detail, instance);
    return Create(problem, jsonOptions);
  }
}
