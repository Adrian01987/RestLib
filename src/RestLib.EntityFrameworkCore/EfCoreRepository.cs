using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using RestLib.Abstractions;
using RestLib.FieldSelection;
using RestLib.Filtering;
using RestLib.Pagination;
using RestLib.Serialization;
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
      IConditionalWriteRepository<TEntity, TKey>,
      ICountableRepository<TEntity, TKey>,
      IQueryCountableRepository<TEntity, TKey>,
      IFieldSelectionProjectionRepository<TEntity, TKey>
    where TContext : DbContext
    where TEntity : class
    where TKey : notnull
{
    private readonly TContext _context;
    private readonly EfCoreRepositoryOptions<TEntity, TKey> _options;
    private readonly EfCoreBatchKeyQueryExecutor<TEntity, TKey> _batchKeyQueryExecutor;
    private readonly EfCoreKeyMetadata<TEntity, TKey> _keyMetadata;
    private readonly EfCorePatchPlanner<TEntity> _patchPlanner;
    private readonly EfCorePageQueryExecutor<TEntity> _pageQueryExecutor;
    private readonly EfCoreProjectionPlanner<TEntity> _projectionPlanner;

    /// <summary>
    /// Initializes a new instance of the <see cref="EfCoreRepository{TContext, TEntity, TKey}"/> class.
    /// </summary>
    /// <param name="context">The EF Core DbContext used by the repository.</param>
    /// <param name="options">The repository options.</param>
    public EfCoreRepository(TContext context, EfCoreRepositoryOptions<TEntity, TKey> options)
        : this(context, options, RestLibJsonOptions.CreateDefault())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EfCoreRepository{TContext, TEntity, TKey}"/> class.
    /// </summary>
    /// <param name="context">The EF Core DbContext used by the repository.</param>
    /// <param name="options">The repository options.</param>
    /// <param name="jsonOptions">The JSON serializer options used by PATCH operations.</param>
    public EfCoreRepository(
        TContext context,
        EfCoreRepositoryOptions<TEntity, TKey> options,
        JsonSerializerOptions jsonOptions)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentNullException.ThrowIfNull(jsonOptions);
        var jsonContract = JsonObjectContract.Get(typeof(TEntity), jsonOptions);
        _patchPlanner = new EfCorePatchPlanner<TEntity>(_context.Model, jsonContract);
        var planningBundle = EfCoreRepositoryPlanCache<TEntity, TKey>.GetOrCreate(
            _context.Model,
            _options);
        _keyMetadata = planningBundle.KeyMetadata;
        _batchKeyQueryExecutor = new EfCoreBatchKeyQueryExecutor<TEntity, TKey>(_keyMetadata);
        _pageQueryExecutor = new EfCorePageQueryExecutor<TEntity>(
            _context.Model,
            _keyMetadata.SortKeyParts,
            () => _options.Logger,
            planningBundle.PagePlanningCache);
        _projectionPlanner = new EfCoreProjectionPlanner<TEntity>(
            () => _options.EnableProjectionPushdown,
            _keyMetadata.PropertyNames,
            planningBundle.ProjectionPlanningCache);
    }

    /// <inheritdoc />
    public Task<TEntity?> GetByIdAsync(TKey id, CancellationToken ct = default)
    {
        if (!_options.UseAsNoTracking)
        {
            if (!_keyMetadata.UsesExplicitKeySelector)
            {
                return _context.Set<TEntity>().FindAsync(_keyMetadata.GetKeyValues(id), ct).AsTask();
            }

            return _context.Set<TEntity>()
                .FirstOrDefaultAsync(_keyMetadata.BuildEqualsPredicate(id), ct);
        }

        var predicate = _keyMetadata.BuildEqualsPredicate(id);
        return _context.Set<TEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(predicate, ct);
    }

    /// <inheritdoc />
    public async Task<TEntity?> GetByIdProjectedAsync(
        TKey id,
        IReadOnlyList<SelectedField> selectedFields,
        IReadOnlyList<FilterValue>? filters = null,
        IReadOnlyList<SortField>? sortFields = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(selectedFields);

        if (!_projectionPlanner.TryBuild(
                selectedFields,
                filters ?? [],
                sortFields ?? [],
                search: null,
                out var projectionPlan))
        {
            if (!_projectionPlanner.TryBuildNavigationLoadPaths(selectedFields, out var includePaths))
            {
                return null;
            }

            var includeQuery = _projectionPlanner.ApplyIncludes(GetBaseProjectionQuery(), includePaths);
            var includePredicate = _keyMetadata.BuildEqualsPredicate(id);
            return await includeQuery.FirstOrDefaultAsync(includePredicate, ct);
        }

        var predicate = _keyMetadata.BuildEqualsPredicate(id);
        var plan = projectionPlan!;
        return await _projectionPlanner.BuildQuery(GetBaseProjectionQuery(), plan)
            .FirstOrDefaultAsync(predicate, ct);
    }

    /// <inheritdoc />
    public async Task<PagedResult<TEntity>> GetAllAsync(PaginationRequest pagination, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pagination);

        return await _pageQueryExecutor.ExecuteAsync(GetBaseQuery(), pagination, ct);
    }

    /// <inheritdoc />
    public async Task<PagedResult<TEntity>?> GetAllProjectedAsync(
        PaginationRequest pagination,
        IReadOnlyList<SelectedField> selectedFields,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pagination);
        ArgumentNullException.ThrowIfNull(selectedFields);

        if (!_projectionPlanner.TryBuild(
                selectedFields,
                pagination.Filters,
                pagination.SortFields,
                pagination.Search,
                out var projectionPlan))
        {
            if (!_projectionPlanner.TryBuildNavigationLoadPaths(selectedFields, out var includePaths))
            {
                return null;
            }

            return await GetAllWithIncludedNavigationsAsync(pagination, includePaths, ct);
        }

        var projection = projectionPlan!;
        var query = _projectionPlanner.BuildQuery(GetBaseProjectionQuery(), projection);
        return await _pageQueryExecutor.ExecuteAsync(query, pagination, ct);
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

        var existing = _keyMetadata.UsesExplicitKeySelector
            ? await _context.Set<TEntity>().FirstOrDefaultAsync(_keyMetadata.BuildEqualsPredicate(id), ct)
            : await _context.Set<TEntity>().FindAsync(_keyMetadata.GetKeyValues(id), ct);
        if (existing is null)
        {
            return null;
        }

        try
        {
            _keyMetadata.CopyPrimaryKeyValues(existing, entity);
            var entry = _context.Entry(existing);
            entry.CurrentValues.SetValues(entity);
            _keyMetadata.SetResourceKeyValues(entry, id);
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
        var existing = _keyMetadata.UsesExplicitKeySelector
            ? await _context.Set<TEntity>().FirstOrDefaultAsync(_keyMetadata.BuildEqualsPredicate(id), ct)
            : await _context.Set<TEntity>().FindAsync(_keyMetadata.GetKeyValues(id), ct);
        if (existing is null)
        {
            return null;
        }

        var entry = _context.Entry(existing);
        var primaryKey = _keyMetadata.PrimaryKey;
        var keyPropertyNames = primaryKey.Properties
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        keyPropertyNames.UnionWith(_keyMetadata.PropertyNames);
        var patchPlan = _patchPlanner.BuildPlan(
            entry,
            existing,
            patchDocument,
            keyPropertyNames,
            _options.PatchUnknownFieldBehavior,
            new Dictionary<string, object?>(StringComparer.Ordinal));
        var snapshots = new List<EfCorePatchPropertySnapshot<TEntity>>(
            patchPlan.OperationCount);

        try
        {
            _patchPlanner.ApplyPlan(patchPlan, snapshots);
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            _patchPlanner.RestoreChanges(snapshots);
            return null;
        }
        catch (DbUpdateException ex)
        {
            _patchPlanner.RestoreChanges(snapshots);
            throw ClassifyConstraintViolation(ex);
        }
        catch
        {
            _patchPlanner.RestoreChanges(snapshots);
            throw;
        }

        return existing;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(TKey id, CancellationToken ct = default)
    {
        var existing = _keyMetadata.UsesExplicitKeySelector
            ? await _context.Set<TEntity>().FirstOrDefaultAsync(_keyMetadata.BuildEqualsPredicate(id), ct)
            : await _context.Set<TEntity>().FindAsync(_keyMetadata.GetKeyValues(id), ct);
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
    public Task<ConditionalWriteResult<TEntity>> UpdateConditionallyAsync(
        TKey id,
        TEntity entity,
        Func<TEntity, bool> precondition,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(precondition);

        return ExecuteConditionalWriteAsync(
            id,
            precondition,
            async current =>
            {
                _keyMetadata.CopyPrimaryKeyValues(current, entity);
                var entry = TrackCurrentEntity(current, id);
                entry.CurrentValues.SetValues(entity);
                _keyMetadata.SetResourceKeyValues(entry, id);

                try
                {
                    await _context.SaveChangesAsync(ct);
                }
                catch (DbUpdateConcurrencyException)
                {
                    RestoreConditionalEntry(entry);
                    return ConditionalWriteResult<TEntity>.PreconditionFailed();
                }
                catch (DbUpdateException ex)
                {
                    RestoreConditionalEntry(entry);
                    throw ClassifyConstraintViolation(ex);
                }

                return ConditionalWriteResult<TEntity>.Success(entry.Entity);
            },
            ct);
    }

    /// <inheritdoc />
    public Task<ConditionalWriteResult<TEntity>> PatchConditionallyAsync(
        TKey id,
        JsonElement patchDocument,
        Func<TEntity, bool> precondition,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(precondition);

        return ExecuteConditionalWriteAsync(
            id,
            precondition,
            async current =>
            {
                var entry = TrackCurrentEntity(current, id);
                var primaryKey = _keyMetadata.PrimaryKey;
                var keyPropertyNames = primaryKey.Properties
                    .Select(property => property.Name)
                    .ToHashSet(StringComparer.Ordinal);
                keyPropertyNames.UnionWith(_keyMetadata.PropertyNames);
                var patchPlan = _patchPlanner.BuildPlan(
                    entry,
                    current,
                    patchDocument,
                    keyPropertyNames,
                    _options.PatchUnknownFieldBehavior,
                    new Dictionary<string, object?>(StringComparer.Ordinal));
                var snapshots = new List<EfCorePatchPropertySnapshot<TEntity>>(
                    patchPlan.OperationCount);

                try
                {
                    _patchPlanner.ApplyPlan(patchPlan, snapshots);
                    await _context.SaveChangesAsync(ct);
                }
                catch (DbUpdateConcurrencyException)
                {
                    _patchPlanner.RestoreChanges(snapshots);
                    return ConditionalWriteResult<TEntity>.PreconditionFailed();
                }
                catch (DbUpdateException ex)
                {
                    _patchPlanner.RestoreChanges(snapshots);
                    throw ClassifyConstraintViolation(ex);
                }
                catch
                {
                    _patchPlanner.RestoreChanges(snapshots);
                    throw;
                }

                return ConditionalWriteResult<TEntity>.Success(entry.Entity);
            },
            ct);
    }

    /// <inheritdoc />
    public Task<ConditionalWriteResult<TEntity>> DeleteConditionallyAsync(
        TKey id,
        Func<TEntity, bool> precondition,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(precondition);

        return ExecuteConditionalWriteAsync(
            id,
            precondition,
            async current =>
            {
                var entry = TrackCurrentEntity(current, id);
                entry.State = EntityState.Deleted;

                try
                {
                    await _context.SaveChangesAsync(ct);
                }
                catch (DbUpdateConcurrencyException)
                {
                    entry.State = EntityState.Unchanged;
                    return ConditionalWriteResult<TEntity>.PreconditionFailed();
                }
                catch (DbUpdateException ex)
                {
                    entry.State = EntityState.Unchanged;
                    throw ClassifyConstraintViolation(ex);
                }

                return ConditionalWriteResult<TEntity>.Success(entry.Entity);
            },
            ct);
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

        var snapshots = CaptureTrackingSnapshot();
        try
        {
            await _context.Set<TEntity>().AddRangeAsync(entities, ct);
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            RestoreTrackingSnapshot(snapshots);
            throw;
        }
        catch (DbUpdateException ex)
        {
            RestoreTrackingSnapshot(snapshots);
            throw ClassifyConstraintViolation(ex);
        }
        catch
        {
            RestoreTrackingSnapshot(snapshots);
            throw;
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

        var getKey = _keyMetadata.KeyAccessor;
        var keys = entities.Select(getKey).ToList();
        var existingEntities = await _batchKeyQueryExecutor.FetchAsync(
            _context.Set<TEntity>(),
            keys,
            ct);
        if (existingEntities.Count == 0)
        {
            return [];
        }

        var existingById = existingEntities.ToDictionary(getKey);
        var snapshots = CaptureTrackingSnapshot();

        try
        {
            foreach (var entity in entities)
            {
                var key = getKey(entity);
                if (!existingById.TryGetValue(key, out var existing))
                {
                    continue;
                }

                _keyMetadata.CopyPrimaryKeyValues(existing, entity);
                _context.Entry(existing).CurrentValues.SetValues(entity);
            }

            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            RestoreTrackingSnapshot(snapshots);
            throw;
        }
        catch (DbUpdateException ex)
        {
            RestoreTrackingSnapshot(snapshots);
            throw ClassifyConstraintViolation(ex);
        }
        catch
        {
            RestoreTrackingSnapshot(snapshots);
            throw;
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
        if (existingById.Count == 0)
        {
            return [];
        }

        var keyPropertyNames = _keyMetadata.PrimaryKey.Properties
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        keyPropertyNames.UnionWith(_keyMetadata.PropertyNames);
        var patchPlans = new List<EfCorePatchPlan<TEntity>>(patches.Count);
        var plannedValuesByEntity = new Dictionary<TEntity, Dictionary<string, object?>>(
            ReferenceEqualityComparer.Instance);

        foreach (var (id, patchDocument) in patches)
        {
            if (!existingById.TryGetValue(id, out var existing))
            {
                continue;
            }

            if (!plannedValuesByEntity.TryGetValue(existing, out var plannedValues))
            {
                plannedValues = new Dictionary<string, object?>(StringComparer.Ordinal);
                plannedValuesByEntity.Add(existing, plannedValues);
            }

            var patchPlan = _patchPlanner.BuildPlan(
                _context.Entry(existing),
                existing,
                patchDocument,
                keyPropertyNames,
                _options.PatchUnknownFieldBehavior,
                plannedValues);
            patchPlans.Add(patchPlan);
        }

        var snapshots = new List<EfCorePatchPropertySnapshot<TEntity>>();
        try
        {
            foreach (var patchPlan in patchPlans)
            {
                _patchPlanner.ApplyPlan(patchPlan, snapshots);
            }

            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            _patchPlanner.RestoreChanges(snapshots);
            throw;
        }
        catch (DbUpdateException ex)
        {
            _patchPlanner.RestoreChanges(snapshots);
            throw ClassifyConstraintViolation(ex);
        }
        catch
        {
            _patchPlanner.RestoreChanges(snapshots);
            throw;
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

        var snapshots = CaptureTrackingSnapshot();

        try
        {
            _context.Set<TEntity>().RemoveRange(found);
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            RestoreTrackingSnapshot(snapshots);
            return 0;
        }
        catch (DbUpdateException ex)
        {
            RestoreTrackingSnapshot(snapshots);
            throw ClassifyConstraintViolation(ex);
        }
        catch
        {
            RestoreTrackingSnapshot(snapshots);
            throw;
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

        var getKey = _keyMetadata.KeyAccessor;
        var entities = await _batchKeyQueryExecutor.FetchAsync(GetBaseQuery(), ids, ct);

        return entities.ToDictionary(getKey);
    }

    /// <inheritdoc />
    public Task<long> CountAsync(IReadOnlyList<FilterValue> filters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filters);

        var query = _pageQueryExecutor.ApplyFilters(GetBaseQuery(), filters);
        return query.LongCountAsync(ct);
    }

    /// <inheritdoc />
    public Task<long> CountAsync(PaginationRequest query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var countQuery = _pageQueryExecutor.ApplyCriteria(GetBaseQuery(), query);
        return countQuery.LongCountAsync(ct);
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

    private static void RestoreConditionalEntry(EntityEntry<TEntity> entry)
    {
        entry.CurrentValues.SetValues(entry.OriginalValues);
        entry.State = EntityState.Unchanged;
    }

    private async Task<ConditionalWriteResult<TEntity>> ExecuteConditionalWriteAsync(
        TKey id,
        Func<TEntity, bool> precondition,
        Func<TEntity, Task<ConditionalWriteResult<TEntity>>> mutation,
        CancellationToken ct)
    {
        var currentTransaction = _context.Database.CurrentTransaction;
        if (currentTransaction is not null)
        {
            if (currentTransaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            {
                throw new InvalidOperationException(
                    "Atomic conditional writes require an existing EF Core transaction to use Serializable isolation.");
            }

            return await ExecuteConditionalWriteCoreAsync(id, precondition, mutation, ct);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            ct);
        try
        {
            var result = await ExecuteConditionalWriteCoreAsync(id, precondition, mutation, ct);
            if (result.Status == ConditionalWriteStatus.Succeeded)
            {
                await transaction.CommitAsync(ct);
            }
            else
            {
                await transaction.RollbackAsync(ct);
            }

            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<ConditionalWriteResult<TEntity>> ExecuteConditionalWriteCoreAsync(
        TKey id,
        Func<TEntity, bool> precondition,
        Func<TEntity, Task<ConditionalWriteResult<TEntity>>> mutation,
        CancellationToken ct)
    {
        var current = await _context.Set<TEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(_keyMetadata.BuildEqualsPredicate(id), ct);
        if (current is null)
        {
            return ConditionalWriteResult<TEntity>.NotFound();
        }

        if (!precondition(current))
        {
            return ConditionalWriteResult<TEntity>.PreconditionFailed();
        }

        return await mutation(current);
    }

    private EntityEntry<TEntity> TrackCurrentEntity(TEntity current, TKey id)
    {
        var trackedEntry = _context.ChangeTracker
            .Entries<TEntity>()
            .FirstOrDefault(entry =>
                EqualityComparer<TKey>.Default.Equals(_keyMetadata.KeyAccessor(entry.Entity), id));
        if (trackedEntry is null)
        {
            return _context.Attach(current);
        }

        trackedEntry.State = EntityState.Unchanged;
        trackedEntry.CurrentValues.SetValues(current);
        trackedEntry.OriginalValues.SetValues(current);
        return trackedEntry;
    }

    private async Task<PagedResult<TEntity>> GetAllWithIncludedNavigationsAsync(
        PaginationRequest pagination,
        IReadOnlyList<string> includePaths,
        CancellationToken ct)
    {
        var query = _projectionPlanner.ApplyIncludes(GetBaseProjectionQuery(), includePaths);
        return await _pageQueryExecutor.ExecuteAsync(query, pagination, ct);
    }

    private IQueryable<TEntity> GetBaseProjectionQuery()
    {
        return _context.Set<TEntity>().AsNoTracking();
    }

    private IReadOnlyList<EntityTrackingSnapshot> CaptureTrackingSnapshot()
    {
        return _context.ChangeTracker
            .Entries()
            .Select(entry =>
            {
                return new EntityTrackingSnapshot(
                    entry,
                    entry.State,
                    entry.CurrentValues.Clone(),
                    entry.OriginalValues.Clone(),
                    entry.Properties.ToDictionary(
                        property => property.Metadata.Name,
                        property => property.IsModified,
                        StringComparer.Ordinal));
            })
            .ToList();
    }

    private void RestoreTrackingSnapshot(
        IEnumerable<EntityTrackingSnapshot> snapshots)
    {
        var snapshotList = snapshots.ToList();
        var previouslyTrackedEntities = snapshotList
            .Select(snapshot => snapshot.Entry.Entity)
            .ToHashSet(ReferenceEqualityComparer.Instance);
        var newlyTrackedEntries = _context.ChangeTracker
            .Entries()
            .Where(entry => !previouslyTrackedEntities.Contains(entry.Entity))
            .ToList();

        foreach (var entry in newlyTrackedEntries)
        {
            entry.State = EntityState.Detached;
        }

        foreach (var snapshot in snapshotList.AsEnumerable().Reverse())
        {
            snapshot.Entry.CurrentValues.SetValues(snapshot.CurrentValues!);
            snapshot.Entry.OriginalValues.SetValues(snapshot.OriginalValues!);
            snapshot.Entry.State = snapshot.State;

            foreach (var property in snapshot.Entry.Properties)
            {
                property.IsModified = snapshot.ModifiedProperties[property.Metadata.Name];
            }
        }
    }

    private async Task<Dictionary<TKey, TEntity>> FetchTrackedEntitiesByIdsAsync(
        IReadOnlyList<TKey> ids,
        CancellationToken ct)
    {
        var getKey = _keyMetadata.KeyAccessor;
        var existingEntities = await _batchKeyQueryExecutor.FetchAsync(
            _context.Set<TEntity>(),
            ids,
            ct);

        return existingEntities.ToDictionary(getKey);
    }

    private IQueryable<TEntity> GetBaseQuery()
    {
        var query = _context.Set<TEntity>().AsQueryable();
        return _options.UseAsNoTracking ? query.AsNoTracking() : query;
    }

    private sealed record EntityTrackingSnapshot(
        EntityEntry Entry,
        EntityState State,
        PropertyValues CurrentValues,
        PropertyValues OriginalValues,
        IReadOnlyDictionary<string, bool> ModifiedProperties);
}
