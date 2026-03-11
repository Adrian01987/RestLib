namespace RestLib.Hooks;

/// <summary>
/// Resolves named hook handlers used by JSON-backed resource configuration.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
public interface IRestLibNamedHookResolver<TEntity, TKey>
    where TEntity : class
{
  /// <summary>
  /// Resolves a named standard hook.
  /// </summary>
  /// <param name="name">The configured hook name.</param>
  /// <returns>The hook delegate.</returns>
  RestLibHookDelegate<TEntity, TKey> Resolve(string name);

  /// <summary>
  /// Resolves a named error hook.
  /// </summary>
  /// <param name="name">The configured hook name.</param>
  /// <returns>The error hook delegate.</returns>
  RestLibErrorHookDelegate<TEntity, TKey> ResolveError(string name);
}

/// <summary>
/// Default in-memory implementation for resolving named hooks.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
public sealed class RestLibNamedHookResolver<TEntity, TKey> : IRestLibNamedHookResolver<TEntity, TKey>
    where TEntity : class
{
  private readonly Dictionary<string, RestLibHookDelegate<TEntity, TKey>> _hooks =
      new(StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<string, RestLibErrorHookDelegate<TEntity, TKey>> _errorHooks =
      new(StringComparer.OrdinalIgnoreCase);

  /// <summary>
  /// Registers a named standard hook.
  /// </summary>
  /// <param name="name">The unique hook name.</param>
  /// <param name="hook">The hook delegate.</param>
  /// <returns>The resolver for chaining.</returns>
  public RestLibNamedHookResolver<TEntity, TKey> Add(string name, RestLibHookDelegate<TEntity, TKey> hook)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ArgumentNullException.ThrowIfNull(hook);

    _hooks[name] = hook;
    return this;
  }

  /// <summary>
  /// Registers a named error hook.
  /// </summary>
  /// <param name="name">The unique hook name.</param>
  /// <param name="hook">The error hook delegate.</param>
  /// <returns>The resolver for chaining.</returns>
  public RestLibNamedHookResolver<TEntity, TKey> AddError(string name, RestLibErrorHookDelegate<TEntity, TKey> hook)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ArgumentNullException.ThrowIfNull(hook);

    _errorHooks[name] = hook;
    return this;
  }

  /// <inheritdoc />
  public RestLibHookDelegate<TEntity, TKey> Resolve(string name)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);

    if (_hooks.TryGetValue(name, out var hook))
    {
      return hook;
    }

    throw new InvalidOperationException(
        $"No RestLib hook named '{name}' has been registered for entity type '{typeof(TEntity).Name}'.");
  }

  /// <inheritdoc />
  public RestLibErrorHookDelegate<TEntity, TKey> ResolveError(string name)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);

    if (_errorHooks.TryGetValue(name, out var hook))
    {
      return hook;
    }

    throw new InvalidOperationException(
        $"No RestLib error hook named '{name}' has been registered for entity type '{typeof(TEntity).Name}'.");
  }
}
