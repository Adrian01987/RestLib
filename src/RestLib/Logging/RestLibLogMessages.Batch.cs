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
    /// Logs that bulk persistence failed and individual retry was skipped because the outcome is unknown.
    /// </summary>
    [LoggerMessage(EventId = 1110, Level = LogLevel.Warning,
        Message = "Bulk persistence failed; individual retry skipped because the persistence outcome is unknown (action: {Action}, item count: {ItemCount})")]
    internal static partial void BulkPersistenceFailed(
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

    /// <summary>
    /// Logs that a batch repository returned a result that could not be safely
    /// associated with the submitted items.
    /// </summary>
    [LoggerMessage(EventId = 1113, Level = LogLevel.Error,
        Message = "Batch repository result violated its ordering or cardinality contract; successes suppressed (action: {Action}, item count: {ItemCount})")]
    internal static partial void BatchRepositoryContractViolated(
        ILogger logger, string action, int itemCount, Exception exception);

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

    // ──────────────────────────────────────────────────────────────
    //  BatchPatchPipeline (1150–1159)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Logs that a batch patch item was not found during persistence.
    /// </summary>
    [LoggerMessage(EventId = 1150, Level = LogLevel.Debug,
        Message = "Batch patch item not found (item index: {ItemIndex}, entity: {EntityName}, id: {ResourceId})")]
    internal static partial void BatchPatchItemNotFound(
        ILogger logger, int itemIndex, string entityName, object resourceId);

    /// <summary>
    /// Logs that a batch patch item failed pre-persist validation.
    /// </summary>
    [LoggerMessage(EventId = 1151, Level = LogLevel.Debug,
        Message = "Batch patch item validation failed (item index: {ItemIndex})")]
    internal static partial void BatchPatchItemValidationFailed(
        ILogger logger, int itemIndex);

    /// <summary>
    /// Logs the count of entities patched in a batch patch operation via bulk persistence.
    /// </summary>
    [LoggerMessage(EventId = 1152, Level = LogLevel.Information,
        Message = "Batch patch completed (patched: {PatchedCount})")]
    internal static partial void BatchPatchCompleted(
        ILogger logger, int patchedCount);

    // ──────────────────────────────────────────────────────────────
    //  BatchDeletePipeline (1160–1169)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Logs that a batch delete item was not found during persistence.
    /// </summary>
    [LoggerMessage(EventId = 1160, Level = LogLevel.Debug,
        Message = "Batch delete item not found (item index: {ItemIndex}, entity: {EntityName}, id: {ResourceId})")]
    internal static partial void BatchDeleteItemNotFound(
        ILogger logger, int itemIndex, string entityName, object resourceId);

    /// <summary>
    /// Logs the count of entities deleted in a batch delete operation via bulk persistence.
    /// </summary>
    [LoggerMessage(EventId = 1161, Level = LogLevel.Information,
        Message = "Batch delete completed (deleted: {DeletedCount})")]
    internal static partial void BatchDeleteCompleted(
        ILogger logger, int deletedCount);
}
