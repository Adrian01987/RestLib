using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using RestLib.Abstractions;
using RestLib.Filtering;
using RestLib.Pagination;
using RestLib.Sorting;

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

        var query = GetBaseQuery();
        if (pagination.Filters.Count > 0)
        {
            query = ApplyComparisonFilters(query, pagination.Filters);
            query = ApplyStringFilters(query, pagination.Filters);
            query = ApplyInFilters(query, pagination.Filters);
        }

        var orderedQuery = SortBuilder.ApplySorting(query, pagination.SortFields, keySelector);

        var startIndex = 0;
        if (!string.IsNullOrEmpty(pagination.Cursor) && CursorEncoder.TryDecode<int>(pagination.Cursor, out var cursorIndex))
        {
            startIndex = cursorIndex;
        }

        var takeCount = pagination.Limit == int.MaxValue ? int.MaxValue : pagination.Limit + 1;
        var pagedItems = await orderedQuery
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
            ApplyPatch(entry, patchDocument, keyPropertyNames);

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
    public async Task<IReadOnlyList<TEntity>> CreateManyAsync(
        IReadOnlyList<TEntity> entities,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entities);

        if (entities.Count == 0)
        {
            return [];
        }

        try
        {
            await _context.Set<TEntity>().AddRangeAsync(entities, ct);
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

        return entities;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TEntity>> UpdateManyAsync(
        IReadOnlyList<TEntity> entities,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entities);

        if (entities.Count == 0)
        {
            return [];
        }

        var keySelector = _options.KeySelector
            ?? throw new InvalidOperationException(
                $"No key selector is configured for entity type '{typeof(TEntity).Name}'.");
        var getKey = keySelector.Compile();
        var keys = entities.Select(getKey).ToList();
        var containsMethod = typeof(Enumerable)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == nameof(Enumerable.Contains) && method.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(TKey));
        var containsCall = Expression.Call(
            containsMethod,
            Expression.Constant(keys),
            keySelector.Body);
        var predicate = Expression.Lambda<Func<TEntity, bool>>(containsCall, keySelector.Parameters);
        var existingEntities = await _context.Set<TEntity>()
            .Where(predicate)
            .ToListAsync(ct);
        var existingById = existingEntities.ToDictionary(getKey);
        var results = new List<TEntity>(existingEntities.Count);

        foreach (var entity in entities)
        {
            var key = getKey(entity);
            if (!existingById.TryGetValue(key, out var existing))
            {
                continue;
            }

            CopyPrimaryKeyValues(existing, entity);
            _context.Entry(existing).CurrentValues.SetValues(entity);
            results.Add(existing);
        }

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return results;
        }
        catch (DbUpdateException ex)
        {
            throw ClassifyConstraintViolation(ex);
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TEntity>> PatchManyAsync(
        IReadOnlyList<(TKey Id, JsonElement PatchDocument)> patches,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(patches);

        if (patches.Count == 0)
        {
            return [];
        }

        var ids = patches.Select(patch => patch.Id).ToList();
        var existingById = await FetchTrackedEntitiesByIdsAsync(ids, ct);
        var keyPropertyNames = GetPrimaryKey().Properties
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        var results = new List<TEntity>(existingById.Count);

        foreach (var (id, patchDocument) in patches)
        {
            if (!existingById.TryGetValue(id, out var existing))
            {
                continue;
            }

            ApplyPatch(_context.Entry(existing), patchDocument, keyPropertyNames);
            results.Add(existing);
        }

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return results;
        }
        catch (DbUpdateException ex)
        {
            throw ClassifyConstraintViolation(ex);
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<int> DeleteManyAsync(
        IReadOnlyList<TKey> keys,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keys);

        if (keys.Count == 0)
        {
            return 0;
        }

        var found = (await FetchTrackedEntitiesByIdsAsync(keys, ct)).Values.ToList();
        if (found.Count == 0)
        {
            return 0;
        }

        _context.Set<TEntity>().RemoveRange(found);

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return 0;
        }
        catch (DbUpdateException ex)
        {
            throw ClassifyConstraintViolation(ex);
        }

        return found.Count;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<TKey, TEntity>> GetByIdsAsync(
        IReadOnlyList<TKey> ids,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return new Dictionary<TKey, TEntity>();
        }

        var keySelector = _options.KeySelector
            ?? throw new InvalidOperationException(
                $"No key selector is configured for entity type '{typeof(TEntity).Name}'.");
        var idList = ids.ToList();
        var containsMethod = typeof(Enumerable)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == nameof(Enumerable.Contains) && method.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(TKey));
        var containsCall = Expression.Call(
            containsMethod,
            Expression.Constant(idList),
            keySelector.Body);
        var predicate = Expression.Lambda<Func<TEntity, bool>>(containsCall, keySelector.Parameters);
        var getKey = keySelector.Compile();

        var entities = await GetBaseQuery()
            .Where(predicate)
            .ToListAsync(ct);

        return entities.ToDictionary(getKey);
    }

    /// <inheritdoc />
    public Task<long> CountAsync(IReadOnlyList<FilterValue> filters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filters);

        var query = GetBaseQuery();
        if (filters.Count > 0)
        {
            query = ApplyComparisonFilters(query, filters);
            query = ApplyStringFilters(query, filters);
            query = ApplyInFilters(query, filters);
        }

        return query.LongCountAsync(ct);
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

    private static IQueryable<TEntity> ApplyComparisonFilters(
        IQueryable<TEntity> query,
        IReadOnlyList<FilterValue> filters)
    {
        foreach (var filter in filters)
        {
            if (!IsComparisonOperator(filter.Operator))
            {
                continue;
            }

            var predicate = ComparisonFilterBuilder.BuildPredicate<TEntity>(filter);
            query = query.Where(predicate);
        }

        return query;
    }

    private static bool IsComparisonOperator(FilterOperator op)
    {
        return op is FilterOperator.Eq
            or FilterOperator.Neq
            or FilterOperator.Gt
            or FilterOperator.Lt
            or FilterOperator.Gte
            or FilterOperator.Lte;
    }

    private static IQueryable<TEntity> ApplyStringFilters(
        IQueryable<TEntity> query,
        IReadOnlyList<FilterValue> filters)
    {
        foreach (var filter in filters)
        {
            if (!IsStringOperator(filter.Operator))
            {
                continue;
            }

            var predicate = StringFilterBuilder.BuildPredicate<TEntity>(filter);
            query = query.Where(predicate);
        }

        return query;
    }

    private static bool IsStringOperator(FilterOperator op)
    {
        return op is FilterOperator.Contains
            or FilterOperator.StartsWith
            or FilterOperator.EndsWith;
    }

    private static IQueryable<TEntity> ApplyInFilters(
        IQueryable<TEntity> query,
        IReadOnlyList<FilterValue> filters)
    {
        foreach (var filter in filters)
        {
            if (!IsInOperator(filter.Operator))
            {
                continue;
            }

            var predicate = InFilterBuilder.BuildPredicate<TEntity>(filter);
            query = query.Where(predicate);
        }

        return query;
    }

    private static bool IsInOperator(FilterOperator op)
    {
        return op is FilterOperator.In;
    }

    private static void ApplyPatch(
        EntityEntry<TEntity> entry,
        JsonElement patchDocument,
        IReadOnlySet<string> keyPropertyNames)
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

    private async Task<Dictionary<TKey, TEntity>> FetchTrackedEntitiesByIdsAsync(
        IReadOnlyList<TKey> ids,
        CancellationToken ct)
    {
        var keySelector = _options.KeySelector
            ?? throw new InvalidOperationException(
                $"No key selector is configured for entity type '{typeof(TEntity).Name}'.");
        var getKey = keySelector.Compile();
        var idList = ids.ToList();
        var containsMethod = typeof(Enumerable)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == nameof(Enumerable.Contains) && method.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(TKey));
        var containsCall = Expression.Call(
            containsMethod,
            Expression.Constant(idList),
            keySelector.Body);
        var predicate = Expression.Lambda<Func<TEntity, bool>>(containsCall, keySelector.Parameters);
        var existingEntities = await _context.Set<TEntity>()
            .Where(predicate)
            .ToListAsync(ct);

        return existingEntities.ToDictionary(getKey);
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
