using RestLib.FieldSelection;
using RestLib.Filtering;
using RestLib.Pagination;
using RestLib.Sorting;

namespace RestLib.Abstractions;

/// <summary>
/// Optional repository capability for pushing field-selection projection into the data source.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
public interface IFieldSelectionProjectionRepository<TEntity, TKey>
    where TEntity : class
    where TKey : notnull
{
    /// <summary>
    /// Gets projected entities with pagination.
    /// </summary>
    /// <param name="pagination">The pagination request.</param>
    /// <param name="selectedFields">The requested selected fields.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The paged projected result, or <c>null</c> when projection pushdown is not applicable.</returns>
    Task<PagedResult<TEntity>?> GetAllProjectedAsync(
        PaginationRequest pagination,
        IReadOnlyList<SelectedField> selectedFields,
        CancellationToken ct = default);

    /// <summary>
    /// Gets a projected entity by ID.
    /// </summary>
    /// <param name="id">The entity ID.</param>
    /// <param name="selectedFields">The requested selected fields.</param>
    /// <param name="filters">Optional active filters for determining required projected properties.</param>
    /// <param name="sortFields">Optional active sort fields for determining required projected properties.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The projected entity, or <c>null</c> if not found or projection pushdown is not applicable.</returns>
    Task<TEntity?> GetByIdProjectedAsync(
        TKey id,
        IReadOnlyList<SelectedField> selectedFields,
        IReadOnlyList<FilterValue>? filters = null,
        IReadOnlyList<SortField>? sortFields = null,
        CancellationToken ct = default);
}
