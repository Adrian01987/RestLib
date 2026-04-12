using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestLib.Abstractions;
using RestLib.Filtering;
using RestLib.Pagination;

namespace RestLib.EntityFrameworkCore;

/// <summary>
/// EF Core-backed repository skeleton for the specified DbContext and entity type.
/// </summary>
/// <typeparam name="TContext">The DbContext type.</typeparam>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The primary key type.</typeparam>
public class EfCoreRepository<TContext, TEntity, TKey>
    : IRepository<TEntity, TKey>,
      IBatchRepository<TEntity, TKey>,
      ICountableRepository<TEntity, TKey>
    where TContext : DbContext
    where TEntity : class
    where TKey : notnull
{
    private readonly TContext _context;
    private readonly EfCoreRepositoryOptions<TEntity, TKey> _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="EfCoreRepository{TContext, TEntity, TKey}"/> class.
    /// </summary>
    /// <param name="context">The EF Core DbContext used by the repository.</param>
    /// <param name="options">The repository options.</param>
    public EfCoreRepository(TContext context, EfCoreRepositoryOptions<TEntity, TKey> options)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public Task<TEntity?> GetByIdAsync(TKey id, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public Task<PagedResult<TEntity>> GetAllAsync(PaginationRequest pagination, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public Task<TEntity> CreateAsync(TEntity entity, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public Task<TEntity?> UpdateAsync(TKey id, TEntity entity, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public Task<TEntity?> PatchAsync(TKey id, JsonElement patchDocument, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(TKey id, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TEntity>> CreateManyAsync(
        IReadOnlyList<TEntity> entities,
        CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TEntity>> UpdateManyAsync(
        IReadOnlyList<TEntity> entities,
        CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TEntity>> PatchManyAsync(
        IReadOnlyList<(TKey Id, JsonElement PatchDocument)> patches,
        CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public Task<int> DeleteManyAsync(
        IReadOnlyList<TKey> keys,
        CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<TKey, TEntity>> GetByIdsAsync(
        IReadOnlyList<TKey> ids,
        CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public Task<long> CountAsync(IReadOnlyList<FilterValue> filters, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }
}
