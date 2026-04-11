using Microsoft.Extensions.Logging;

namespace RestLib.Logging;

/// <summary>
/// Log message definitions for infrastructure: parsers, cursor encoding,
/// problem details, options resolution (EventId 1300–1349).
/// </summary>
internal static partial class RestLibLogMessages
{
    // ──────────────────────────────────────────────────────────────
    //  ProblemDetailsResult (1300–1309)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Logs a 4xx client error ProblemDetails response.
    /// </summary>
    [LoggerMessage(EventId = 1300, Level = LogLevel.Information,
        Message = "ProblemDetails response (status: {StatusCode}, type: {ProblemType}, instance: {Instance})")]
    internal static partial void ProblemDetailsClientError(
        ILogger logger, int statusCode, string problemType, string? instance);

    /// <summary>
    /// Logs a 5xx server error ProblemDetails response.
    /// </summary>
    [LoggerMessage(EventId = 1301, Level = LogLevel.Error,
        Message = "ProblemDetails server error (status: {StatusCode}, type: {ProblemType}, instance: {Instance})")]
    internal static partial void ProblemDetailsServerError(
        ILogger logger, int statusCode, string problemType, string? instance);

    // ──────────────────────────────────────────────────────────────
    //  CursorEncoder (1310–1319)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Logs a cursor decode failure.
    /// </summary>
    [LoggerMessage(EventId = 1310, Level = LogLevel.Debug,
        Message = "Cursor decode failed (cursor length: {CursorLength})")]
    internal static partial void CursorDecodeFailed(
        ILogger logger, int cursorLength, Exception exception);

    /// <summary>
    /// Logs a cursor validation failure.
    /// </summary>
    [LoggerMessage(EventId = 1311, Level = LogLevel.Debug,
        Message = "Cursor validation failed (cursor length: {CursorLength})")]
    internal static partial void CursorValidationFailed(
        ILogger logger, int cursorLength, Exception exception);

    // ──────────────────────────────────────────────────────────────
    //  FilterParser (1320–1329)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Logs a type conversion failure during filter value parsing.
    /// </summary>
    [LoggerMessage(EventId = 1320, Level = LogLevel.Debug,
        Message = "Filter value type conversion failed (parameter: {ParameterName}, value: {RawValue}, target type: {TargetType})")]
    internal static partial void FilterTypeConversionFailed(
        ILogger logger, string parameterName, string rawValue, string targetType, Exception exception);

    // ──────────────────────────────────────────────────────────────
    //  JsonDeserializationHelper (1330–1339)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Logs a JSON deserialization failure.
    /// </summary>
    [LoggerMessage(EventId = 1330, Level = LogLevel.Debug,
        Message = "JSON deserialization failed")]
    internal static partial void JsonDeserializationFailed(
        ILogger logger, Exception exception);

    // ──────────────────────────────────────────────────────────────
    //  OptionsResolver (1340–1344)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Logs that options were not found in DI and defaults are being used.
    /// </summary>
    [LoggerMessage(EventId = 1340, Level = LogLevel.Warning,
        Message = "RestLib options not registered in DI; using defaults. Ensure AddRestLib() is called in service configuration")]
    internal static partial void OptionsNotRegistered(ILogger logger);

    // ──────────────────────────────────────────────────────────────
    //  PatchHelper (1345–1346)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Logs that patch preview deserialization returned null.
    /// </summary>
    [LoggerMessage(EventId = 1345, Level = LogLevel.Debug,
        Message = "Patch preview deserialization returned null")]
    internal static partial void PatchPreviewDeserializationNull(ILogger logger);

    // ──────────────────────────────────────────────────────────────
    //  ETagHelper (1347–1348)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Logs an ETag precondition failure (412).
    /// </summary>
    [LoggerMessage(EventId = 1347, Level = LogLevel.Debug,
        Message = "ETag precondition failed (entity: {EntityName}, id: {Id})")]
    internal static partial void ETagPreconditionFailed(
        ILogger logger, string entityName, string id);
}
