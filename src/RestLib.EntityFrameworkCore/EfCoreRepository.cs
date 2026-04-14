using System.Linq.Expressions;
using System.Reflection;
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
    private static readonly IReadOnlyDictionary<string, PropertyInfo> SnakeCasePropertyMap = BuildSnakeCasePropertyMap();
    private static readonly JsonSerializerOptions PatchJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

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
        if (!_options.UseAsNoTracking)
        {
            return _context.Set<TEntity>().FindAsync([id], ct).AsTask();
        }

        var predicate = BuildKeyEqualsPredicate(id);
        return _context.Set<TEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(predicate, ct);
    }

    /// <inheritdoc />
    public async Task<PagedResult<TEntity>> GetAllAsync(PaginationRequest pagination, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pagination);

        var keySelector = _options.KeySelector
            ?? throw new InvalidOperationException(
                $"No key selector is configured for entity type '{typeof(TEntity).Name}'.");

        var query = GetBaseQuery().OrderBy(keySelector);

        var startIndex = 0;
        if (!string.IsNullOrEmpty(pagination.Cursor) && CursorEncoder.TryDecode<int>(pagination.Cursor, out var cursorIndex))
        {
            startIndex = cursorIndex;
        }

        var takeCount = pagination.Limit == int.MaxValue ? int.MaxValue : pagination.Limit + 1;
        var pagedItems = await query
            .Skip(startIndex)
            .Take(takeCount)
            .ToListAsync(ct);

        var hasMore = pagedItems.Count > pagination.Limit;
        if (hasMore)
        {
            pagedItems = pagedItems.Take(pagination.Limit).ToList();
        }

        var nextCursor = hasMore && startIndex <= int.MaxValue - pagination.Limit
            ? CursorEncoder.Encode(startIndex + pagination.Limit)
            : null;

        return new PagedResult<TEntity>
        {
            Items = pagedItems,
            NextCursor = nextCursor
        };
    }

    /// <inheritdoc />
    public async Task<TEntity> CreateAsync(TEntity entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        try
        {
            await _context.Set<TEntity>().AddAsync(entity, ct);
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw;
        }
        catch (DbUpdateException ex)
        {
            throw ClassifyConstraintViolation(ex);
        }

        return entity;
    }

    /// <inheritdoc />
    public async Task<TEntity?> UpdateAsync(TKey id, TEntity entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var existing = await _context.Set<TEntity>().FindAsync([id], ct);
        if (existing is null)
        {
            return null;
        }

        try
        {
            CopyPrimaryKeyValues(existing, entity);
            _context.Entry(existing).CurrentValues.SetValues(entity);
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return null;
        }
        catch (DbUpdateException ex)
        {
            throw ClassifyConstraintViolation(ex);
        }

        return existing;
    }

    /// <inheritdoc />
    public async Task<TEntity?> PatchAsync(TKey id, JsonElement patchDocument, CancellationToken ct = default)
    {
        var existing = await _context.Set<TEntity>().FindAsync([id], ct);
        if (existing is null)
        {
            return null;
        }

        var entry = _context.Entry(existing);
        var primaryKey = GetPrimaryKey();
        var keyPropertyNames = primaryKey.Properties
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        try
        {
            foreach (var patchProperty in patchDocument.EnumerateObject())
            {
                if (!SnakeCasePropertyMap.TryGetValue(patchProperty.Name, out var propertyInfo) ||
                    keyPropertyNames.Contains(propertyInfo.Name))
                {
                    continue;
                }

                var value = JsonSerializer.Deserialize(
                    patchProperty.Value.GetRawText(),
                    propertyInfo.PropertyType,
                    PatchJsonOptions);

                entry.Property(propertyInfo.Name).CurrentValue = value;
            }

            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return null;
        }
        catch (DbUpdateException ex)
        {
            throw ClassifyConstraintViolation(ex);
        }

        return existing;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(TKey id, CancellationToken ct = default)
    {
        var existing = await _context.Set<TEntity>().FindAsync([id], ct);
        if (existing is null)
        {
            return false;
        }

        try
        {
            _context.Set<TEntity>().Remove(existing);
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
        catch (DbUpdateException ex)
        {
            throw ClassifyConstraintViolation(ex);
        }

        return true;
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
        ArgumentNullException.ThrowIfNull(filters);
        return GetBaseQuery().LongCountAsync(ct);
    }

    private static IReadOnlyDictionary<string, PropertyInfo> BuildSnakeCasePropertyMap()
    {
        return typeof(TEntity)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanWrite)
            .ToDictionary(
                property => JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name),
                property => property,
                StringComparer.OrdinalIgnoreCase);
    }

    private static EfCoreConstraintViolationException ClassifyConstraintViolation(DbUpdateException ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        var message = ex.InnerException?.Message;
        var constraintType = EfCoreConstraintType.Unknown;

        if (!string.IsNullOrEmpty(message))
        {
            if (message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("PRIMARY KEY", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("DUPLICATE", StringComparison.OrdinalIgnoreCase))
            {
                constraintType = EfCoreConstraintType.UniqueConstraint;
            }
            else if (message.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase))
            {
                constraintType = EfCoreConstraintType.ForeignKeyConstraint;
            }
        }

        return new EfCoreConstraintViolationException(
            message ?? "A database constraint violation occurred.",
            constraintType,
            ex);
    }

    private Expression<Func<TEntity, bool>> BuildKeyEqualsPredicate(TKey id)
    {
        var keySelector = _options.KeySelector
            ?? throw new InvalidOperationException(
                $"No key selector is configured for entity type '{typeof(TEntity).Name}'.");
        var constant = Expression.Constant(id, typeof(TKey));
        var equals = Expression.Equal(keySelector.Body, constant);

        return Expression.Lambda<Func<TEntity, bool>>(equals, keySelector.Parameters);
    }

    private IQueryable<TEntity> GetBaseQuery()
    {
        var query = _context.Set<TEntity>().AsQueryable();
        return _options.UseAsNoTracking ? query.AsNoTracking() : query;
    }

    private void CopyPrimaryKeyValues(TEntity source, TEntity target)
    {
        var primaryKey = GetPrimaryKey();

        foreach (var keyProperty in primaryKey.Properties)
        {
            if (keyProperty.PropertyInfo is null)
            {
                continue;
            }

            var keyValue = keyProperty.PropertyInfo.GetValue(source);
            keyProperty.PropertyInfo.SetValue(target, keyValue);
        }
    }

    private Microsoft.EntityFrameworkCore.Metadata.IKey GetPrimaryKey()
    {
        var entityType = _context.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException(
                $"Entity type '{typeof(TEntity).Name}' is not part of the EF Core model.");

        return entityType.FindPrimaryKey()
            ?? throw new InvalidOperationException(
                $"Entity type '{typeof(TEntity).Name}' has no primary key configured in the EF Core model.");
    }
}
