using Microsoft.Extensions.Logging;

namespace RestLib.Logging;

/// <summary>
/// Log message definitions for batch pipeline processing (EventId 1100–1199).
/// </summary>
internal static partial class RestLibLogMessages
{
    // ──────────────────────────────────────────────────────────────
    //  BatchHandler (1100–1109)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Logs the entry of a batch request.
    /// </summary>
    [LoggerMessage(EventId = 1100, Level = LogLevel.Debug,
        Message = "Batch request received (action: {Action}, item count: {ItemCount})")]
    internal static partial void BatchRequestReceived(
        ILogger logger, string action, int itemCount);

    /// <summary>
    /// Logs a JSON deserialization failure when parsing the batch envelope.
    /// </summary>
    [LoggerMessage(EventId = 1101, Level = LogLevel.Warning,
        Message = "Batch envelope deserialization failed")]
    internal static partial void BatchEnvelopeDeserializationFailed(
        ILogger logger, Exception exception);

    /// <summary>
    /// Logs the completion of a batch request.
    /// </summary>
    [LoggerMessage(EventId = 1102, Level = LogLevel.Information,
        Message = "Batch completed (action: {Action}, total: {Total}, succeeded: {Succeeded}, failed: {Failed}, status: {StatusCode})")]
    internal static partial void BatchCompleted(
        ILogger logger, string action, int total, int succeeded, int failed, int statusCode);

    // ──────────────────────────────────────────────────────────────
    //  BatchActionPipeline (1110–1129)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Logs that bulk persistence failed and the pipeline is falling back to individual persistence.
    /// </summary>
    [LoggerMessage(EventId = 1110, Level = LogLevel.Warning,
        Message = "Bulk persistence failed, falling back to individual persistence (action: {Action}, item count: {ItemCount})")]
    internal static partial void BulkPersistenceFallback(
        ILogger logger, string action, int itemCount, Exception exception);

    /// <summary>
    /// Logs that an error hook threw an exception during batch processing, which was swallowed.
    /// </summary>
    [LoggerMessage(EventId = 1111, Level = LogLevel.Error,
        Message = "Error hook threw during batch processing; exception swallowed to preserve original error (action: {Action}, item index: {ItemIndex})")]
    internal static partial void BatchErrorHookSwallowed(
        ILogger logger, string action, int itemIndex, Exception exception);

    /// <summary>
    /// Logs a per-item persistence error in the batch pipeline.
    /// </summary>
    [LoggerMessage(EventId = 1112, Level = LogLevel.Debug,
        Message = "Batch item persistence failed (action: {Action}, item index: {ItemIndex})")]
    internal static partial void BatchItemPersistenceFailed(
        ILogger logger, string action, int itemIndex, Exception exception);

    // ──────────────────────────────────────────────────────────────
    //  BatchCreatePipeline (1130–1139)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Logs the count of entities created in a batch create operation.
    /// </summary>
    [LoggerMessage(EventId = 1130, Level = LogLevel.Information,
        Message = "Batch create completed (created: {CreatedCount})")]
    internal static partial void BatchCreateCompleted(
        ILogger logger, int createdCount);

    // ──────────────────────────────────────────────────────────────
    //  BatchUpdatePipeline (1140–1149)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Logs a JSON deserialization failure for an individual item body in a batch update.
    /// </summary>
    [LoggerMessage(EventId = 1140, Level = LogLevel.Warning,
        Message = "Batch update item deserialization failed (item index: {ItemIndex})")]
    internal static partial void BatchUpdateItemDeserializationFailed(
        ILogger logger, int itemIndex, Exception exception);
}
