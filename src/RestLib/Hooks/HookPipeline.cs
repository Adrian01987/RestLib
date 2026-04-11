using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using RestLib.Logging;

namespace RestLib.Hooks;

/// <summary>
/// Manages the execution of hooks in the RestLib request processing pipeline.
/// </summary>
/// <typeparam name="TEntity">The entity type being processed.</typeparam>
/// <typeparam name="TKey">The key type of the entity.</typeparam>
internal sealed class HookPipeline<TEntity, TKey> where TEntity : class where TKey : notnull
{
    private readonly RestLibHooks<TEntity, TKey> _hooks;
    private readonly IDictionary<string, object?> _sharedItems = new Dictionary<string, object?>();
    private readonly ILogger? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="HookPipeline{TEntity, TKey}"/> class
    /// with the specified hook definitions and optional logger.
    /// </summary>
    /// <param name="hooks">The hook definitions to execute during request processing.</param>
    /// <param name="logger">Optional logger for tracing hook stage entry/exit and short-circuits.</param>
    internal HookPipeline(RestLibHooks<TEntity, TKey> hooks, ILogger? logger = null)
    {
        _hooks = hooks;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new hook context for the current request.
    /// </summary>
    public HookContext<TEntity, TKey> CreateContext(
      HttpContext httpContext,
      RestLibOperation operation,
      TKey? resourceId = default,
      TEntity? entity = default,
      TEntity? originalEntity = default)
    {
        var context = new HookContext<TEntity, TKey>
        {
            HttpContext = httpContext,
            Operation = operation,
            ResourceId = resourceId,
            Entity = entity,
            Services = httpContext.RequestServices,
            CancellationToken = httpContext.RequestAborted,
            ShouldContinue = true,
            EarlyResult = null
        };
        context.SetOriginalEntity(originalEntity);
        return context;
    }

    /// <summary>
    /// Creates an error hook context for the current request.
    /// </summary>
    public ErrorHookContext<TEntity, TKey> CreateErrorContext(
      HttpContext httpContext,
      RestLibOperation operation,
      Exception exception,
      TKey? resourceId = default,
      TEntity? entity = default)
    {
        return new ErrorHookContext<TEntity, TKey>
        {
            HttpContext = httpContext,
            Operation = operation,
            ResourceId = resourceId,
            Entity = entity,
            Exception = exception,
            Services = httpContext.RequestServices,
            CancellationToken = httpContext.RequestAborted,
            Handled = false,
            ErrorResult = null
        };
    }

    /// <summary>
    /// Executes the OnRequestReceived hook if configured.
    /// </summary>
    public Task<bool> ExecuteOnRequestReceivedAsync(HookContext<TEntity, TKey> context) =>
      ExecuteStageAsync(_hooks.OnRequestReceived, context, "OnRequestReceived", isFirstStage: true);

    /// <summary>
    /// Executes the OnRequestValidated hook if configured.
    /// </summary>
    public Task<bool> ExecuteOnRequestValidatedAsync(HookContext<TEntity, TKey> context) =>
      ExecuteStageAsync(_hooks.OnRequestValidated, context, "OnRequestValidated");

    /// <summary>
    /// Executes the BeforePersist hook if configured.
    /// </summary>
    public Task<bool> ExecuteBeforePersistAsync(HookContext<TEntity, TKey> context) =>
      ExecuteStageAsync(_hooks.BeforePersist, context, "BeforePersist");

    /// <summary>
    /// Executes the AfterPersist hook if configured.
    /// </summary>
    public Task<bool> ExecuteAfterPersistAsync(HookContext<TEntity, TKey> context) =>
      ExecuteStageAsync(_hooks.AfterPersist, context, "AfterPersist");

    /// <summary>
    /// Executes the BeforeResponse hook if configured.
    /// </summary>
    public Task<bool> ExecuteBeforeResponseAsync(HookContext<TEntity, TKey> context) =>
      ExecuteStageAsync(_hooks.BeforeResponse, context, "BeforeResponse");

    /// <summary>
    /// Executes the OnError hook if configured.
    /// </summary>
    public async Task<(bool Handled, IResult? ErrorResult)> ExecuteOnErrorAsync(
      ErrorHookContext<TEntity, TKey> context)
    {
        if (_hooks.OnError is null) return (false, null);

        // Copy shared items to context
        foreach (var item in _sharedItems)
        {
            context.Items[item.Key] = item.Value;
        }

        await _hooks.OnError(context);

        return (context.Handled, context.ErrorResult);
    }

    /// <summary>
    /// Executes a single hook stage delegate, managing shared item propagation.
    /// </summary>
    /// <param name="hookDelegate">The hook delegate to invoke, or null if the stage is not configured.</param>
    /// <param name="context">The hook context for the current request.</param>
    /// <param name="stageName">The name of the hook stage (for logging).</param>
    /// <param name="isFirstStage">
    /// When true (OnRequestReceived), shared items overwrite context items unconditionally.
    /// When false (subsequent stages), shared items are only copied if the key is not already present.
    /// </param>
    /// <returns>True if the pipeline should continue; false if the hook short-circuited.</returns>
    private async Task<bool> ExecuteStageAsync(
        RestLibHookDelegate<TEntity, TKey>? hookDelegate,
        HookContext<TEntity, TKey> context,
        string stageName,
        bool isFirstStage = false)
    {
        if (hookDelegate is null)
        {
            return true;
        }

        var operationName = context.Operation.ToString();

        if (_logger is not null)
        {
            RestLibLogMessages.HookStageEntry(_logger, stageName, operationName);
        }

        // Copy shared items to context
        foreach (var item in _sharedItems)
        {
            if (isFirstStage || !context.Items.ContainsKey(item.Key))
            {
                context.Items[item.Key] = item.Value;
            }
        }

        await hookDelegate(context);

        // Copy context items back to shared items
        foreach (var item in context.Items)
        {
            _sharedItems[item.Key] = item.Value;
        }

        // If the pipeline continues, clear EarlyResult so a stale value from this
        // stage does not leak into subsequent stages.
        if (context.ShouldContinue)
        {
            context.EarlyResult = null;
        }

        if (_logger is not null)
        {
            RestLibLogMessages.HookStageExit(_logger, stageName, operationName, context.ShouldContinue);

            if (!context.ShouldContinue)
            {
                RestLibLogMessages.HookStageShortCircuit(_logger, stageName, operationName);
            }
        }

        return context.ShouldContinue;
    }
}
