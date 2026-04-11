using Microsoft.Extensions.Logging;

namespace RestLib.Logging;

/// <summary>
/// Log message definitions for CRUD endpoint handlers (EventId 1000–1099).
/// </summary>
internal static partial class RestLibLogMessages
{
    // ──────────────────────────────────────────────────────────────
    //  GetAll (1000–1009)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Logs the entry of a GetAll request.
    /// </summary>
    [LoggerMessage(EventId = 1000, Level = LogLevel.Debug,
        Message = "GetAll request received (cursor length: {CursorLength}, limit: {Limit})")]
    internal static partial void GetAllRequestReceived(
        ILogger logger, int cursorLength, int? limit);

    /// <summary>
    /// Logs the result of a GetAll request.
    /// </summary>
    [LoggerMessage(EventId = 1001, Level = LogLevel.Debug,
        Message = "GetAll returning {ItemCount} items (has next page: {HasNextPage})")]
    internal static partial void GetAllResponse(
        ILogger logger, int itemCount, bool hasNextPage);

    // ──────────────────────────────────────────────────────────────
    //  GetById (1010–1019)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Logs the entry of a GetById request.
    /// </summary>
    [LoggerMessage(EventId = 1010, Level = LogLevel.Debug,
        Message = "GetById request received (entity: {EntityName}, id: {Id})")]
    internal static partial void GetByIdRequestReceived(
        ILogger logger, string entityName, string id);

    /// <summary>
    /// Logs a 304 Not Modified response due to ETag match.
    /// </summary>
    [LoggerMessage(EventId = 1011, Level = LogLevel.Debug,
        Message = "GetById returning 304 Not Modified (entity: {EntityName}, id: {Id})")]
    internal static partial void GetByIdNotModified(
        ILogger logger, string entityName, string id);

    // ──────────────────────────────────────────────────────────────
    //  Create (1020–1029)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Logs the entry of a Create request.
    /// </summary>
    [LoggerMessage(EventId = 1020, Level = LogLevel.Debug,
        Message = "Create request received")]
    internal static partial void CreateRequestReceived(ILogger logger);

    /// <summary>
    /// Logs a successful entity creation.
    /// </summary>
    [LoggerMessage(EventId = 1021, Level = LogLevel.Information,
        Message = "Entity created (id: {Id}, location: {Location})")]
    internal static partial void EntityCreated(
        ILogger logger, string id, string location);

    // ──────────────────────────────────────────────────────────────
    //  Update (1030–1039)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Logs the entry of an Update request.
    /// </summary>
    [LoggerMessage(EventId = 1030, Level = LogLevel.Debug,
        Message = "Update request received (entity: {EntityName}, id: {Id})")]
    internal static partial void UpdateRequestReceived(
        ILogger logger, string entityName, string id);

    // ──────────────────────────────────────────────────────────────
    //  Patch (1040–1049)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Logs the entry of a Patch request.
    /// </summary>
    [LoggerMessage(EventId = 1040, Level = LogLevel.Debug,
        Message = "Patch request received (entity: {EntityName}, id: {Id})")]
    internal static partial void PatchRequestReceived(
        ILogger logger, string entityName, string id);

    // ──────────────────────────────────────────────────────────────
    //  Delete (1050–1059)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Logs the entry of a Delete request.
    /// </summary>
    [LoggerMessage(EventId = 1050, Level = LogLevel.Debug,
        Message = "Delete request received (entity: {EntityName}, id: {Id})")]
    internal static partial void DeleteRequestReceived(
        ILogger logger, string entityName, string id);

    /// <summary>
    /// Logs a successful entity deletion.
    /// </summary>
    [LoggerMessage(EventId = 1051, Level = LogLevel.Information,
        Message = "Entity deleted (entity: {EntityName}, id: {Id})")]
    internal static partial void EntityDeleted(
        ILogger logger, string entityName, string id);

    // ──────────────────────────────────────────────────────────────
    //  Shared endpoint events (1090–1099)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Logs an unhandled exception in an endpoint handler catch block.
    /// </summary>
    [LoggerMessage(EventId = 1090, Level = LogLevel.Error,
        Message = "Unhandled exception in {Operation} handler")]
    internal static partial void EndpointUnhandledException(
        ILogger logger, string operation, Exception exception);
}
