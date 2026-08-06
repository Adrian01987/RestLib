using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace RestLib.Responses;

/// <summary>
/// Defines the invariant metadata for one RestLib Problem Details type.
/// </summary>
/// <param name="Type">The relative problem type URI.</param>
/// <param name="Title">The human-readable problem title.</param>
/// <param name="Status">The default HTTP status code.</param>
/// <param name="DefaultDetail">The default occurrence detail, if one exists.</param>
internal readonly record struct ProblemDescriptor(
    string Type,
    string Title,
    int Status,
    string? DefaultDetail = null)
{
    /// <summary>
    /// Creates a Problem Details occurrence from this descriptor.
    /// </summary>
    /// <param name="detail">An occurrence-specific detail, or <c>null</c> to use the descriptor default.</param>
    /// <param name="instance">The request path.</param>
    /// <param name="errors">Optional validation errors.</param>
    /// <param name="extensions">Optional extension members.</param>
    /// <param name="status">An optional occurrence-specific status code.</param>
    /// <returns>A configured Problem Details occurrence.</returns>
    internal RestLibProblemDetails Create(
        string? detail = null,
        string? instance = null,
        IReadOnlyDictionary<string, string[]>? errors = null,
        IDictionary<string, JsonElement>? extensions = null,
        int? status = null)
    {
        return new RestLibProblemDetails
        {
            Type = ProblemTypes.Resolve(Type),
            Title = Title,
            Status = status ?? Status,
            Detail = detail ?? DefaultDetail,
            Instance = instance,
            Errors = errors,
            Extensions = extensions
        };
    }
}

/// <summary>
/// Owns the invariant metadata for RestLib's built-in Problem Details types.
/// </summary>
internal static class ProblemCatalog
{
    /// <summary>Gets the resource-not-found descriptor.</summary>
    internal static readonly ProblemDescriptor NotFound = new(
        ProblemTypes.NotFound,
        "Resource Not Found",
        StatusCodes.Status404NotFound);

    /// <summary>Gets the validation-failed descriptor.</summary>
    internal static readonly ProblemDescriptor ValidationFailed = new(
        ProblemTypes.ValidationFailed,
        "Validation Failed",
        StatusCodes.Status400BadRequest,
        "One or more validation errors occurred.");

    /// <summary>Gets the bad-request descriptor.</summary>
    internal static readonly ProblemDescriptor BadRequest = new(
        ProblemTypes.BadRequest,
        "Bad Request",
        StatusCodes.Status400BadRequest);

    /// <summary>Gets the invalid-cursor descriptor.</summary>
    internal static readonly ProblemDescriptor InvalidCursor = new(
        ProblemTypes.InvalidCursor,
        "Invalid Cursor",
        StatusCodes.Status400BadRequest,
        "The provided cursor is not a valid pagination cursor.");

    /// <summary>Gets the invalid-limit descriptor.</summary>
    internal static readonly ProblemDescriptor InvalidLimit = new(
        ProblemTypes.InvalidLimit,
        "Invalid Limit",
        StatusCodes.Status400BadRequest);

    /// <summary>Gets the invalid-filter descriptor.</summary>
    internal static readonly ProblemDescriptor InvalidFilter = new(
        ProblemTypes.InvalidFilter,
        "Invalid Filter Value",
        StatusCodes.Status400BadRequest);

    /// <summary>Gets the invalid-sort descriptor.</summary>
    internal static readonly ProblemDescriptor InvalidSort = new(
        ProblemTypes.InvalidSort,
        "Invalid Sort Parameter",
        StatusCodes.Status400BadRequest);

    /// <summary>Gets the invalid-field-selection descriptor.</summary>
    internal static readonly ProblemDescriptor InvalidFields = new(
        ProblemTypes.InvalidFields,
        "Invalid Field Selection",
        StatusCodes.Status400BadRequest);

    /// <summary>Gets the invalid-search descriptor.</summary>
    internal static readonly ProblemDescriptor InvalidSearch = new(
        ProblemTypes.InvalidSearch,
        "Invalid Search Parameter",
        StatusCodes.Status400BadRequest);

    /// <summary>Gets the invalid-batch-request descriptor.</summary>
    internal static readonly ProblemDescriptor InvalidBatchRequest = new(
        ProblemTypes.InvalidBatchRequest,
        "Invalid Batch Request",
        StatusCodes.Status400BadRequest);

    /// <summary>Gets the batch-size-exceeded descriptor.</summary>
    internal static readonly ProblemDescriptor BatchSizeExceeded = new(
        ProblemTypes.BatchSizeExceeded,
        "Batch Size Exceeded",
        StatusCodes.Status400BadRequest);

    /// <summary>Gets the batch-action-not-enabled descriptor.</summary>
    internal static readonly ProblemDescriptor BatchActionNotEnabled = new(
        ProblemTypes.BatchActionNotEnabled,
        "Batch Action Not Enabled",
        StatusCodes.Status400BadRequest);

    /// <summary>Gets the conflict descriptor.</summary>
    internal static readonly ProblemDescriptor Conflict = new(
        ProblemTypes.Conflict,
        "Conflict",
        StatusCodes.Status409Conflict);

    /// <summary>Gets the insufficient-stock descriptor.</summary>
    internal static readonly ProblemDescriptor InsufficientStock = new(
        ProblemTypes.InsufficientStock,
        "Insufficient Stock",
        StatusCodes.Status409Conflict);

    /// <summary>Gets the invalid-status-transition descriptor.</summary>
    internal static readonly ProblemDescriptor InvalidStatusTransition = new(
        ProblemTypes.InvalidStatusTransition,
        "Invalid Status Transition",
        StatusCodes.Status409Conflict);

    /// <summary>Gets the precondition-failed descriptor.</summary>
    internal static readonly ProblemDescriptor PreconditionFailed = new(
        ProblemTypes.PreconditionFailed,
        "Precondition Failed",
        StatusCodes.Status412PreconditionFailed);

    /// <summary>Gets the conditional-write-not-supported descriptor.</summary>
    internal static readonly ProblemDescriptor ConditionalWriteNotSupported = new(
        ProblemTypes.ConditionalWriteNotSupported,
        "Conditional Write Not Supported",
        StatusCodes.Status501NotImplemented);

    /// <summary>Gets the internal-error descriptor.</summary>
    internal static readonly ProblemDescriptor InternalError = new(
        ProblemTypes.InternalError,
        "Internal Server Error",
        StatusCodes.Status500InternalServerError,
        "An unexpected error occurred.");

    /// <summary>Gets the hook-short-circuit descriptor.</summary>
    internal static readonly ProblemDescriptor HookShortCircuit = new(
        ProblemTypes.HookShortCircuit,
        "Hook Short-Circuit",
        StatusCodes.Status500InternalServerError,
        "The operation was short-circuited by a hook.");
}
