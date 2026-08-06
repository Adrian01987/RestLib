using System.Text.Json;
using RestLib.Configuration;
using RestLib.Endpoints;
using RestLib.FieldSelection;
using RestLib.Filtering;
using RestLib.Search;
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
        var detail = id?.GetType().IsGenericType == true
            && id.GetType().GetGenericTypeDefinition() == typeof(RestLibCompositeKey<,>)
            ? $"{entityName} with key ({id}) does not exist."
            : $"{entityName} with ID '{id}' does not exist.";

        return ProblemCatalog.NotFound.Create(detail, instance);
    }

    /// <summary>
    /// Creates a 400 Validation Failed problem details response.
    /// </summary>
    /// <param name="errors">Dictionary of field names to error messages.</param>
    /// <param name="instance">The request path.</param>
    public static RestLibProblemDetails ValidationFailed(
        IReadOnlyDictionary<string, string[]> errors,
        string? instance = null)
    {
        return ProblemCatalog.ValidationFailed.Create(instance: instance, errors: errors);
    }

    /// <summary>
    /// Creates a 400 Bad Request problem details response.
    /// </summary>
    /// <param name="detail">Description of what went wrong.</param>
    /// <param name="instance">The request path.</param>
    public static RestLibProblemDetails BadRequest(string detail, string? instance = null)
    {
        return ProblemCatalog.BadRequest.Create(detail, instance);
    }

    /// <summary>
    /// Creates a 400 Invalid Cursor problem details response.
    /// </summary>
    /// <param name="cursor">The invalid cursor value.</param>
    /// <param name="instance">The request path.</param>
    /// <param name="detail">Optional detail message; when <c>null</c> a default message is used.</param>
    public static RestLibProblemDetails InvalidCursor(string cursor, string? instance = null, string? detail = null)
    {
        return ProblemCatalog.InvalidCursor.Create(detail, instance);
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
        return ProblemCatalog.InvalidLimit.Create(
            $"The limit value '{limit}' is invalid. Limit must be between {minLimit} and {maxLimit}.",
            instance);
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
        var errorDict = errors
            .GroupBy(e => e.ParameterName)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.Message).ToArray());

        var detail = errors.Count == 1
            ? $"The filter parameter '{errors[0].ParameterName}' has an invalid value."
            : "Multiple filter parameters have invalid values.";
        return ProblemCatalog.InvalidFilter.Create(detail, instance, errorDict);
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
        var errorDict = errors
            .GroupBy(e => e.Field)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.Message).ToArray());

        var detail = errors.Count == 1
            ? $"The sort field '{errors[0].Field}' is invalid."
            : "One or more sort fields are invalid.";
        return ProblemCatalog.InvalidSort.Create(detail, instance, errorDict);
    }

    /// <summary>
    /// Creates a 400 Invalid Fields problem details response.
    /// </summary>
    /// <param name="errors">The field selection validation errors.</param>
    /// <param name="instance">The request path.</param>
    public static RestLibProblemDetails InvalidFields(
        IReadOnlyList<FieldSelectionValidationError> errors,
        string? instance = null)
    {
        var errorDict = errors
            .GroupBy(e => e.Field)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.Message).ToArray());

        var detail = errors.Count == 1
            ? $"The field '{errors[0].Field}' is not a selectable field."
            : "One or more requested fields are not selectable.";
        return ProblemCatalog.InvalidFields.Create(detail, instance, errorDict);
    }

    /// <summary>
    /// Creates a 400 Invalid Search problem details response.
    /// </summary>
    /// <param name="errors">The search validation errors.</param>
    /// <param name="instance">The request path.</param>
    public static RestLibProblemDetails InvalidSearch(
        IReadOnlyList<SearchValidationError> errors,
        string? instance = null)
    {
        var errorDict = errors
            .GroupBy(e => e.ParameterName)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.Message).ToArray());

        var detail = errors.Count == 1
            ? $"The search parameter '{errors[0].ParameterName}' is invalid."
            : "One or more search parameters are invalid.";
        return ProblemCatalog.InvalidSearch.Create(detail, instance, errorDict);
    }

    /// <summary>
    /// Creates a 400 Invalid Batch Request problem details response.
    /// </summary>
    /// <param name="detail">Description of the batch validation error.</param>
    /// <param name="errors">Optional field-level errors.</param>
    /// <param name="instance">The request path.</param>
    public static RestLibProblemDetails InvalidBatchRequest(
        string detail,
        IReadOnlyDictionary<string, string[]>? errors = null,
        string? instance = null)
    {
        return ProblemCatalog.InvalidBatchRequest.Create(detail, instance, errors);
    }

    /// <summary>
    /// Creates a 400 Batch Size Exceeded problem details response.
    /// </summary>
    /// <param name="itemCount">The number of items in the request.</param>
    /// <param name="maxBatchSize">The maximum allowed batch size.</param>
    /// <param name="instance">The request path.</param>
    public static RestLibProblemDetails BatchSizeExceeded(
        int itemCount,
        int maxBatchSize,
        string? instance = null)
    {
        return ProblemCatalog.BatchSizeExceeded.Create(
            $"The batch contains {itemCount} items but the maximum allowed is {maxBatchSize}.",
            instance);
    }

    /// <summary>
    /// Creates a 400 Batch Action Not Enabled problem details response.
    /// </summary>
    /// <param name="action">The requested batch action.</param>
    /// <param name="enabledActions">The actions enabled for this resource.</param>
    /// <param name="instance">The request path.</param>
    public static RestLibProblemDetails BatchActionNotEnabled(
        string action,
        IEnumerable<string> enabledActions,
        string? instance = null)
    {
        var allowed = string.Join(", ", enabledActions);
        return ProblemCatalog.BatchActionNotEnabled.Create(
            $"The batch action '{action}' is not enabled for this resource. Enabled actions: {allowed}.",
            instance);
    }

    /// <summary>
    /// Creates a 409 Conflict problem details response.
    /// </summary>
    /// <param name="detail">Description of the conflict.</param>
    /// <param name="instance">The request path.</param>
    public static RestLibProblemDetails Conflict(string detail, string? instance = null)
    {
        return ProblemCatalog.Conflict.Create(detail, instance);
    }

    /// <summary>
    /// Creates a 409 Insufficient Stock problem details response.
    /// </summary>
    /// <param name="detail">Description of the stock conflict.</param>
    /// <param name="productId">The product identifier.</param>
    /// <param name="requested">The requested quantity.</param>
    /// <param name="available">The available quantity.</param>
    /// <param name="instance">The request path.</param>
    public static RestLibProblemDetails InsufficientStock(
        string detail,
        string productId,
        int requested,
        int available,
        string? instance = null)
    {
        return ProblemCatalog.InsufficientStock.Create(
            detail,
            instance,
            extensions: CreateExtensions(
                ("product_id", productId),
                ("requested", requested),
                ("available", available)));
    }

    /// <summary>
    /// Creates a 409 Invalid Status Transition problem details response.
    /// </summary>
    /// <param name="fromStatus">The current status.</param>
    /// <param name="toStatus">The requested target status.</param>
    /// <param name="instance">The request path.</param>
    public static RestLibProblemDetails InvalidStatusTransition(
        string fromStatus,
        string toStatus,
        string? instance = null)
    {
        return ProblemCatalog.InvalidStatusTransition.Create(
            $"Status cannot transition from '{fromStatus}' to '{toStatus}'.",
            instance,
            extensions: CreateExtensions(
                ("from", fromStatus),
                ("to", toStatus)));
    }

    /// <summary>
    /// Creates a 412 Precondition Failed problem details response.
    /// </summary>
    /// <param name="detail">Description of the precondition failure.</param>
    /// <param name="instance">The request path.</param>
    public static RestLibProblemDetails PreconditionFailed(string detail, string? instance = null)
    {
        return ProblemCatalog.PreconditionFailed.Create(detail, instance);
    }

    /// <summary>
    /// Creates a 501 Not Implemented problem details response for a repository that does not
    /// support atomic conditional writes.
    /// </summary>
    /// <param name="detail">Description of the unsupported conditional-write capability.</param>
    /// <param name="instance">The request path.</param>
    /// <returns>A configured Problem Details response.</returns>
    public static RestLibProblemDetails ConditionalWriteNotSupported(string detail, string? instance = null)
    {
        return ProblemCatalog.ConditionalWriteNotSupported.Create(detail, instance);
    }

    /// <summary>
    /// Creates a 500 Internal Server Error problem details response.
    /// </summary>
    /// <param name="detail">Optional detail (only include in development).</param>
    /// <param name="instance">The request path.</param>
    public static RestLibProblemDetails InternalError(string? detail = null, string? instance = null)
    {
        return ProblemCatalog.InternalError.Create(detail, instance);
    }

    /// <summary>
    /// Creates a problem details response for an operation short-circuited by a hook.
    /// The status code is determined by the hook's early result.
    /// </summary>
    /// <param name="statusCode">The HTTP status code from the hook's early result.</param>
    /// <param name="instance">The request path.</param>
    public static RestLibProblemDetails HookShortCircuit(int statusCode, string? instance = null)
    {
        return ProblemCatalog.HookShortCircuit.Create(instance: instance, status: statusCode);
    }

    /// <summary>
    /// Creates a 404 Not Found problem details response using configured key-route metadata.
    /// </summary>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="entityName">The name of the entity type.</param>
    /// <param name="id">The requested resource ID.</param>
    /// <param name="keyRouteParts">The configured key-route metadata.</param>
    /// <param name="instance">The request path.</param>
    internal static RestLibProblemDetails NotFound<TKey>(
        string entityName,
        TKey id,
        IReadOnlyList<RestLibKeyRoutePart<TKey>> keyRouteParts,
        string? instance = null)
        where TKey : notnull
    {
        var detail = keyRouteParts.Count > 1
            ? $"{entityName} with key ({EntityKeyHelper.FormatKeyForDisplay(id, keyRouteParts)}) does not exist."
            : $"{entityName} with ID '{id}' does not exist.";

        return ProblemCatalog.NotFound.Create(detail, instance);
    }

    private static IDictionary<string, JsonElement> CreateExtensions(
        params (string Key, object? Value)[] values)
    {
        var extensions = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            extensions[key] = JsonSerializer.SerializeToElement(value);
        }

        return extensions;
    }
}
