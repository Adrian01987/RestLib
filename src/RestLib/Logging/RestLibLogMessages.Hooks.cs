using Microsoft.Extensions.Logging;

namespace RestLib.Logging;

/// <summary>
/// Log message definitions for hook pipeline execution (EventId 1200–1249).
/// </summary>
internal static partial class RestLibLogMessages
{
    // ──────────────────────────────────────────────────────────────
    //  Hook stage execution (1200–1219)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Logs entry into a hook stage.
    /// </summary>
    [LoggerMessage(EventId = 1200, Level = LogLevel.Trace,
        Message = "Entering hook stage {StageName} (operation: {Operation})")]
    internal static partial void HookStageEntry(
        ILogger logger, string stageName, string operation);

    /// <summary>
    /// Logs exit from a hook stage.
    /// </summary>
    [LoggerMessage(EventId = 1201, Level = LogLevel.Trace,
        Message = "Exiting hook stage {StageName} (operation: {Operation}, should continue: {ShouldContinue})")]
    internal static partial void HookStageExit(
        ILogger logger, string stageName, string operation, bool shouldContinue);

    /// <summary>
    /// Logs that a hook stage short-circuited the pipeline.
    /// </summary>
    [LoggerMessage(EventId = 1202, Level = LogLevel.Debug,
        Message = "Hook stage {StageName} short-circuited pipeline (operation: {Operation})")]
    internal static partial void HookStageShortCircuit(
        ILogger logger, string stageName, string operation);

    // ──────────────────────────────────────────────────────────────
    //  Error hook (1220–1229)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Logs that an error hook threw an exception, which was swallowed to preserve the original exception.
    /// </summary>
    [LoggerMessage(EventId = 1220, Level = LogLevel.Error,
        Message = "Error hook threw an exception; swallowed to preserve original error (operation: {Operation})")]
    internal static partial void ErrorHookSwallowed(
        ILogger logger, string operation, Exception exception);
}
