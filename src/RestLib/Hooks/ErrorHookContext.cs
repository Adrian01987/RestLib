using Microsoft.AspNetCore.Http;

namespace RestLib.Hooks;

/// <summary>
/// Provides context for error hook execution when an exception occurs during RestLib request processing.
/// </summary>
/// <typeparam name="TEntity">The entity type being processed.</typeparam>
/// <typeparam name="TKey">The key type of the entity.</typeparam>
/// <remarks>
/// The error context extends the standard hook context with exception information
/// and allows hooks to handle errors and provide custom error responses.
/// </remarks>
public class ErrorHookContext<TEntity, TKey> where TEntity : class where TKey : notnull
{
    /// <summary>
    /// Gets the current HTTP context.
    /// </summary>
    public required HttpContext HttpContext { get; init; }

    /// <summary>
    /// Gets the CRUD operation being performed when the error occurred.
    /// </summary>
    public required RestLibOperation Operation { get; init; }

    /// <summary>
    /// Gets the resource ID, if applicable (null for Create and GetAll operations).
    /// </summary>
    public TKey? ResourceId { get; init; }

    /// <summary>
    /// Gets the entity being processed when the error occurred.
    /// May be null if the error occurred before the entity was available.
    /// </summary>
    public TEntity? Entity { get; init; }

    /// <summary>
    /// Gets the exception that was thrown.
    /// </summary>
    public required Exception Exception { get; init; }

    /// <summary>
    /// Gets or sets whether the exception has been handled.
    /// When true, the exception will not be re-thrown and <see cref="ErrorResult"/> will be returned.
    /// </summary>
    public bool Handled { get; set; }

    /// <summary>
    /// Gets or sets the result to return if the exception is handled.
    /// Only used when <see cref="Handled"/> is set to true.
    /// </summary>
    public IResult? ErrorResult { get; set; }

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
