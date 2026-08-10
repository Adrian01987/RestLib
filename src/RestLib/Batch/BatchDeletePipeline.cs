using Microsoft.AspNetCore.Http;
using RestLib.Abstractions;
using RestLib.Endpoints;
using RestLib.Logging;
using RestLib.Responses;

namespace RestLib.Batch;

/// <summary>
/// Batch delete pipeline. Deserializes keys, validates via hooks, and persists
/// via <see cref="IBatchRepository{TEntity, TKey}.DeleteManyAsync"/> when available,
/// falling back to individual <see cref="IRepository{TEntity, TKey}.DeleteAsync"/> calls.
/// The bulk path pre-checks existence to provide per-item 404 detection before
/// calling <c>DeleteManyAsync</c> with only the keys that are known to exist.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
internal sealed class BatchDeletePipeline<TEntity, TKey>
    : BatchActionPipeline<TEntity, TKey, TKey, (int Index, TKey Key)>
    where TEntity : class
    where TKey : notnull
{
    /// <inheritdoc/>
    protected override int SuccessStatusCode => StatusCodes.Status204NoContent;

    /// <inheritdoc/>
    protected override RestLibOperation Operation => RestLibOperation.BatchDelete;

    /// <inheritdoc/>
    protected override async Task<(BatchItemResult? Error, (int Index, TKey Key) ValidItem)> ValidateItemAsync(
        int index,
        TKey? key,
        BatchContext<TEntity, TKey> context)
    {
        if (key is null)
            return (BadRequestResult(index, $"Item at index {index} has a null or invalid ID.", context.HttpContext.Request.Path), default);

        // Request hooks run before the bulk existence lookup. BeforePersist runs
        // after the entity has been loaded so mapped and unmapped modes expose
        // the same entity-bearing stage.
        if (context.Pipeline is not null)
        {
            var hookContext = context.Pipeline.CreateContext(
                context.HttpContext, RestLibOperation.BatchDelete, resourceId: key);

            var hookError = await RunRequestHooksAsync(index, context.Pipeline, hookContext);
            if (hookError is not null) return (hookError, default);
        }

        return (null, (index, key));
    }

    /// <inheritdoc/>
    protected override int GetIndex((int Index, TKey Key) validItem) => validItem.Index;

    /// <inheritdoc/>
    protected override TKey? GetResourceId((int Index, TKey Key) validItem) => validItem.Key;

    /// <inheritdoc/>
    protected override async Task PersistBulkAsync(
        List<(int Index, TKey Key)> validItems,
        BatchItemResult?[] results,
        BatchContext<TEntity, TKey> context)
    {
        // Pre-check existence so we can produce per-item 404s before calling DeleteManyAsync.
        // Use GetByIdsAsync for a single bulk fetch instead of N individual GetByIdAsync calls.
        var keys = validItems.Select(v => v.Key).ToList();
        var existingEntities = await BulkPersistenceExecutor.ExecuteAsync(
            () => context.BatchRepository!.GetByIdsAsync(keys, context.CancellationToken),
            context.CancellationToken);

        var itemsToDelete = new List<(int Index, TKey Key, TEntity Entity)>();
        var entityName = typeof(TEntity).Name;

        foreach (var (index, key) in validItems)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if (!existingEntities.TryGetValue(key, out var existingEntity))
            {
                RestLibLogMessages.BatchDeleteItemNotFound(context.Logger, index, entityName, key!);
                results[index] = new BatchItemResult
                {
                    Index = index,
                    Status = StatusCodes.Status404NotFound,
                    Error = ProblemDetailsFactory.NotFound(entityName, key, context.EndpointConfig.KeyRouteParts, context.HttpContext.Request.Path)
                };
                continue;
            }

            if (context.Pipeline is not null)
            {
                var hookContext = context.Pipeline.CreateContext(
                    context.HttpContext,
                    RestLibOperation.BatchDelete,
                    resourceId: key,
                    entity: existingEntity);
                var hookError = await RunBeforePersistHookAsync(index, context.Pipeline, hookContext);
                if (hookError is not null)
                {
                    results[index] = hookError;
                    continue;
                }

                existingEntity = hookContext.Entity ?? existingEntity;
                _ = EntityKeyHelper.TrySetEntityKeyParts(
                    existingEntity,
                    key,
                    context.EndpointConfig.KeyRouteParts);
            }

            itemsToDelete.Add((index, key, existingEntity));
        }

        if (itemsToDelete.Count == 0) return;

        var keysToDelete = itemsToDelete.Select(v => v.Key).ToList();
        var deletedCount = await BulkPersistenceExecutor.ExecuteAsync(
            () => context.BatchRepository!.DeleteManyAsync(keysToDelete, context.CancellationToken),
            context.CancellationToken);

        BatchRepositoryResultContract.ValidateDeletedCount(
            keysToDelete.Distinct().Count(),
            deletedCount);

        RestLibLogMessages.BatchDeleteCompleted(context.Logger, deletedCount);

        // Run AfterPersist hooks and build 204 results for each deleted item.
        foreach (var (index, key, entity) in itemsToDelete)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if (context.Pipeline is not null)
            {
                var afterContext = context.Pipeline.CreateContext(
                    context.HttpContext,
                    RestLibOperation.BatchDelete,
                    resourceId: key,
                    entity: entity);
                var shouldContinue = await context.Pipeline.ExecuteAfterPersistAsync(afterContext);
                if (!shouldContinue)
                {
                    results[index] = BuildHookResultItem(index, afterContext.EarlyResult, context.HttpContext);
                    continue;
                }
            }

            results[index] = new BatchItemResult
            {
                Index = index,
                Status = StatusCodes.Status204NoContent
            };
        }
    }

    /// <inheritdoc/>
    protected override async Task PersistSingleItemAsync(
        (int Index, TKey Key) validItem,
        BatchItemResult?[] results,
        BatchContext<TEntity, TKey> context)
    {
        var (index, key) = validItem;
        TEntity? entityToDelete = null;

        if (context.Pipeline is not null)
        {
            entityToDelete = await context.Repository.GetByIdAsync(key, context.CancellationToken);
            if (entityToDelete is null)
            {
                var entityName = typeof(TEntity).Name;
                RestLibLogMessages.BatchDeleteItemNotFound(context.Logger, index, entityName, key!);
                results[index] = new BatchItemResult
                {
                    Index = index,
                    Status = StatusCodes.Status404NotFound,
                    Error = ProblemDetailsFactory.NotFound(
                        entityName,
                        key!,
                        context.EndpointConfig.KeyRouteParts,
                        context.HttpContext.Request.Path)
                };
                return;
            }

            var hookContext = context.Pipeline.CreateContext(
                context.HttpContext,
                RestLibOperation.BatchDelete,
                resourceId: key,
                entity: entityToDelete);
            var hookError = await RunBeforePersistHookAsync(index, context.Pipeline, hookContext);
            if (hookError is not null)
            {
                results[index] = hookError;
                return;
            }

            entityToDelete = hookContext.Entity ?? entityToDelete;
            _ = EntityKeyHelper.TrySetEntityKeyParts(
                entityToDelete,
                key,
                context.EndpointConfig.KeyRouteParts);
        }

        var deleted = await context.Repository.DeleteAsync(key, context.CancellationToken);
        if (!deleted)
        {
            var entityName = typeof(TEntity).Name;
            RestLibLogMessages.BatchDeleteItemNotFound(context.Logger, index, entityName, key!);
            results[index] = new BatchItemResult
            {
                Index = index,
                Status = StatusCodes.Status404NotFound,
                Error = ProblemDetailsFactory.NotFound(entityName, key, context.EndpointConfig.KeyRouteParts, context.HttpContext.Request.Path)
            };
            return;
        }

        if (context.Pipeline is not null)
        {
            var afterContext = context.Pipeline.CreateContext(
                context.HttpContext,
                RestLibOperation.BatchDelete,
                resourceId: key,
                entity: entityToDelete);
            var shouldContinue = await context.Pipeline.ExecuteAfterPersistAsync(afterContext);
            if (!shouldContinue)
            {
                results[index] = BuildHookResultItem(index, afterContext.EarlyResult, context.HttpContext);
                return;
            }
        }

        results[index] = new BatchItemResult
        {
            Index = index,
            Status = StatusCodes.Status204NoContent
        };
    }
}
