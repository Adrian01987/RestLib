using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using RestLib.Configuration;
using RestLib.FieldSelection;
using RestLib.Filtering;
using RestLib.Logging;
using RestLib.Search;
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
    public static IResult Create(
        RestLibProblemDetails problem,
        JsonSerializerOptions? jsonOptions = null,
        ILogger? logger = null)
    {
        return Create(problem, jsonOptions, logger, options: null);
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
        return Create(ProblemDetailsFactory.NotFound(entityName, id, instance), jsonOptions, logger);
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
        return Create(ProblemDetailsFactory.ValidationFailed(errors, instance), jsonOptions, logger);
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
        return Create(ProblemDetailsFactory.BadRequest(detail, instance), jsonOptions, logger);
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
        return Create(ProblemDetailsFactory.InvalidCursor(cursor, instance, detail), jsonOptions, logger);
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
        return Create(ProblemDetailsFactory.InvalidLimit(limit, minLimit, maxLimit, instance), jsonOptions, logger);
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
        return Create(ProblemDetailsFactory.InvalidFilters(errors, instance), jsonOptions, logger);
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
        return Create(ProblemDetailsFactory.InvalidSort(errors, instance), jsonOptions, logger);
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
        return Create(ProblemDetailsFactory.InvalidFields(errors, instance), jsonOptions, logger);
    }

    /// <summary>
    /// Creates a 400 Invalid Search result.
    /// </summary>
    /// <param name="errors">The search validation errors.</param>
    /// <param name="instance">The request path.</param>
    /// <param name="jsonOptions">Optional JSON serializer options.</param>
    /// <param name="logger">Optional logger; when provided, the response is logged at the appropriate level.</param>
    public static IResult InvalidSearch(
        IReadOnlyList<SearchValidationError> errors,
        string? instance = null,
        JsonSerializerOptions? jsonOptions = null,
        ILogger? logger = null)
    {
        return Create(ProblemDetailsFactory.InvalidSearch(errors, instance), jsonOptions, logger);
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
        return Create(ProblemDetailsFactory.InvalidBatchRequest(detail, errors, instance), jsonOptions, logger);
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
        return Create(ProblemDetailsFactory.BatchSizeExceeded(itemCount, maxBatchSize, instance), jsonOptions, logger);
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
        return Create(ProblemDetailsFactory.BatchActionNotEnabled(action, enabledActions, instance), jsonOptions, logger);
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
        return Create(ProblemDetailsFactory.Conflict(detail, instance), jsonOptions, logger);
    }

    /// <summary>
    /// Creates a 409 Insufficient Stock result.
    /// </summary>
    /// <param name="detail">The stock conflict detail message.</param>
    /// <param name="productId">The product identifier.</param>
    /// <param name="requested">The requested quantity.</param>
    /// <param name="available">The available quantity.</param>
    /// <param name="instance">The request path.</param>
    /// <param name="jsonOptions">Optional JSON serializer options.</param>
    /// <param name="logger">Optional logger; when provided, the response is logged at the appropriate level.</param>
    public static IResult InsufficientStock(
        string detail,
        string productId,
        int requested,
        int available,
        string? instance = null,
        JsonSerializerOptions? jsonOptions = null,
        ILogger? logger = null)
    {
        return Create(
            ProblemDetailsFactory.InsufficientStock(detail, productId, requested, available, instance),
            jsonOptions,
            logger);
    }

    /// <summary>
    /// Creates a 409 Invalid Status Transition result.
    /// </summary>
    /// <param name="fromStatus">The current status.</param>
    /// <param name="toStatus">The requested target status.</param>
    /// <param name="instance">The request path.</param>
    /// <param name="jsonOptions">Optional JSON serializer options.</param>
    /// <param name="logger">Optional logger; when provided, the response is logged at the appropriate level.</param>
    public static IResult InvalidStatusTransition(
        string fromStatus,
        string toStatus,
        string? instance = null,
        JsonSerializerOptions? jsonOptions = null,
        ILogger? logger = null)
    {
        return Create(ProblemDetailsFactory.InvalidStatusTransition(fromStatus, toStatus, instance), jsonOptions, logger);
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
        return Create(ProblemDetailsFactory.PreconditionFailed(detail, instance), jsonOptions, logger);
    }

    /// <summary>
    /// Creates a 501 Not Implemented result when a repository cannot perform an atomic conditional write.
    /// </summary>
    /// <param name="detail">The unsupported capability detail message.</param>
    /// <param name="instance">The request path.</param>
    /// <param name="jsonOptions">Optional JSON serializer options.</param>
    /// <param name="logger">Optional logger; when provided, the response is logged at the appropriate level.</param>
    /// <returns>A Problem Details result.</returns>
    public static IResult ConditionalWriteNotSupported(
        string detail,
        string? instance = null,
        JsonSerializerOptions? jsonOptions = null,
        ILogger? logger = null)
    {
        return Create(ProblemDetailsFactory.ConditionalWriteNotSupported(detail, instance), jsonOptions, logger);
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
        return Create(ProblemDetailsFactory.InternalError(detail, instance), jsonOptions, logger);
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
        return Create(ProblemDetailsFactory.HookShortCircuit(statusCode, instance), jsonOptions, logger);
    }

    /// <summary>
    /// Creates an endpoint-scoped responder bound to the option-aware result pipeline.
    /// </summary>
    /// <param name="jsonOptions">The endpoint JSON serializer settings.</param>
    /// <param name="logger">The optional endpoint logger.</param>
    /// <param name="options">The RestLib response settings.</param>
    /// <returns>A responder that applies the supplied settings to every occurrence.</returns>
    internal static ProblemDetailsResponder CreateResponder(
        JsonSerializerOptions? jsonOptions,
        ILogger? logger,
        RestLibOptions? options)
    {
        return new ProblemDetailsResponder(jsonOptions, logger, options);
    }

    /// <summary>
    /// Creates an option-aware IResult for RestLib endpoint handlers.
    /// </summary>
    internal static IResult Create(
        RestLibProblemDetails problem,
        JsonSerializerOptions? jsonOptions,
        ILogger? logger,
        RestLibOptions? options)
    {
        problem = ApplyOptions(problem, options);

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

        if (options?.UseProblemDetails == false)
        {
            var error = new
            {
                error = problem.Title,
                problem.Status,
                problem.Detail,
                problem.Instance,
                problem.Errors
            };

            return Results.Json(
                error,
                jsonOptions,
                statusCode: problem.Status);
        }

        return Results.Json(
            problem,
            jsonOptions,
            contentType: ProblemJsonContentType,
            statusCode: problem.Status);
    }

    private static RestLibProblemDetails ApplyOptions(RestLibProblemDetails problem, RestLibOptions? options)
    {
        if (options?.ProblemTypeBaseUri is null || !problem.Type.StartsWith("/problems/", StringComparison.Ordinal))
        {
            return problem;
        }

        return new RestLibProblemDetails
        {
            Type = ProblemTypes.Resolve(problem.Type, options.ProblemTypeBaseUri),
            Title = problem.Title,
            Status = problem.Status,
            Detail = problem.Detail,
            Instance = problem.Instance,
            Errors = problem.Errors,
            Extensions = problem.Extensions
        };
    }
}
