using System.Text.Json;

namespace RestLib.Abstractions;

/// <summary>
/// Optional repository interface for batch-optimized operations.
/// When implemented alongside <see cref="IRepository{TEntity, TKey}"/>,
/// RestLib uses these methods for batch endpoints instead of looping
/// over single-entity methods.
/// Mutating operations are atomic with respect to repository persistence:
/// when one throws, none of the changes from that call may remain persisted.
/// RestLib reports the failure and does not retry through
/// <see cref="IRepository{TEntity, TKey}"/>.
/// </summary>
/// <typeparam name="TEntity">The entity type managed by this repository.</typeparam>
/// <typeparam name="TKey">The type of the entity's primary key.</typeparam>
public interface IBatchRepository<TEntity, TKey>
    where TEntity : class
    where TKey : notnull
{
    /// <summary>
    /// Creates multiple entities in a single operation.
    /// The returned list contains one entity per input in the same order.
    /// Duplicate keys, including collisions produced by key generation, must
    /// reject the entire operation without persisting any input entity.
    /// </summary>
    /// <param name="entities">The entities to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created entities with generated keys, in input order.</returns>
    Task<IReadOnlyList<TEntity>> CreateManyAsync(
        IReadOnlyList<TEntity> entities,
        CancellationToken ct = default);

    /// <summary>
    /// Updates (fully replaces) multiple entities in a single operation.
    /// Inputs whose keys do not exist are skipped. The returned list contains
    /// one entity for each matching input, preserving their relative input order.
    /// Repeated keys are applied in input order, with the last value persisted;
    /// every returned occurrence represents that final persisted value.
    /// </summary>
    /// <param name="entities">The entities to update, each with its key already set.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated entities; missing inputs are omitted.</returns>
    Task<IReadOnlyList<TEntity>> UpdateManyAsync(
        IReadOnlyList<TEntity> entities,
        CancellationToken ct = default);

    /// <summary>
    /// Patches (partially updates) multiple entities in a single operation.
    /// Resource-key fields are immutable and must not be modified by a patch.
    /// Inputs whose keys do not exist are skipped. The returned list contains
    /// one entity for each matching input, preserving their relative input order.
    /// Repeated keys are patched sequentially in input order, and every returned
    /// occurrence represents the final persisted value for that key.
    /// </summary>
    /// <param name="patches">A list of tuples, each containing the entity key and a JSON merge-patch document.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The patched entities; missing inputs are omitted.</returns>
    Task<IReadOnlyList<TEntity>> PatchManyAsync(
        IReadOnlyList<(TKey Id, JsonElement PatchDocument)> patches,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes multiple entities by their keys in a single operation.
    /// Missing keys are ignored. Repeated keys identify the same entity and do
    /// not increase the returned count.
    /// </summary>
    /// <param name="keys">The keys of the entities to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of distinct entities actually deleted.</returns>
    Task<int> DeleteManyAsync(
        IReadOnlyList<TKey> keys,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves multiple entities by their keys in a single operation.
    /// Used by batch pipelines to avoid N+1 <c>GetByIdAsync</c> calls
    /// when checking existence or fetching originals before persistence.
    /// </summary>
    /// <param name="ids">The keys of the entities to retrieve.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A dictionary mapping each found key to its entity.
    /// Keys that do not exist in the store are omitted from the result.
    /// Repeated input keys are represented once because the result is keyed.
    /// </returns>
    Task<IReadOnlyDictionary<TKey, TEntity>> GetByIdsAsync(
        IReadOnlyList<TKey> ids,
        CancellationToken ct = default);
}
