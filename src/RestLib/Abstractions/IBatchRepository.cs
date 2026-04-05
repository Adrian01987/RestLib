using System.Text.Json;

namespace RestLib.Abstractions;

/// <summary>
/// Optional repository interface for batch-optimized operations.
/// When implemented alongside <see cref="IRepository{TEntity, TKey}"/>,
/// RestLib uses these methods for batch endpoints instead of looping
/// over single-entity methods.
/// </summary>
/// <typeparam name="TEntity">The entity type managed by this repository.</typeparam>
/// <typeparam name="TKey">The type of the entity's primary key.</typeparam>
public interface IBatchRepository<TEntity, TKey>
    where TEntity : class
    where TKey : notnull
{
    /// <summary>
    /// Creates multiple entities in a single operation.
    /// </summary>
    /// <param name="entities">The entities to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created entities with generated keys.</returns>
    Task<IReadOnlyList<TEntity>> CreateManyAsync(
        IReadOnlyList<TEntity> entities,
        CancellationToken ct = default);

    /// <summary>
    /// Updates (fully replaces) multiple entities in a single operation.
    /// </summary>
    /// <param name="entities">The entities to update, each with its key already set.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated entities.</returns>
    Task<IReadOnlyList<TEntity>> UpdateManyAsync(
        IReadOnlyList<TEntity> entities,
        CancellationToken ct = default);

    /// <summary>
    /// Patches (partially updates) multiple entities in a single operation.
    /// </summary>
    /// <param name="patches">A list of tuples, each containing the entity key and a JSON merge-patch document.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The patched entities.</returns>
    Task<IReadOnlyList<TEntity>> PatchManyAsync(
        IReadOnlyList<(TKey Id, JsonElement PatchDocument)> patches,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes multiple entities by their keys in a single operation.
    /// </summary>
    /// <param name="keys">The keys of the entities to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of entities actually deleted.</returns>
    Task<int> DeleteManyAsync(
        IReadOnlyList<TKey> keys,
        CancellationToken ct = default);
}
