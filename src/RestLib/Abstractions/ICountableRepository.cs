using RestLib.Filtering;

namespace RestLib.Abstractions;

/// <summary>
/// Optional repository interface that provides a total entity count.
/// When implemented alongside <see cref="IRepository{TEntity, TKey}"/>,
/// RestLib includes a <c>total_count</c> field in collection responses.
/// </summary>
/// <typeparam name="TEntity">The entity type managed by this repository.</typeparam>
/// <typeparam name="TKey">The type of the entity's primary key.</typeparam>
public interface ICountableRepository<TEntity, TKey>
    where TEntity : class
    where TKey : notnull
{
    /// <summary>
    /// Returns the total number of entities matching the specified filters.
    /// </summary>
    /// <param name="filters">
    /// The active filters to apply before counting. Pass an empty list to count all entities.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The total number of matching entities.</returns>
    Task<long> CountAsync(IReadOnlyList<FilterValue> filters, CancellationToken ct = default);
}
