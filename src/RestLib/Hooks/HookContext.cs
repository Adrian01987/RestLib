using Microsoft.AspNetCore.Http;

namespace RestLib.Hooks;

/// <summary>
/// Provides context for hook execution during RestLib request processing.
/// </summary>
/// <typeparam name="TEntity">The entity type being processed.</typeparam>
/// <typeparam name="TKey">The key type of the entity.</typeparam>
/// <remarks>
/// The context provides access to the HTTP context, the current operation,
/// and allows hooks to inspect/modify the entity or short-circuit the pipeline.
/// </remarks>
public class HookContext<TEntity, TKey> where TEntity : class
{
  /// <summary>
  /// Gets the current HTTP context.
  /// </summary>
  public required HttpContext HttpContext { get; init; }

  /// <summary>
  /// Gets the CRUD operation being performed.
  /// </summary>
  public required RestLibOperation Operation { get; init; }

  /// <summary>
  /// Gets the resource ID, if applicable (null for Create and GetAll operations).
  /// </summary>
  public TKey? ResourceId { get; init; }

  /// <summary>
  /// Gets or sets the entity being processed.
  /// Can be modified by hooks to alter the entity before persistence.
  /// </summary>
  public TEntity? Entity { get; set; }

  /// <summary>
  /// Gets the original entity before any modifications.
  /// Available for Update and Patch operations after fetching from repository.
  /// </summary>
  public TEntity? OriginalEntity { get; private set; }

  /// <summary>
  /// Sets the original entity. Used internally by the pipeline when the original
  /// entity becomes available after the context has been created.
  /// </summary>
  /// <param name="originalEntity">The original entity before modifications.</param>
  internal void SetOriginalEntity(TEntity? originalEntity) => OriginalEntity = originalEntity;

  /// <summary>
  /// Gets or sets whether the pipeline should continue execution.
  /// Set to false to short-circuit the pipeline.
  /// </summary>
  /// <remarks>
  /// When set to false, subsequent hooks and the repository operation will be skipped.
  /// If <see cref="EarlyResult"/> is set, it will be returned as the response.
  /// </remarks>
  public bool ShouldContinue { get; set; } = true;

  /// <summary>
  /// Gets or sets the result to return if the pipeline is short-circuited.
  /// Only used when <see cref="ShouldContinue"/> is set to false.
  /// </summary>
  public IResult? EarlyResult { get; set; }

  /// <summary>
  /// Gets a dictionary for sharing data between hooks during a single request.
  /// </summary>
  public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>();

  /// <summary>
  /// Gets the service provider for resolving dependencies.
  /// </summary>
  public required IServiceProvider Services { get; init; }

  /// <summary>
  /// Gets the cancellation token for the current request.
  /// </summary>
  public required CancellationToken CancellationToken { get; init; }
}
