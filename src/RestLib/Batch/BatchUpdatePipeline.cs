using System.Text.Json;
using Microsoft.AspNetCore.Http;
using RestLib.Abstractions;
using RestLib.Logging;

namespace RestLib.Batch;

/// <summary>
/// Batch update pipeline. Deserializes <see cref="BatchUpdateItem{TKey}"/> items,
/// validates bodies and existence, persists via
/// <see cref="IRepository{TEntity, TKey}.UpdateAsync"/> or
/// <see cref="IBatchRepository{TEntity, TKey}.UpdateManyAsync"/>.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
internal sealed class BatchUpdatePipeline<TEntity, TKey>
    : BatchActionPipeline<TEntity, TKey, BatchUpdateItem<TKey>, (int Index, TKey Id, TEntity Entity)>
    where TEntity : class
    where TKey : notnull
{
    /// <inheritdoc/>
    protected override int SuccessStatusCode => StatusCodes.Status200OK;

    /// <inheritdoc/>
    protected override RestLibOperation Operation => RestLibOperation.BatchUpdate;

    /// <inheritdoc/>
    protected override async Task<(BatchItemResult? Error, (int Index, TKey Id, TEntity Entity) ValidItem)> ValidateItemAsync(
        int index,
        BatchUpdateItem<TKey>? item,
        BatchContext<TEntity, TKey> context)
    {
        if (item is null)
        {
            return (BadRequestResult(index, $"Item at index {index} could not be deserialized.", context.HttpContext.Request.Path), default);
        }

        // Deserialize the body as the entity type
        TEntity? entity;
        try
        {
            entity = item.Body.Deserialize<TEntity>(context.JsonOptions);
        }
        catch (JsonException ex)
        {
            RestLibLogMessages.BatchUpdateItemDeserializationFailed(context.Logger, index, ex);
            return (BadRequestResult(index, $"Item at index {index} has an invalid body.", context.HttpContext.Request.Path), default);
        }

        if (entity is null)
        {
            return (BadRequestResult(index, $"Item at index {index} body deserialized to null.", context.HttpContext.Request.Path), default);
        }

        // Fetch existing entity
        var existing = await context.Repository.GetByIdAsync(item.Id, context.CancellationToken);
        if (existing is null)
        {
            var entityName = typeof(TEntity).Name;
            return (NotFoundResult(index, entityName, item.Id!, context.HttpContext.Request.Path), default);
        }

        // Validation
        var validationError = ValidateEntity(index, entity, context);
        if (validationError is not null)
        {
            return (validationError, default);
        }

        // Hooks
        if (context.Pipeline is not null)
        {
            var hookContext = context.Pipeline.CreateContext(
                context.HttpContext, RestLibOperation.BatchUpdate,
                resourceId: item.Id, entity: entity, originalEntity: existing);

            var hookError = await RunPrePersistHooksAsync(index, context.Pipeline, hookContext);
            if (hookError is not null)
            {
                return (hookError, default);
            }

            entity = hookContext.Entity ?? entity;
        }

        return (null, (index, item.Id, entity));
    }

    /// <inheritdoc/>
    protected override int GetIndex((int Index, TKey Id, TEntity Entity) validItem) => validItem.Index;

    /// <inheritdoc/>
    protected override TKey? GetResourceId((int Index, TKey Id, TEntity Entity) validItem) => validItem.Id;

    /// <inheritdoc/>
    protected override TEntity? GetEntity((int Index, TKey Id, TEntity Entity) validItem) => validItem.Entity;

    /// <inheritdoc/>
    protected override async Task PersistBulkAsync(
        List<(int Index, TKey Id, TEntity Entity)> validItems,
        BatchItemResult?[] results,
        BatchContext<TEntity, TKey> context)
    {
        var entities = validItems.Select(v => v.Entity).ToList();
        var updated = await context.BatchRepository!.UpdateManyAsync(entities, context.CancellationToken);

        await ProcessBulkResultsAsync(validItems, updated, results, context);
    }

    /// <inheritdoc/>
    protected override async Task PersistSingleItemAsync(
        (int Index, TKey Id, TEntity Entity) validItem,
        BatchItemResult?[] results,
        BatchContext<TEntity, TKey> context)
    {
        var (index, id, entity) = validItem;
        var updated = await context.Repository.UpdateAsync(id, entity, context.CancellationToken);
        if (updated is null)
        {
            var entityName = typeof(TEntity).Name;
            results[index] = new BatchItemResult
            {
                Index = index,
                Status = StatusCodes.Status404NotFound,
                Error = Responses.ProblemDetailsFactory.NotFound(entityName, id!, context.HttpContext.Request.Path)
            };
            return;
        }

        results[index] = await RunAfterPersistAndBuildResultAsync(index, updated, id, context);
    }
}
