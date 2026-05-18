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
        return NotFound(entityName, id, instance, jsonOptions, logger, options: null);
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
        return ValidationFailed(errors, instance, jsonOptions, logger, options: null);
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
        return BadRequest(detail, instance, jsonOptions, logger, options: null);
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
        return InvalidCursor(cursor, instance, jsonOptions, detail, logger, options: null);
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
        return InvalidLimit(limit, minLimit, maxLimit, instance, jsonOptions, logger, options: null);
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
        return InvalidFilters(errors, instance, jsonOptions, logger, options: null);
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
        return InvalidSort(errors, instance, jsonOptions, logger, options: null);
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
        return InvalidFields(errors, instance, jsonOptions, logger, options: null);
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
        return InvalidSearch(errors, instance, jsonOptions, logger, options: null);
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
        return InvalidBatchRequest(detail, errors, instance, jsonOptions, logger, options: null);
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
        return BatchSizeExceeded(itemCount, maxBatchSize, instance, jsonOptions, logger, options: null);
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
        return BatchActionNotEnabled(action, enabledActions, instance, jsonOptions, logger, options: null);
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
        return Conflict(detail, instance, jsonOptions, logger, options: null);
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
        return InsufficientStock(detail, productId, requested, available, instance, jsonOptions, logger, options: null);
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
        return InvalidStatusTransition(fromStatus, toStatus, instance, jsonOptions, logger, options: null);
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
        return PreconditionFailed(detail, instance, jsonOptions, logger, options: null);
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
        return InternalError(detail, instance, jsonOptions, logger, options: null);
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
        return HookShortCircuit(statusCode, instance, jsonOptions, logger, options: null);
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

    /// <summary>
    /// Creates an option-aware 404 Not Found result for RestLib endpoint handlers.
    /// </summary>
    internal static IResult NotFound(
        string entityName,
        object id,
        string? instance,
        JsonSerializerOptions? jsonOptions,
        ILogger? logger,
        RestLibOptions? options)
    {
        var problem = ProblemDetailsFactory.NotFound(entityName, id, instance);
        return Create(problem, jsonOptions, logger, options);
    }

    /// <summary>
    /// Creates an option-aware 400 Validation Failed result for RestLib endpoint handlers.
    /// </summary>
    internal static IResult ValidationFailed(
        IReadOnlyDictionary<string, string[]> errors,
        string? instance,
        JsonSerializerOptions? jsonOptions,
        ILogger? logger,
        RestLibOptions? options)
    {
        var problem = ProblemDetailsFactory.ValidationFailed(errors, instance);
        return Create(problem, jsonOptions, logger, options);
    }

    /// <summary>
    /// Creates an option-aware 400 Bad Request result for RestLib endpoint handlers.
    /// </summary>
    internal static IResult BadRequest(
        string detail,
        string? instance,
        JsonSerializerOptions? jsonOptions,
        ILogger? logger,
        RestLibOptions? options)
    {
        var problem = ProblemDetailsFactory.BadRequest(detail, instance);
        return Create(problem, jsonOptions, logger, options);
    }

    /// <summary>
    /// Creates an option-aware 400 Invalid Cursor result for RestLib endpoint handlers.
    /// </summary>
    internal static IResult InvalidCursor(
        string cursor,
        string? instance,
        JsonSerializerOptions? jsonOptions,
        string? detail,
        ILogger? logger,
        RestLibOptions? options)
    {
        var problem = ProblemDetailsFactory.InvalidCursor(cursor, instance, detail);
        return Create(problem, jsonOptions, logger, options);
    }

    /// <summary>
    /// Creates an option-aware 400 Invalid Cursor result with the default detail for RestLib endpoint handlers.
    /// </summary>
    internal static IResult InvalidCursor(
        string cursor,
        string? instance,
        JsonSerializerOptions? jsonOptions,
        ILogger? logger,
        RestLibOptions? options)
    {
        return InvalidCursor(cursor, instance, jsonOptions, detail: null, logger, options);
    }

    /// <summary>
    /// Creates an option-aware 400 Invalid Limit result for RestLib endpoint handlers.
    /// </summary>
    internal static IResult InvalidLimit(
        int limit,
        int minLimit,
        int maxLimit,
        string? instance,
        JsonSerializerOptions? jsonOptions,
        ILogger? logger,
        RestLibOptions? options)
    {
        var problem = ProblemDetailsFactory.InvalidLimit(limit, minLimit, maxLimit, instance);
        return Create(problem, jsonOptions, logger, options);
    }

    /// <summary>
    /// Creates an option-aware 400 Invalid Filters result for RestLib endpoint handlers.
    /// </summary>
    internal static IResult InvalidFilters(
        IReadOnlyList<FilterValidationError> errors,
        string? instance,
        JsonSerializerOptions? jsonOptions,
        ILogger? logger,
        RestLibOptions? options)
    {
        var problem = ProblemDetailsFactory.InvalidFilters(errors, instance);
        return Create(problem, jsonOptions, logger, options);
    }

    /// <summary>
    /// Creates an option-aware 400 Invalid Sort result for RestLib endpoint handlers.
    /// </summary>
    internal static IResult InvalidSort(
        IReadOnlyList<SortValidationError> errors,
        string? instance,
        JsonSerializerOptions? jsonOptions,
        ILogger? logger,
        RestLibOptions? options)
    {
        var problem = ProblemDetailsFactory.InvalidSort(errors, instance);
        return Create(problem, jsonOptions, logger, options);
    }

    /// <summary>
    /// Creates an option-aware 400 Invalid Fields result for RestLib endpoint handlers.
    /// </summary>
    internal static IResult InvalidFields(
        IReadOnlyList<FieldSelectionValidationError> errors,
        string? instance,
        JsonSerializerOptions? jsonOptions,
        ILogger? logger,
        RestLibOptions? options)
    {
        var problem = ProblemDetailsFactory.InvalidFields(errors, instance);
        return Create(problem, jsonOptions, logger, options);
    }

    /// <summary>
    /// Creates an option-aware 400 Invalid Search result for RestLib endpoint handlers.
    /// </summary>
    internal static IResult InvalidSearch(
        IReadOnlyList<SearchValidationError> errors,
        string? instance,
        JsonSerializerOptions? jsonOptions,
        ILogger? logger,
        RestLibOptions? options)
    {
        var problem = ProblemDetailsFactory.InvalidSearch(errors, instance);
        return Create(problem, jsonOptions, logger, options);
    }

    /// <summary>
    /// Creates an option-aware 400 Invalid Batch Request result for RestLib endpoint handlers.
    /// </summary>
    internal static IResult InvalidBatchRequest(
        string detail,
        IReadOnlyDictionary<string, string[]>? errors,
        string? instance,
        JsonSerializerOptions? jsonOptions,
        ILogger? logger,
        RestLibOptions? options)
    {
        var problem = ProblemDetailsFactory.InvalidBatchRequest(detail, errors, instance);
        return Create(problem, jsonOptions, logger, options);
    }

    /// <summary>
    /// Creates an option-aware 400 Invalid Batch Request result without field errors for RestLib endpoint handlers.
    /// </summary>
    internal static IResult InvalidBatchRequest(
        string detail,
        string? instance,
        JsonSerializerOptions? jsonOptions,
        ILogger? logger,
        RestLibOptions? options)
    {
        return InvalidBatchRequest(detail, errors: null, instance, jsonOptions, logger, options);
    }

    /// <summary>
    /// Creates an option-aware 400 Batch Size Exceeded result for RestLib endpoint handlers.
    /// </summary>
    internal static IResult BatchSizeExceeded(
        int itemCount,
        int maxBatchSize,
        string? instance,
        JsonSerializerOptions? jsonOptions,
        ILogger? logger,
        RestLibOptions? options)
    {
        var problem = ProblemDetailsFactory.BatchSizeExceeded(itemCount, maxBatchSize, instance);
        return Create(problem, jsonOptions, logger, options);
    }

    /// <summary>
    /// Creates an option-aware 400 Batch Action Not Enabled result for RestLib endpoint handlers.
    /// </summary>
    internal static IResult BatchActionNotEnabled(
        string action,
        IEnumerable<string> enabledActions,
        string? instance,
        JsonSerializerOptions? jsonOptions,
        ILogger? logger,
        RestLibOptions? options)
    {
        var problem = ProblemDetailsFactory.BatchActionNotEnabled(action, enabledActions, instance);
        return Create(problem, jsonOptions, logger, options);
    }

    /// <summary>
    /// Creates an option-aware 409 Conflict result for RestLib endpoint handlers.
    /// </summary>
    internal static IResult Conflict(
        string detail,
        string? instance,
        JsonSerializerOptions? jsonOptions,
        ILogger? logger,
        RestLibOptions? options)
    {
        var problem = ProblemDetailsFactory.Conflict(detail, instance);
        return Create(problem, jsonOptions, logger, options);
    }

    /// <summary>
    /// Creates an option-aware 409 Insufficient Stock result for RestLib endpoint handlers.
    /// </summary>
    internal static IResult InsufficientStock(
        string detail,
        string productId,
        int requested,
        int available,
        string? instance,
        JsonSerializerOptions? jsonOptions,
        ILogger? logger,
        RestLibOptions? options)
    {
        var problem = ProblemDetailsFactory.InsufficientStock(detail, productId, requested, available, instance);
        return Create(problem, jsonOptions, logger, options);
    }

    /// <summary>
    /// Creates an option-aware 409 Invalid Status Transition result for RestLib endpoint handlers.
    /// </summary>
    internal static IResult InvalidStatusTransition(
        string fromStatus,
        string toStatus,
        string? instance,
        JsonSerializerOptions? jsonOptions,
        ILogger? logger,
        RestLibOptions? options)
    {
        var problem = ProblemDetailsFactory.InvalidStatusTransition(fromStatus, toStatus, instance);
        return Create(problem, jsonOptions, logger, options);
    }

    /// <summary>
    /// Creates an option-aware 412 Precondition Failed result for RestLib endpoint handlers.
    /// </summary>
    internal static IResult PreconditionFailed(
        string detail,
        string? instance,
        JsonSerializerOptions? jsonOptions,
        ILogger? logger,
        RestLibOptions? options)
    {
        var problem = ProblemDetailsFactory.PreconditionFailed(detail, instance);
        return Create(problem, jsonOptions, logger, options);
    }

    /// <summary>
    /// Creates an option-aware 500 Internal Server Error result for RestLib endpoint handlers.
    /// </summary>
    internal static IResult InternalError(
        string? detail,
        string? instance,
        JsonSerializerOptions? jsonOptions,
        ILogger? logger,
        RestLibOptions? options)
    {
        var problem = ProblemDetailsFactory.InternalError(detail, instance);
        return Create(problem, jsonOptions, logger, options);
    }

    /// <summary>
    /// Creates an option-aware hook short-circuit result for RestLib endpoint handlers.
    /// </summary>
    internal static IResult HookShortCircuit(
        int statusCode,
        string? instance,
        JsonSerializerOptions? jsonOptions,
        ILogger? logger,
        RestLibOptions? options)
    {
        var problem = ProblemDetailsFactory.HookShortCircuit(statusCode, instance);
        return Create(problem, jsonOptions, logger, options);
    }

    /// <summary>
    /// Creates a 404 Not Found result using configured key-route metadata.
    /// </summary>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="entityName">The entity type name.</param>
    /// <param name="id">The entity identifier that was not found.</param>
    /// <param name="keyRouteParts">The configured key-route metadata.</param>
    /// <param name="instance">The request path.</param>
    /// <param name="jsonOptions">Optional JSON serializer options.</param>
    /// <param name="logger">Optional logger; when provided, the response is logged at the appropriate level.</param>
    /// <param name="options">Optional RestLib options that control problem type URI resolution and response shape.</param>
    internal static IResult NotFound<TKey>(
        string entityName,
        TKey id,
        IReadOnlyList<RestLibKeyRoutePart<TKey>> keyRouteParts,
        string? instance,
        JsonSerializerOptions? jsonOptions,
        ILogger? logger,
        RestLibOptions? options)
        where TKey : notnull
    {
        var problem = ProblemDetailsFactory.NotFound(entityName, id, keyRouteParts, instance);
        return Create(problem, jsonOptions, logger, options);
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
