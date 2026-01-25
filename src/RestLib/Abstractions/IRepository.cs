using System.Text.Json;
using RestLib.Pagination;

namespace RestLib.Abstractions;

/// <summary>
/// Defines the contract for a persistence-agnostic repository.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
public interface IRepository<TEntity, TKey> where TEntity : class
{
    /// <summary>
    /// Gets an entity by its identifier.
    /// </summary>
    /// <param name="id">The entity identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The entity if found; otherwise, null.</returns>
    Task<TEntity?> GetByIdAsync(TKey id, CancellationToken ct = default);

    /// <summary>
    /// Gets all entities with pagination.
    /// </summary>
    /// <param name="pagination">The pagination request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged result containing the entities.</returns>
    Task<PagedResult<TEntity>> GetAllAsync(PaginationRequest pagination, CancellationToken ct = default);

    /// <summary>
    /// Creates a new entity.
    /// </summary>
    /// <param name="entity">The entity to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created entity.</returns>
    Task<TEntity> CreateAsync(TEntity entity, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing entity (full replacement).
    /// </summary>
    /// <param name="id">The entity identifier.</param>
    /// <param name="entity">The updated entity data.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated entity if found; otherwise, null.</returns>
    Task<TEntity?> UpdateAsync(TKey id, TEntity entity, CancellationToken ct = default);

    /// <summary>
    /// Partially updates an existing entity (JSON Merge Patch - RFC 7396).
    /// </summary>
    /// <param name="id">The entity identifier.</param>
    /// <param name="patchDocument">The JSON document containing fields to update.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated entity if found; otherwise, null.</returns>
    Task<TEntity?> PatchAsync(TKey id, JsonElement patchDocument, CancellationToken ct = default);

    /// <summary>
    /// Deletes an entity by its identifier.
    /// </summary>
    /// <param name="id">The entity identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if deleted; false if not found.</returns>
    Task<bool> DeleteAsync(TKey id, CancellationToken ct = default);
}
