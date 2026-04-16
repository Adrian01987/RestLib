using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
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
    private const int KeysetCursorVersion = 1;
    private static readonly MethodInfo OrderByMethod = typeof(Queryable)
        .GetMethods()
        .Single(method =>
            method.Name == nameof(Queryable.OrderBy) &&
            method.IsGenericMethodDefinition &&
            method.GetGenericArguments().Length == 2 &&
            method.GetParameters().Length == 2);
    private static readonly MethodInfo OrderByDescendingMethod = typeof(Queryable)
        .GetMethods()
        .Single(method =>
            method.Name == nameof(Queryable.OrderByDescending) &&
            method.IsGenericMethodDefinition &&
            method.GetGenericArguments().Length == 2 &&
            method.GetParameters().Length == 2);
    private static readonly MethodInfo ThenByMethod = typeof(Queryable)
        .GetMethods()
        .Single(method =>
            method.Name == nameof(Queryable.ThenBy) &&
            method.IsGenericMethodDefinition &&
            method.GetGenericArguments().Length == 2 &&
            method.GetParameters().Length == 2);
    private static readonly MethodInfo ThenByDescendingMethod = typeof(Queryable)
        .GetMethods()
        .Single(method =>
            method.Name == nameof(Queryable.ThenByDescending) &&
            method.IsGenericMethodDefinition &&
            method.GetGenericArguments().Length == 2 &&
            method.GetParameters().Length == 2);
    private static readonly IReadOnlyDictionary<string, PropertyInfo> SnakeCasePropertyMap = BuildSnakeCasePropertyMap();
    private static readonly JsonSerializerOptions PatchJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly TContext _context;
    private readonly EfCoreRepositoryOptions<TEntity, TKey> _options;
    private readonly Expression<Func<TEntity, TKey>> _keySelector;

    /// <summary>
    /// Initializes a new instance of the <see cref="EfCoreRepository{TContext, TEntity, TKey}"/> class.
    /// </summary>
    /// <param name="context">The EF Core DbContext used by the repository.</param>
    /// <param name="options">The repository options.</param>
    public EfCoreRepository(TContext context, EfCoreRepositoryOptions<TEntity, TKey> options)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _keySelector = _options.KeySelector ?? ResolveKeySelector();
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

        var query = GetBaseQuery();
        if (pagination.Filters.Count > 0)
        {
            query = ApplyComparisonFilters(query, pagination.Filters);
            query = ApplyStringFilters(query, pagination.Filters);
            query = ApplyInFilters(query, pagination.Filters);
        }

        var effectiveSortFields = GetEffectiveSortFields(pagination.SortFields);
        IOrderedQueryable<TEntity> orderedQuery;
        KeysetPlan? keysetPlan;
        var offsetStartIndex = 0;
        if (TryBuildKeysetPlan(effectiveSortFields, out var builtKeysetPlan))
        {
            var plan = builtKeysetPlan!;
            keysetPlan = plan;
            orderedQuery = ApplyKeysetCursorFilter(query, plan, pagination.Cursor);
        }
        else
        {
            keysetPlan = null;
            LogKeysetFallback(effectiveSortFields);
            offsetStartIndex = DecodeOffsetCursor(pagination.Cursor);
            orderedQuery = SortBuilder.ApplySorting(query, pagination.SortFields, _keySelector);
        }

        var takeCount = pagination.Limit == int.MaxValue ? int.MaxValue : pagination.Limit + 1;
        List<TEntity> pagedItems;

        if (keysetPlan is not null)
        {
            pagedItems = await orderedQuery
                .Take(takeCount)
                .ToListAsync(ct);
        }
        else
        {
            pagedItems = await orderedQuery
                .Skip(offsetStartIndex)
                .Take(takeCount)
                .ToListAsync(ct);
        }

        var hasMore = pagedItems.Count > pagination.Limit;
        if (hasMore)
        {
            pagedItems = pagedItems.Take(pagination.Limit).ToList();
        }

        string? nextCursor;
        if (!hasMore)
        {
            nextCursor = null;
        }
        else if (keysetPlan is not null)
        {
            nextCursor = EncodeKeysetCursor(keysetPlan, pagedItems[^1]);
        }
        else
        {
            nextCursor = offsetStartIndex <= int.MaxValue - pagination.Limit
                ? CursorEncoder.Encode(offsetStartIndex + pagination.Limit)
                : null;
        }

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
            ApplyPatch(entry, patchDocument, keyPropertyNames, _options.PatchUnknownFieldBehavior);

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

        var getKey = _keySelector.Compile();
        var keys = entities.Select(getKey).ToList();
        var containsMethod = typeof(Enumerable)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == nameof(Enumerable.Contains) && method.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(TKey));
        var containsCall = Expression.Call(
            containsMethod,
            Expression.Constant(keys),
            _keySelector.Body);
        var predicate = Expression.Lambda<Func<TEntity, bool>>(containsCall, _keySelector.Parameters);
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
            throw;
        }
        catch (DbUpdateException ex)
        {
            throw ClassifyConstraintViolation(ex);
        }

        return entities
            .Where(entity => existingById.ContainsKey(getKey(entity)))
            .Select(entity => existingById[getKey(entity)])
            .ToList();
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

            ApplyPatch(
                _context.Entry(existing),
                patchDocument,
                keyPropertyNames,
                _options.PatchUnknownFieldBehavior);
            results.Add(existing);
        }

        try
        {
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

        return patches
            .Where(patch => existingById.ContainsKey(patch.Id))
            .Select(patch => existingById[patch.Id])
            .ToList();
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

        var idList = ids.ToList();
        var containsMethod = typeof(Enumerable)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == nameof(Enumerable.Contains) && method.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(TKey));
        var containsCall = Expression.Call(
            containsMethod,
            Expression.Constant(idList),
            _keySelector.Body);
        var predicate = Expression.Lambda<Func<TEntity, bool>>(containsCall, _keySelector.Parameters);
        var getKey = _keySelector.Compile();

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
        var constraintType = ConstraintViolationClassifier.Classify(ex);

        return new EfCoreConstraintViolationException(
            message ?? "A database constraint violation occurred.",
            constraintType,
            ex);
    }

    private static List<KeysetSortPart> GetEffectiveSortFields(IReadOnlyList<SortField> sortFields)
    {
        return sortFields
            .Select(sortField => new KeysetSortPart(sortField.PropertyName, sortField.Direction, sortField.QueryParameterName))
            .ToList();
    }

    private int DecodeOffsetCursor(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            return 0;
        }

        if (CursorEncoder.TryDecode<int>(cursor, out var cursorIndex))
        {
            return cursorIndex;
        }

        throw new EfCoreInvalidCursorException("The provided cursor is not a valid offset cursor for this result set.");
    }

    private bool TryBuildKeysetPlan(
        IReadOnlyList<KeysetSortPart> sortFields,
        out KeysetPlan? keysetPlan)
    {
        var parts = new List<KeysetPlanPart>();

        foreach (var sortField in sortFields)
        {
            if (!TryBuildKeysetPlanPart(sortField.PropertyName, sortField.Direction, sortField.QueryParameterName, out var part))
            {
                keysetPlan = null;
                return false;
            }

            parts.Add(part!);
        }

        if (!parts.Any(part => string.Equals(part.PropertyName, GetKeyPropertyName(), StringComparison.Ordinal)))
        {
            if (!TryBuildKeysetPlanPart(GetKeyPropertyName(), SortDirection.Asc, JsonNamingPolicy.SnakeCaseLower.ConvertName(GetKeyPropertyName()), out var keyPart))
            {
                keysetPlan = null;
                return false;
            }

            parts.Add(keyPart!);
        }

        keysetPlan = new KeysetPlan(parts);
        return true;
    }

    private bool TryBuildKeysetPlanPart(
        string propertyName,
        SortDirection direction,
        string queryParameterName,
        out KeysetPlanPart? part)
    {
        var property = typeof(TEntity).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (property is null || !IsKeysetComparableType(property.PropertyType))
        {
            part = null;
            return false;
        }

        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        var memberAccess = Expression.Property(parameter, property);
        var delegateType = typeof(Func<,>).MakeGenericType(typeof(TEntity), property.PropertyType);
        var selector = Expression.Lambda(delegateType, memberAccess, parameter);

        part = new KeysetPlanPart(property.Name, queryParameterName, property.PropertyType, direction, selector, property);
        return true;
    }

    private IOrderedQueryable<TEntity> ApplyKeysetCursorFilter(
        IQueryable<TEntity> query,
        KeysetPlan keysetPlan,
        string? cursor)
    {
        var filteredQuery = query;
        if (!string.IsNullOrEmpty(cursor))
        {
            var decodedCursor = DecodeKeysetCursor(cursor, keysetPlan);
            var predicate = BuildKeysetPredicate(keysetPlan, decodedCursor);
            filteredQuery = filteredQuery.Where(predicate);
        }

        return ApplyKeysetOrdering(filteredQuery, keysetPlan);
    }

    private IOrderedQueryable<TEntity> ApplyKeysetOrdering(IQueryable<TEntity> query, KeysetPlan keysetPlan)
    {
        IOrderedQueryable<TEntity>? orderedQuery = null;

        foreach (var part in keysetPlan.Parts)
        {
            var method = GetQueryableSortMethod(part.Direction, orderedQuery is null);
            orderedQuery = ApplyQueryableOrdering(method, orderedQuery ?? query, part.Selector);
        }

        return orderedQuery!;
    }

    private Expression<Func<TEntity, bool>> BuildKeysetPredicate(KeysetPlan keysetPlan, EfCoreKeysetCursor cursor)
    {
        if (cursor.Version != KeysetCursorVersion)
        {
            throw new EfCoreInvalidCursorException("The provided cursor version is not supported.");
        }

        if (cursor.Values.Count != keysetPlan.Parts.Count)
        {
            throw new EfCoreInvalidCursorException("The provided cursor does not match the active sort shape.");
        }

        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        Expression? predicate = null;

        for (var i = 0; i < keysetPlan.Parts.Count; i++)
        {
            Expression? andChain = null;

            for (var j = 0; j < i; j++)
            {
                var equalsExpression = BuildComparisonExpression(parameter, keysetPlan.Parts[j], cursor.Values[j], ExpressionType.Equal);
                andChain = andChain is null ? equalsExpression : Expression.AndAlso(andChain, equalsExpression);
            }

            var comparisonType = keysetPlan.Parts[i].Direction == SortDirection.Asc
                ? ExpressionType.GreaterThan
                : ExpressionType.LessThan;
            var comparisonExpression = BuildComparisonExpression(parameter, keysetPlan.Parts[i], cursor.Values[i], comparisonType);
            var branch = andChain is null ? comparisonExpression : Expression.AndAlso(andChain, comparisonExpression);
            predicate = predicate is null ? branch : Expression.OrElse(predicate, branch);
        }

        return Expression.Lambda<Func<TEntity, bool>>(predicate!, parameter);
    }

    private Expression BuildComparisonExpression(
        ParameterExpression parameter,
        KeysetPlanPart part,
        JsonElement cursorValue,
        ExpressionType comparisonType)
    {
        var left = Expression.Property(parameter, part.Property);
        var typedValue = JsonSerializer.Deserialize(cursorValue.GetRawText(), part.PropertyType)
            ?? throw new EfCoreInvalidCursorException($"The provided cursor contains an invalid value for '{part.QueryParameterName}'.");
        var right = Expression.Constant(typedValue, part.PropertyType);

        return Expression.MakeBinary(comparisonType, left, right);
    }

    private string EncodeKeysetCursor(KeysetPlan keysetPlan, TEntity entity)
    {
        var values = keysetPlan.Parts
            .Select(part => JsonSerializer.SerializeToElement(part.Property.GetValue(entity), part.PropertyType))
            .ToList();

        return CursorEncoder.Encode(new EfCoreKeysetCursor
        {
            Version = KeysetCursorVersion,
            SortSignature = BuildSortSignature(keysetPlan),
            Values = values
        });
    }

    private EfCoreKeysetCursor DecodeKeysetCursor(string cursor, KeysetPlan keysetPlan)
    {
        if (CursorEncoder.TryDecode<EfCoreKeysetCursor>(cursor, out var decodedCursor))
        {
            var keysetCursor = decodedCursor ?? throw new EfCoreInvalidCursorException("The provided cursor could not be decoded.");
            if (!string.Equals(keysetCursor.SortSignature, BuildSortSignature(keysetPlan), StringComparison.Ordinal))
            {
                throw new EfCoreInvalidCursorException("The provided cursor does not match the active sort order.");
            }

            return keysetCursor;
        }

        if (CursorEncoder.TryDecode<int>(cursor, out _))
        {
            throw new EfCoreInvalidCursorException("Offset cursors are no longer valid for this sorted result set.");
        }

        throw new EfCoreInvalidCursorException("The provided cursor is not a valid EF Core pagination cursor.");
    }

    private IQueryable<TEntity> ApplyComparisonFilters(
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

    private bool IsComparisonOperator(FilterOperator op)
    {
        return op is FilterOperator.Eq
            or FilterOperator.Neq
            or FilterOperator.Gt
            or FilterOperator.Lt
            or FilterOperator.Gte
            or FilterOperator.Lte;
    }

    private IQueryable<TEntity> ApplyStringFilters(
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

    private bool IsStringOperator(FilterOperator op)
    {
        return op is FilterOperator.Contains
            or FilterOperator.StartsWith
            or FilterOperator.EndsWith;
    }

    private IQueryable<TEntity> ApplyInFilters(
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

    private bool IsInOperator(FilterOperator op)
    {
        return op is FilterOperator.In;
    }

    private MethodInfo GetQueryableSortMethod(SortDirection direction, bool isPrimarySort)
    {
        return (direction, isPrimarySort) switch
        {
            (SortDirection.Asc, true) => OrderByMethod,
            (SortDirection.Desc, true) => OrderByDescendingMethod,
            (SortDirection.Asc, false) => ThenByMethod,
            _ => ThenByDescendingMethod,
        };
    }

    private IOrderedQueryable<TEntity> ApplyQueryableOrdering(
        MethodInfo method,
        IQueryable<TEntity> source,
        LambdaExpression keySelector)
    {
        var genericMethod = method.MakeGenericMethod(typeof(TEntity), keySelector.ReturnType);
        return (IOrderedQueryable<TEntity>)genericMethod.Invoke(null, [source, keySelector])!;
    }

    private void ApplyPatch(
        EntityEntry<TEntity> entry,
        JsonElement patchDocument,
        IReadOnlySet<string> keyPropertyNames,
        EfCorePatchUnknownFieldBehavior unknownFieldBehavior)
    {
        foreach (var patchProperty in patchDocument.EnumerateObject())
        {
            if (!SnakeCasePropertyMap.TryGetValue(patchProperty.Name, out var propertyInfo))
            {
                ThrowIfStrictUnknownField(unknownFieldBehavior, patchProperty.Name, "unknown");
                continue;
            }

            if (keyPropertyNames.Contains(propertyInfo.Name))
            {
                ThrowIfStrictUnknownField(unknownFieldBehavior, patchProperty.Name, "forbidden");
                continue;
            }

            var value = JsonSerializer.Deserialize(
                patchProperty.Value.GetRawText(),
                propertyInfo.PropertyType,
                PatchJsonOptions);

            entry.Property(propertyInfo.Name).CurrentValue = value;
        }
    }

    private void ThrowIfStrictUnknownField(
        EfCorePatchUnknownFieldBehavior unknownFieldBehavior,
        string propertyName,
        string reason)
    {
        if (unknownFieldBehavior == EfCorePatchUnknownFieldBehavior.Strict)
        {
            throw new EfCorePatchValidationException(
                $"PATCH field '{propertyName}' is {reason} for this resource.");
        }
    }

    private Expression<Func<TEntity, bool>> BuildKeyEqualsPredicate(TKey id)
    {
        var constant = Expression.Constant(id, typeof(TKey));
        var equals = Expression.Equal(_keySelector.Body, constant);

        return Expression.Lambda<Func<TEntity, bool>>(equals, _keySelector.Parameters);
    }

    private async Task<Dictionary<TKey, TEntity>> FetchTrackedEntitiesByIdsAsync(
        IReadOnlyList<TKey> ids,
        CancellationToken ct)
    {
        var getKey = _keySelector.Compile();
        var idList = ids.ToList();
        var containsMethod = typeof(Enumerable)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == nameof(Enumerable.Contains) && method.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(TKey));
        var containsCall = Expression.Call(
            containsMethod,
            Expression.Constant(idList),
            _keySelector.Body);
        var predicate = Expression.Lambda<Func<TEntity, bool>>(containsCall, _keySelector.Parameters);
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

    private string GetKeyPropertyName()
    {
        if (_keySelector.Body is MemberExpression memberExpression)
        {
            return memberExpression.Member.Name;
        }

        throw new InvalidOperationException("Key selector must resolve to a direct member access for keyset pagination.");
    }

    private string BuildSortSignature(KeysetPlan keysetPlan)
    {
        return string.Join(",", keysetPlan.Parts.Select(part => $"{part.QueryParameterName}:{part.Direction}"));
    }

    private void LogKeysetFallback(IReadOnlyList<KeysetSortPart> effectiveSortFields)
    {
        if (_options.Logger is null)
        {
            return;
        }

        var sortDescription = effectiveSortFields.Count == 0
            ? "key only"
            : string.Join(", ", effectiveSortFields.Select(field => $"{field.QueryParameterName}:{field.Direction}"));
        _options.Logger.LogWarning(
            "EF Core keyset pagination fallback activated for {EntityType} with sort {SortDescription}; using offset cursor pagination instead.",
            typeof(TEntity).Name,
            sortDescription);
    }

    private Expression<Func<TEntity, TKey>> ResolveKeySelector()
    {
        var primaryKey = GetPrimaryKey();

        if (primaryKey.Properties.Count > 1)
        {
            var propertyNames = string.Join(", ", primaryKey.Properties.Select(property => property.Name));
            throw new NotSupportedException(
                $"Entity type '{typeof(TEntity).Name}' has a composite primary key, which is not supported. Composite keys have {primaryKey.Properties.Count} properties: {propertyNames}. Only single-property primary keys are supported.");
        }

        var keyProperty = primaryKey.Properties[0];
        if (keyProperty.ClrType != typeof(TKey))
        {
            throw new InvalidOperationException(
                $"Entity type '{typeof(TEntity).Name}' has primary key property '{keyProperty.Name}' of type '{keyProperty.ClrType.Name}', but the registration specifies TKey as '{typeof(TKey).Name}'.");
        }

        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        var propertyAccess = keyProperty.PropertyInfo is not null
            ? Expression.Property(parameter, keyProperty.PropertyInfo)
            : Expression.Property(parameter, keyProperty.Name);

        return Expression.Lambda<Func<TEntity, TKey>>(propertyAccess, parameter);
    }

    private bool IsKeysetComparableType(Type propertyType)
    {
        var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        return underlyingType.IsEnum ||
            underlyingType == typeof(string) ||
            underlyingType == typeof(Guid) ||
            underlyingType == typeof(DateTime) ||
            underlyingType == typeof(DateTimeOffset) ||
            underlyingType == typeof(decimal) ||
            underlyingType == typeof(double) ||
            underlyingType == typeof(float) ||
            underlyingType == typeof(long) ||
            underlyingType == typeof(int) ||
            underlyingType == typeof(short) ||
            underlyingType == typeof(byte) ||
            underlyingType == typeof(ulong) ||
            underlyingType == typeof(uint) ||
            underlyingType == typeof(ushort) ||
            underlyingType == typeof(sbyte) ||
            underlyingType == typeof(bool);
    }

    private sealed record KeysetSortPart(string PropertyName, SortDirection Direction, string QueryParameterName);

    private sealed record KeysetPlan(IReadOnlyList<KeysetPlanPart> Parts);

    private sealed record KeysetPlanPart(
        string PropertyName,
        string QueryParameterName,
        Type PropertyType,
        SortDirection Direction,
        LambdaExpression Selector,
        PropertyInfo Property);
}
