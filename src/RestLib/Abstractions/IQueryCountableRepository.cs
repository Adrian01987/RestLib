using RestLib.Pagination;

namespace RestLib.Abstractions;

/// <summary>
/// Optional repository interface that provides a total entity count for the full collection query shape.
/// </summary>
/// <typeparam name="TEntity">The entity type managed by this repository.</typeparam>
/// <typeparam name="TKey">The type of the entity's primary key.</typeparam>
public interface IQueryCountableRepository<TEntity, TKey>
    where TEntity : class
    where TKey : notnull
{
    /// <summary>
    /// Returns the total number of entities matching the specified collection query.
    /// Implementations should ignore pagination cursor and page size when counting.
    /// </summary>
    /// <param name="query">The active collection query.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The total number of matching entities.</returns>
    Task<long> CountAsync(PaginationRequest query, CancellationToken ct = default);
}
