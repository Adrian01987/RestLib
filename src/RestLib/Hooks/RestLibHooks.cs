namespace RestLib.Hooks;

/// <summary>
/// Delegate for standard hooks in the RestLib request processing pipeline.
/// </summary>
/// <typeparam name="TEntity">The entity type being processed.</typeparam>
/// <typeparam name="TKey">The key type of the entity.</typeparam>
/// <param name="context">The hook context containing request information.</param>
/// <returns>A task representing the asynchronous operation.</returns>
public delegate Task RestLibHookDelegate<TEntity, TKey>(HookContext<TEntity, TKey> context)
  where TEntity : class;

/// <summary>
/// Delegate for error hooks in the RestLib request processing pipeline.
/// </summary>
/// <typeparam name="TEntity">The entity type being processed.</typeparam>
/// <typeparam name="TKey">The key type of the entity.</typeparam>
/// <param name="context">The error hook context containing exception information.</param>
/// <returns>A task representing the asynchronous operation.</returns>
public delegate Task RestLibErrorHookDelegate<TEntity, TKey>(ErrorHookContext<TEntity, TKey> context)
  where TEntity : class;

/// <summary>
/// Defines the hooks available in the RestLib request processing pipeline.
/// </summary>
/// <typeparam name="TEntity">The entity type being processed.</typeparam>
/// <typeparam name="TKey">The key type of the entity.</typeparam>
/// <remarks>
/// <para>Hooks are executed in the following order:</para>
/// <list type="number">
///   <item><description><see cref="OnRequestReceived"/> - Called when a request is first received</description></item>
///   <item><description><see cref="OnRequestValidated"/> - Called after request validation succeeds</description></item>
///   <item><description><see cref="BeforePersist"/> - Called before the entity is persisted to the repository</description></item>
///   <item><description><see cref="AfterPersist"/> - Called after the entity is persisted to the repository</description></item>
///   <item><description><see cref="BeforeResponse"/> - Called before the response is sent to the client</description></item>
/// </list>
/// <para>If an exception occurs, <see cref="OnError"/> is called instead of continuing the normal pipeline.</para>
/// <para>All hooks are optional and support async execution.</para>
/// </remarks>
public class RestLibHooks<TEntity, TKey> where TEntity : class
{
  /// <summary>
  /// Called when a request is first received, before any processing.
  /// </summary>
  /// <remarks>
  /// Use this hook for:
  /// <list type="bullet">
  ///   <item><description>Logging request information</description></item>
  ///   <item><description>Early request validation</description></item>
  ///   <item><description>Rate limiting checks</description></item>
  ///   <item><description>Custom authentication/authorization</description></item>
  /// </list>
  /// The entity will not be available in this hook for most operations.
  /// </remarks>
  public RestLibHookDelegate<TEntity, TKey>? OnRequestReceived { get; set; }

  /// <summary>
  /// Called after the request has been validated (for POST/PUT/PATCH operations with a body).
  /// </summary>
  /// <remarks>
  /// Use this hook for:
  /// <list type="bullet">
  ///   <item><description>Additional entity validation</description></item>
  ///   <item><description>Entity transformation</description></item>
  ///   <item><description>Business rule validation</description></item>
  ///   <item><description>Cross-field validation</description></item>
  /// </list>
  /// The entity is available and can be modified before persistence.
  /// For GET/DELETE operations, this hook is called after OnRequestReceived.
  /// </remarks>
  public RestLibHookDelegate<TEntity, TKey>? OnRequestValidated { get; set; }

  /// <summary>
  /// Called before the entity is persisted to the repository.
  /// </summary>
  /// <remarks>
  /// Use this hook for:
  /// <list type="bullet">
  ///   <item><description>Setting audit fields (CreatedAt, UpdatedAt, etc.)</description></item>
  ///   <item><description>Generating computed values</description></item>
  ///   <item><description>Final entity modifications</description></item>
  ///   <item><description>Preparing related entities</description></item>
  /// </list>
  /// This hook is only called for Create, Update, Patch, and Delete operations.
  /// </remarks>
  public RestLibHookDelegate<TEntity, TKey>? BeforePersist { get; set; }

  /// <summary>
  /// Called after the entity has been persisted to the repository.
  /// </summary>
  /// <remarks>
  /// Use this hook for:
  /// <list type="bullet">
  ///   <item><description>Sending notifications</description></item>
  ///   <item><description>Publishing events</description></item>
  ///   <item><description>Updating caches</description></item>
  ///   <item><description>Triggering side effects</description></item>
  /// </list>
  /// This hook is only called for Create, Update, Patch, and Delete operations.
  /// </remarks>
  public RestLibHookDelegate<TEntity, TKey>? AfterPersist { get; set; }

  /// <summary>
  /// Called before the response is sent to the client.
  /// </summary>
  /// <remarks>
  /// Use this hook for:
  /// <list type="bullet">
  ///   <item><description>Response transformation</description></item>
  ///   <item><description>Adding custom headers</description></item>
  ///   <item><description>Logging response information</description></item>
  ///   <item><description>Final modifications before returning</description></item>
  /// </list>
  /// </remarks>
  public RestLibHookDelegate<TEntity, TKey>? BeforeResponse { get; set; }

  /// <summary>
  /// Called when an exception occurs during request processing.
  /// </summary>
  /// <remarks>
  /// Use this hook for:
  /// <list type="bullet">
  ///   <item><description>Custom error logging</description></item>
  ///   <item><description>Error transformation</description></item>
  ///   <item><description>Custom error responses</description></item>
  ///   <item><description>Alerting/monitoring</description></item>
  /// </list>
  /// Set <see cref="ErrorHookContext{TEntity, TKey}.Handled"/> to true to prevent the exception from being re-thrown.
  /// </remarks>
  public RestLibErrorHookDelegate<TEntity, TKey>? OnError { get; set; }

  /// <summary>
  /// Gets a value indicating whether any hooks have been configured.
  /// </summary>
  internal bool HasAnyHooks =>
    OnRequestReceived is not null ||
    OnRequestValidated is not null ||
    BeforePersist is not null ||
    AfterPersist is not null ||
    BeforeResponse is not null ||
    OnError is not null;
}
