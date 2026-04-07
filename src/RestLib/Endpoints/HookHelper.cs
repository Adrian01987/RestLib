using Microsoft.AspNetCore.Http;
using RestLib.Hooks;

namespace RestLib.Endpoints;

/// <summary>
/// Result of initializing a hook pipeline for an endpoint request.
/// </summary>
/// <typeparam name="TEntity">The entity type being processed.</typeparam>
/// <typeparam name="TKey">The key type of the entity.</typeparam>
/// <param name="Pipeline">The hook pipeline, or <c>null</c> if no hooks are configured.</param>
/// <param name="Context">The hook context, or <c>null</c> if no hooks are configured.</param>
/// <param name="EarlyResult">An early result if <c>OnRequestReceived</c> short-circuited; otherwise <c>null</c>.</param>
internal readonly record struct PipelineInitResult<TEntity, TKey>(
    HookPipeline<TEntity, TKey>? Pipeline,
    HookContext<TEntity, TKey>? Context,
    IResult? EarlyResult)
    where TEntity : class
    where TKey : notnull;

/// <summary>
/// Helper methods for hook pipeline initialization, stage execution, and error handling.
/// </summary>
internal static class HookHelper
{
    /// <summary>
    /// Creates a hook pipeline (if hooks are configured), builds a <see cref="HookContext{TEntity, TKey}"/>,
    /// and executes the <c>OnRequestReceived</c> stage. This consolidates the pipeline initialisation
    /// logic that is common across all endpoint handlers.
    /// </summary>
    /// <typeparam name="TEntity">The entity type being processed.</typeparam>
    /// <typeparam name="TKey">The key type of the entity.</typeparam>
    /// <param name="hooks">The hooks from the endpoint configuration, or <c>null</c>.</param>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="operation">The REST operation being performed.</param>
    /// <param name="resourceId">The optional resource identifier.</param>
    /// <param name="entity">The optional entity being processed.</param>
    /// <returns>
    /// A <see cref="PipelineInitResult{TEntity, TKey}"/> containing the pipeline, context, and
    /// an early result if the <c>OnRequestReceived</c> hook short-circuited.
    /// When no hooks are configured, both <c>Pipeline</c> and <c>Context</c> are <c>null</c>
    /// and <c>EarlyResult</c> is <c>null</c>.
    /// </returns>
    internal static async Task<PipelineInitResult<TEntity, TKey>> InitializePipelineAsync<TEntity, TKey>(
        RestLibHooks<TEntity, TKey>? hooks,
        HttpContext httpContext,
        RestLibOperation operation,
        TKey? resourceId = default,
        TEntity? entity = default)
        where TEntity : class
        where TKey : notnull
    {
        if (hooks is null)
        {
            return new PipelineInitResult<TEntity, TKey>(null, null, null);
        }

        var pipeline = new HookPipeline<TEntity, TKey>(hooks);
        var hookContext = pipeline.CreateContext(httpContext, operation, resourceId, entity);
        var earlyResult = await ExecuteHookAsync(pipeline.ExecuteOnRequestReceivedAsync, hookContext);

        return new PipelineInitResult<TEntity, TKey>(pipeline, hookContext, earlyResult);
    }

    /// <summary>
    /// Executes a hook stage and returns an early result if the pipeline was short-circuited.
    /// Returns <c>null</c> if the pipeline should continue normally.
    /// </summary>
    /// <typeparam name="TEntity">The entity type being processed.</typeparam>
    /// <typeparam name="TKey">The key type of the entity.</typeparam>
    /// <param name="hookExecutor">The hook executor delegate to run.</param>
    /// <param name="hookContext">The hook context for the current request.</param>
    /// <returns>An early result if the hook short-circuited; otherwise <c>null</c>.</returns>
    internal static async Task<IResult?> ExecuteHookAsync<TEntity, TKey>(
        Func<HookContext<TEntity, TKey>, Task<bool>> hookExecutor,
        HookContext<TEntity, TKey> hookContext)
        where TEntity : class
        where TKey : notnull
    {
        if (!await hookExecutor(hookContext))
        {
            return hookContext.EarlyResult ?? Results.StatusCode(StatusCodes.Status500InternalServerError);
        }

        return null;
    }

    /// <summary>
    /// Runs a hook stage if the pipeline and context are available.
    /// Returns <c>null</c> if the hook was skipped or the pipeline should continue;
    /// returns an <see cref="IResult"/> if the hook short-circuited processing.
    /// </summary>
    /// <typeparam name="TEntity">The entity type being processed.</typeparam>
    /// <typeparam name="TKey">The key type of the entity.</typeparam>
    /// <param name="pipeline">The hook pipeline, or null if no hooks are configured.</param>
    /// <param name="hookContext">The hook context, or null if the pipeline was not initialised.</param>
    /// <param name="stageSelector">A function that selects the hook stage to execute.</param>
    /// <returns>An early result if the hook short-circuited; otherwise <c>null</c>.</returns>
    internal static async Task<IResult?> RunHookStageAsync<TEntity, TKey>(
        HookPipeline<TEntity, TKey>? pipeline,
        HookContext<TEntity, TKey>? hookContext,
        Func<HookPipeline<TEntity, TKey>, Func<HookContext<TEntity, TKey>, Task<bool>>> stageSelector)
        where TEntity : class
        where TKey : notnull
    {
        if (pipeline is null || hookContext is null)
        {
            return null;
        }

        return await ExecuteHookAsync(stageSelector(pipeline), hookContext);
    }

    /// <summary>
    /// Executes the error hook pipeline and returns an error result if handled.
    /// Returns <c>null</c> if the error was not handled (caller should rethrow).
    /// </summary>
    /// <typeparam name="TEntity">The entity type being processed.</typeparam>
    /// <typeparam name="TKey">The key type of the entity.</typeparam>
    /// <param name="pipeline">The hook pipeline, or null if no hooks are configured.</param>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="operation">The operation that was being performed.</param>
    /// <param name="exception">The exception that occurred.</param>
    /// <param name="resourceId">The optional resource identifier.</param>
    /// <param name="entity">The optional entity being processed.</param>
    /// <returns>An error result if the hook handled the error; otherwise <c>null</c>.</returns>
    internal static async Task<IResult?> HandleErrorHookAsync<TEntity, TKey>(
        HookPipeline<TEntity, TKey>? pipeline,
        HttpContext httpContext,
        RestLibOperation operation,
        Exception exception,
        TKey? resourceId = default,
        TEntity? entity = default)
        where TEntity : class
        where TKey : notnull
    {
        if (pipeline is null) return null;

        var errorContext = pipeline.CreateErrorContext(httpContext, operation, exception, resourceId, entity);
        var (handled, errorResult) = await pipeline.ExecuteOnErrorAsync(errorContext);

        return handled && errorResult is not null ? errorResult : null;
    }
}
