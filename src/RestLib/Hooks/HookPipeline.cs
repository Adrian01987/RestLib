using Microsoft.AspNetCore.Http;

namespace RestLib.Hooks;

/// <summary>
/// Manages the execution of hooks in the RestLib request processing pipeline.
/// </summary>
/// <typeparam name="TEntity">The entity type being processed.</typeparam>
/// <typeparam name="TKey">The key type of the entity.</typeparam>
internal sealed class HookPipeline<TEntity, TKey> where TEntity : class
{
  private readonly RestLibHooks<TEntity, TKey> _hooks;
  private readonly IDictionary<string, object?> _sharedItems;

  public HookPipeline(RestLibHooks<TEntity, TKey> hooks)
  {
    _hooks = hooks;
    _sharedItems = new Dictionary<string, object?>();
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
    return new HookContext<TEntity, TKey>
    {
      HttpContext = httpContext,
      Operation = operation,
      ResourceId = resourceId,
      Entity = entity,
      OriginalEntity = originalEntity,
      Services = httpContext.RequestServices,
      CancellationToken = httpContext.RequestAborted,
      ShouldContinue = true,
      EarlyResult = null
    };
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
  public async Task<bool> ExecuteOnRequestReceivedAsync(HookContext<TEntity, TKey> context)
  {
    if (_hooks.OnRequestReceived is null) return true;

    // Copy shared items to context
    foreach (var item in _sharedItems)
    {
      context.Items[item.Key] = item.Value;
    }

    await _hooks.OnRequestReceived(context);

    // Copy context items back to shared items
    foreach (var item in context.Items)
    {
      _sharedItems[item.Key] = item.Value;
    }

    return context.ShouldContinue;
  }

  /// <summary>
  /// Executes the OnRequestValidated hook if configured.
  /// </summary>
  public async Task<bool> ExecuteOnRequestValidatedAsync(HookContext<TEntity, TKey> context)
  {
    if (_hooks.OnRequestValidated is null) return true;

    // Copy shared items to context
    foreach (var item in _sharedItems)
    {
      if (!context.Items.ContainsKey(item.Key))
        context.Items[item.Key] = item.Value;
    }

    await _hooks.OnRequestValidated(context);

    // Copy context items back to shared items
    foreach (var item in context.Items)
    {
      _sharedItems[item.Key] = item.Value;
    }

    return context.ShouldContinue;
  }

  /// <summary>
  /// Executes the BeforePersist hook if configured.
  /// </summary>
  public async Task<bool> ExecuteBeforePersistAsync(HookContext<TEntity, TKey> context)
  {
    if (_hooks.BeforePersist is null) return true;

    // Copy shared items to context
    foreach (var item in _sharedItems)
    {
      if (!context.Items.ContainsKey(item.Key))
        context.Items[item.Key] = item.Value;
    }

    await _hooks.BeforePersist(context);

    // Copy context items back to shared items
    foreach (var item in context.Items)
    {
      _sharedItems[item.Key] = item.Value;
    }

    return context.ShouldContinue;
  }

  /// <summary>
  /// Executes the AfterPersist hook if configured.
  /// </summary>
  public async Task<bool> ExecuteAfterPersistAsync(HookContext<TEntity, TKey> context)
  {
    if (_hooks.AfterPersist is null) return true;

    // Copy shared items to context
    foreach (var item in _sharedItems)
    {
      if (!context.Items.ContainsKey(item.Key))
        context.Items[item.Key] = item.Value;
    }

    await _hooks.AfterPersist(context);

    // Copy context items back to shared items
    foreach (var item in context.Items)
    {
      _sharedItems[item.Key] = item.Value;
    }

    return context.ShouldContinue;
  }

  /// <summary>
  /// Executes the BeforeResponse hook if configured.
  /// </summary>
  public async Task<bool> ExecuteBeforeResponseAsync(HookContext<TEntity, TKey> context)
  {
    if (_hooks.BeforeResponse is null) return true;

    // Copy shared items to context
    foreach (var item in _sharedItems)
    {
      if (!context.Items.ContainsKey(item.Key))
        context.Items[item.Key] = item.Value;
    }

    await _hooks.BeforeResponse(context);

    // Copy context items back to shared items
    foreach (var item in context.Items)
    {
      _sharedItems[item.Key] = item.Value;
    }

    return context.ShouldContinue;
  }

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
}
