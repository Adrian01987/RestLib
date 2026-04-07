using Microsoft.AspNetCore.Http;
using RestLib.Abstractions;
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
    protected override string DeserializationErrorMessage =>
        "The 'items' array could not be deserialized as a list of IDs.";

    /// <inheritdoc/>
    protected override async Task<(BatchItemResult? Error, (int Index, TKey Key) ValidItem)> ValidateItemAsync(
        int index,
        TKey? key,
        BatchContext<TEntity, TKey> context)
    {
        if (key is null)
        {
            return (BadRequestResult(index, $"Item at index {index} has a null or invalid ID.", context.HttpContext.Request.Path), default);
        }

        // Hooks: OnRequestReceived, OnRequestValidated, BeforePersist
        if (context.Pipeline is not null)
        {
            var hookContext = context.Pipeline.CreateContext(
                context.HttpContext, RestLibOperation.BatchDelete, resourceId: key);

            var hookError = await RunPrePersistHooksAsync(index, context.Pipeline, hookContext);
            if (hookError is not null)
            {
                return (hookError, default);
            }
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
        var itemsToDelete = new List<(int Index, TKey Key)>();
        var entityName = typeof(TEntity).Name;

        foreach (var (index, key) in validItems)
        {
            var existing = await context.Repository.GetByIdAsync(key, context.CancellationToken);
            if (existing is null)
            {
                results[index] = new BatchItemResult
                {
                    Index = index,
                    Status = StatusCodes.Status404NotFound,
                    Error = ProblemDetailsFactory.NotFound(entityName, key!, context.HttpContext.Request.Path)
                };
                continue;
            }

            itemsToDelete.Add((index, key));
        }

        if (itemsToDelete.Count == 0)
        {
            return;
        }

        var keys = itemsToDelete.Select(v => v.Key).ToList();
        await context.BatchRepository!.DeleteManyAsync(keys, context.CancellationToken);

        // Run AfterPersist hooks and build 204 results for each deleted item.
        foreach (var (index, key) in itemsToDelete)
        {
            if (context.Pipeline is not null)
            {
                var afterContext = context.Pipeline.CreateContext(
                    context.HttpContext, RestLibOperation.BatchDelete, resourceId: key);
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
        var deleted = await context.Repository.DeleteAsync(key, context.CancellationToken);
        if (!deleted)
        {
            var entityName = typeof(TEntity).Name;
            results[index] = new BatchItemResult
            {
                Index = index,
                Status = StatusCodes.Status404NotFound,
                Error = ProblemDetailsFactory.NotFound(entityName, key!, context.HttpContext.Request.Path)
            };
            return;
        }

        if (context.Pipeline is not null)
        {
            var afterContext = context.Pipeline.CreateContext(
                context.HttpContext, RestLibOperation.BatchDelete, resourceId: key);
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
