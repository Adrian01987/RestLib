using System.Text.Json;

namespace RestLib.Abstractions;

/// <summary>
/// Optional repository capability for mutations whose precondition must be evaluated
/// atomically against the current persisted entity.
/// </summary>
/// <remarks>
/// Implementations must prevent another mutation from changing the target between invoking
/// the supplied precondition and completing a successful write. A false precondition must not
/// mutate persistence. RestLib uses this capability to provide lost-update protection for
/// <c>If-Match</c> requests.
/// </remarks>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The entity key type.</typeparam>
public interface IConditionalWriteRepository<TEntity, TKey>
    where TEntity : class
    where TKey : notnull
{
    /// <summary>
    /// Atomically checks the current entity and fully replaces it when the precondition succeeds.
    /// </summary>
    /// <param name="id">The entity identifier.</param>
    /// <param name="entity">The replacement entity.</param>
    /// <param name="precondition">A synchronous predicate evaluated against the current persisted entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The atomic mutation outcome and persisted entity.</returns>
    Task<ConditionalWriteResult<TEntity>> UpdateConditionallyAsync(
        TKey id,
        TEntity entity,
        Func<TEntity, bool> precondition,
        CancellationToken ct = default);

    /// <summary>
    /// Atomically checks the current entity and applies an RFC 7396 merge patch when the precondition succeeds.
    /// </summary>
    /// <param name="id">The entity identifier.</param>
    /// <param name="patchDocument">The JSON merge-patch document.</param>
    /// <param name="precondition">A synchronous predicate evaluated against the current persisted entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The atomic mutation outcome and persisted entity.</returns>
    /// <exception cref="PatchValidationException">
    /// Thrown when the patch contains a client-correctable invalid or forbidden field.
    /// </exception>
    Task<ConditionalWriteResult<TEntity>> PatchConditionallyAsync(
        TKey id,
        JsonElement patchDocument,
        Func<TEntity, bool> precondition,
        CancellationToken ct = default);

    /// <summary>
    /// Atomically checks the current entity and deletes it when the precondition succeeds.
    /// </summary>
    /// <param name="id">The entity identifier.</param>
    /// <param name="precondition">A synchronous predicate evaluated against the current persisted entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The atomic mutation outcome and deleted entity.</returns>
    Task<ConditionalWriteResult<TEntity>> DeleteConditionallyAsync(
        TKey id,
        Func<TEntity, bool> precondition,
        CancellationToken ct = default);
}
