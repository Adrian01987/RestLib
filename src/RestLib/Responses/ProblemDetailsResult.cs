using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using RestLib.FieldSelection;
using RestLib.Filtering;
using RestLib.Logging;
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
    /// <param name="logger">Optional logger; when provided, the response is logged at the appropriate level.</param>
    public static IResult Create(RestLibProblemDetails problem, JsonSerializerOptions? jsonOptions = null, ILogger? logger = null)
    {
        if (logger is not null)
        {
            if (problem.Status >= 500)
            {
                RestLibLogMessages.ProblemDetailsServerError(logger, problem.Status, problem.Type, problem.Instance);
            }
            else
            {
                RestLibLogMessages.ProblemDetailsClientError(logger, problem.Status, problem.Type, problem.Instance);
            }
        }

        return Results.Json(
            problem,
            jsonOptions,
            contentType: ProblemJsonContentType,
            statusCode: problem.Status);
    }

    /// <summary>
    /// Creates a 404 Not Found result.
    /// </summary>
    /// <param name="entityName">The entity type name.</param>
    /// <param name="id">The entity identifier that was not found.</param>
    /// <param name="instance">The request path.</param>
    /// <param name="jsonOptions">Optional JSON serializer options.</param>
    /// <param name="logger">Optional logger; when provided, the response is logged at the appropriate level.</param>
    public static IResult NotFound(
        string entityName,
        object id,
        string? instance = null,
        JsonSerializerOptions? jsonOptions = null,
        ILogger? logger = null)
    {
        var problem = ProblemDetailsFactory.NotFound(entityName, id, instance);
        return Create(problem, jsonOptions, logger);
    }

    /// <summary>
    /// Creates a 400 Validation Failed result.
    /// </summary>
    /// <param name="errors">The validation errors keyed by field name.</param>
    /// <param name="instance">The request path.</param>
    /// <param name="jsonOptions">Optional JSON serializer options.</param>
    /// <param name="logger">Optional logger; when provided, the response is logged at the appropriate level.</param>
    public static IResult ValidationFailed(
        IReadOnlyDictionary<string, string[]> errors,
        string? instance = null,
        JsonSerializerOptions? jsonOptions = null,
        ILogger? logger = null)
    {
        var problem = ProblemDetailsFactory.ValidationFailed(errors, instance);
        return Create(problem, jsonOptions, logger);
    }

    /// <summary>
    /// Creates a 400 Bad Request result.
    /// </summary>
    /// <param name="detail">The error detail message.</param>
    /// <param name="instance">The request path.</param>
    /// <param name="jsonOptions">Optional JSON serializer options.</param>
    /// <param name="logger">Optional logger; when provided, the response is logged at the appropriate level.</param>
    public static IResult BadRequest(
        string detail,
        string? instance = null,
        JsonSerializerOptions? jsonOptions = null,
        ILogger? logger = null)
    {
        var problem = ProblemDetailsFactory.BadRequest(detail, instance);
        return Create(problem, jsonOptions, logger);
    }

    /// <summary>
    /// Creates a 400 Invalid Cursor result.
    /// </summary>
    /// <param name="cursor">The invalid cursor value.</param>
    /// <param name="instance">The request path.</param>
    /// <param name="jsonOptions">Optional JSON serializer options.</param>
    /// <param name="detail">Optional detail message; when <c>null</c> a default message is used.</param>
    /// <param name="logger">Optional logger; when provided, the response is logged at the appropriate level.</param>
    public static IResult InvalidCursor(
        string cursor,
        string? instance = null,
        JsonSerializerOptions? jsonOptions = null,
        string? detail = null,
        ILogger? logger = null)
    {
        var problem = ProblemDetailsFactory.InvalidCursor(cursor, instance, detail);
        return Create(problem, jsonOptions, logger);
    }

    /// <summary>
    /// Creates a 400 Invalid Limit result.
    /// </summary>
    /// <param name="limit">The invalid limit value.</param>
    /// <param name="minLimit">The minimum allowed limit.</param>
    /// <param name="maxLimit">The maximum allowed limit.</param>
    /// <param name="instance">The request path.</param>
    /// <param name="jsonOptions">Optional JSON serializer options.</param>
    /// <param name="logger">Optional logger; when provided, the response is logged at the appropriate level.</param>
    public static IResult InvalidLimit(
        int limit,
        int minLimit,
        int maxLimit,
        string? instance = null,
        JsonSerializerOptions? jsonOptions = null,
        ILogger? logger = null)
    {
        var problem = ProblemDetailsFactory.InvalidLimit(limit, minLimit, maxLimit, instance);
        return Create(problem, jsonOptions, logger);
    }

    /// <summary>
    /// Creates a 400 Invalid Filters result.
    /// </summary>
    /// <param name="errors">The filter validation errors.</param>
    /// <param name="instance">The request path.</param>
    /// <param name="jsonOptions">Optional JSON serializer options.</param>
    /// <param name="logger">Optional logger; when provided, the response is logged at the appropriate level.</param>
    public static IResult InvalidFilters(
        IReadOnlyList<FilterValidationError> errors,
        string? instance = null,
        JsonSerializerOptions? jsonOptions = null,
        ILogger? logger = null)
    {
        var problem = ProblemDetailsFactory.InvalidFilters(errors, instance);
        return Create(problem, jsonOptions, logger);
    }

    /// <summary>
    /// Creates a 400 Invalid Sort result.
    /// </summary>
    /// <param name="errors">The sort validation errors.</param>
    /// <param name="instance">The request path.</param>
    /// <param name="jsonOptions">Optional JSON serializer options.</param>
    /// <param name="logger">Optional logger; when provided, the response is logged at the appropriate level.</param>
    public static IResult InvalidSort(
        IReadOnlyList<SortValidationError> errors,
        string? instance = null,
        JsonSerializerOptions? jsonOptions = null,
        ILogger? logger = null)
    {
        var problem = ProblemDetailsFactory.InvalidSort(errors, instance);
        return Create(problem, jsonOptions, logger);
    }

    /// <summary>
    /// Creates a 400 Invalid Fields result.
    /// </summary>
    /// <param name="errors">The field selection validation errors.</param>
    /// <param name="instance">The request path.</param>
    /// <param name="jsonOptions">Optional JSON serializer options.</param>
    /// <param name="logger">Optional logger; when provided, the response is logged at the appropriate level.</param>
    public static IResult InvalidFields(
        IReadOnlyList<FieldSelectionValidationError> errors,
        string? instance = null,
        JsonSerializerOptions? jsonOptions = null,
        ILogger? logger = null)
    {
        var problem = ProblemDetailsFactory.InvalidFields(errors, instance);
        return Create(problem, jsonOptions, logger);
    }

    /// <summary>
    /// Creates a 400 Invalid Batch Request result.
    /// </summary>
    /// <param name="detail">Description of the batch validation error.</param>
    /// <param name="errors">Optional field-level errors.</param>
    /// <param name="instance">The request path.</param>
    /// <param name="jsonOptions">Optional JSON serializer options.</param>
    /// <param name="logger">Optional logger; when provided, the response is logged at the appropriate level.</param>
    public static IResult InvalidBatchRequest(
        string detail,
        IReadOnlyDictionary<string, string[]>? errors = null,
        string? instance = null,
        JsonSerializerOptions? jsonOptions = null,
        ILogger? logger = null)
    {
        var problem = ProblemDetailsFactory.InvalidBatchRequest(detail, errors, instance);
        return Create(problem, jsonOptions, logger);
    }

    /// <summary>
    /// Creates a 400 Batch Size Exceeded result.
    /// </summary>
    /// <param name="itemCount">The number of items in the request.</param>
    /// <param name="maxBatchSize">The maximum allowed batch size.</param>
    /// <param name="instance">The request path.</param>
    /// <param name="jsonOptions">Optional JSON serializer options.</param>
    /// <param name="logger">Optional logger; when provided, the response is logged at the appropriate level.</param>
    public static IResult BatchSizeExceeded(
        int itemCount,
        int maxBatchSize,
        string? instance = null,
        JsonSerializerOptions? jsonOptions = null,
        ILogger? logger = null)
    {
        var problem = ProblemDetailsFactory.BatchSizeExceeded(itemCount, maxBatchSize, instance);
        return Create(problem, jsonOptions, logger);
    }

    /// <summary>
    /// Creates a 400 Batch Action Not Enabled result.
    /// </summary>
    /// <param name="action">The requested batch action.</param>
    /// <param name="enabledActions">The actions enabled for this resource.</param>
    /// <param name="instance">The request path.</param>
    /// <param name="jsonOptions">Optional JSON serializer options.</param>
    /// <param name="logger">Optional logger; when provided, the response is logged at the appropriate level.</param>
    public static IResult BatchActionNotEnabled(
        string action,
        IEnumerable<string> enabledActions,
        string? instance = null,
        JsonSerializerOptions? jsonOptions = null,
        ILogger? logger = null)
    {
        var problem = ProblemDetailsFactory.BatchActionNotEnabled(action, enabledActions, instance);
        return Create(problem, jsonOptions, logger);
    }

    /// <summary>
    /// Creates a 409 Conflict result.
    /// </summary>
    /// <param name="detail">The conflict detail message.</param>
    /// <param name="instance">The request path.</param>
    /// <param name="jsonOptions">Optional JSON serializer options.</param>
    /// <param name="logger">Optional logger; when provided, the response is logged at the appropriate level.</param>
    public static IResult Conflict(
        string detail,
        string? instance = null,
        JsonSerializerOptions? jsonOptions = null,
        ILogger? logger = null)
    {
        var problem = ProblemDetailsFactory.Conflict(detail, instance);
        return Create(problem, jsonOptions, logger);
    }

    /// <summary>
    /// Creates a 412 Precondition Failed result.
    /// </summary>
    /// <param name="detail">The precondition failure detail message.</param>
    /// <param name="instance">The request path.</param>
    /// <param name="jsonOptions">Optional JSON serializer options.</param>
    /// <param name="logger">Optional logger; when provided, the response is logged at the appropriate level.</param>
    public static IResult PreconditionFailed(
        string detail,
        string? instance = null,
        JsonSerializerOptions? jsonOptions = null,
        ILogger? logger = null)
    {
        var problem = ProblemDetailsFactory.PreconditionFailed(detail, instance);
        return Create(problem, jsonOptions, logger);
    }

    /// <summary>
    /// Creates a 500 Internal Server Error result.
    /// </summary>
    /// <param name="detail">Optional error detail message.</param>
    /// <param name="instance">The request path.</param>
    /// <param name="jsonOptions">Optional JSON serializer options.</param>
    /// <param name="logger">Optional logger; when provided, the response is logged at the appropriate level.</param>
    public static IResult InternalError(
        string? detail = null,
        string? instance = null,
        JsonSerializerOptions? jsonOptions = null,
        ILogger? logger = null)
    {
        var problem = ProblemDetailsFactory.InternalError(detail, instance);
        return Create(problem, jsonOptions, logger);
    }

    /// <summary>
    /// Creates a hook short-circuit result with the given status code.
    /// </summary>
    /// <param name="statusCode">The HTTP status code from the hook's early result.</param>
    /// <param name="instance">The request path.</param>
    /// <param name="jsonOptions">Optional JSON serializer options.</param>
    /// <param name="logger">Optional logger; when provided, the response is logged at the appropriate level.</param>
    public static IResult HookShortCircuit(
        int statusCode,
        string? instance = null,
        JsonSerializerOptions? jsonOptions = null,
        ILogger? logger = null)
    {
        var problem = ProblemDetailsFactory.HookShortCircuit(statusCode, instance);
        return Create(problem, jsonOptions, logger);
    }
}
