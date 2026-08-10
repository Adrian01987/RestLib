using System.Text.Json;
using Microsoft.AspNetCore.Http;
using RestLib.Abstractions;
using RestLib.Endpoints;
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
    protected override Task<(BatchItemResult? Error, (int Index, TKey Id, TEntity Entity) ValidItem)> ValidateBulkItemAsync(
        int index,
        BatchUpdateItem<TKey>? item,
        BatchContext<TEntity, TKey> context)
    {
        var (error, entity) = DeserializeBody(index, item, context);
        return Task.FromResult(error is not null
            ? (error, default((int Index, TKey Id, TEntity Entity)))
            : (null, (index, item!.Id, entity!)));
    }

    /// <inheritdoc/>
    protected override async Task<(BatchItemResult? Error, (int Index, TKey Id, TEntity Entity) ValidItem)> ValidateItemAsync(
        int index,
        BatchUpdateItem<TKey>? item,
        BatchContext<TEntity, TKey> context)
    {
        var (error, entity) = DeserializeBody(index, item, context);
        if (error is not null)
        {
            return (error, default);
        }

        var existing = await context.Repository.GetByIdAsync(item!.Id, context.CancellationToken);
        if (existing is null)
        {
            var entityName = typeof(TEntity).Name;
            return (NotFoundResult(index, entityName, item.Id!, context.HttpContext.Request.Path, context.EndpointConfig.KeyRouteParts), default);
        }

        var (validationError, validatedEntity) = await ValidateWithOriginalAsync(
            index,
            item.Id,
            entity!,
            existing,
            context);
        return validationError is not null
            ? (validationError, default)
            : (null, (index, item.Id, validatedEntity!));
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
        var ids = validItems.Select(static item => item.Id).ToList();
        var originals = await BulkPersistenceExecutor.ExecuteAsync(
            () => context.BatchRepository!.GetByIdsAsync(ids, context.CancellationToken),
            context.CancellationToken);
        BatchRepositoryResultContract.ValidateLookup(
            originals,
            ids,
            entity => EntityKeyHelper.GetEntityKey(
                entity,
                context.EndpointConfig.KeySelector),
            "update");
        var itemsToPersist = new List<(int Index, TKey Id, TEntity Entity)>();

        for (var itemPosition = 0; itemPosition < validItems.Count; itemPosition++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var (index, id, entity) = validItems[itemPosition];
            if (!originals.TryGetValue(id, out var existing))
            {
                results[index] = NotFoundResult(
                    index,
                    typeof(TEntity).Name,
                    id,
                    context.HttpContext.Request.Path,
                    context.EndpointConfig.KeyRouteParts);
                continue;
            }

            var (error, validatedEntity) = await ValidateWithOriginalAsync(
                index,
                id,
                entity,
                existing,
                context);
            if (error is not null)
            {
                results[index] = error;
                continue;
            }

            var itemToPersist = (index, id, validatedEntity!);
            validItems[itemPosition] = itemToPersist;
            itemsToPersist.Add(itemToPersist);
        }

        if (itemsToPersist.Count == 0)
        {
            return;
        }

        var entities = itemsToPersist.Select(static item => item.Entity).ToList();
        var updated = await BulkPersistenceExecutor.ExecuteAsync(
            () => context.BatchRepository!.UpdateManyAsync(entities, context.CancellationToken),
            context.CancellationToken);

        await ProcessBulkResultsAsync(
            itemsToPersist,
            updated,
            results,
            context,
            allowMissingResults: true);
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
                Error = Responses.ProblemDetailsFactory.NotFound(
                    entityName,
                    id!,
                    context.EndpointConfig.KeyRouteParts,
                    context.HttpContext.Request.Path)
            };
            return;
        }

        results[index] = await RunAfterPersistAndBuildResultAsync(index, updated, id, context);
    }

    private (BatchItemResult? Error, TEntity? Entity) DeserializeBody(
        int index,
        BatchUpdateItem<TKey>? item,
        BatchContext<TEntity, TKey> context)
    {
        if (item is null)
        {
            return (BadRequestResult(
                index,
                $"Item at index {index} could not be deserialized.",
                context.HttpContext.Request.Path), null);
        }

        TEntity? entity;
        try
        {
            entity = item.Body.Deserialize<TEntity>(context.JsonOptions);
        }
        catch (JsonException exception)
        {
            RestLibLogMessages.BatchUpdateItemDeserializationFailed(
                context.Logger,
                index,
                exception);
            return (BadRequestResult(
                index,
                $"Item at index {index} has an invalid body.",
                context.HttpContext.Request.Path), null);
        }

        return entity is null
            ? (BadRequestResult(
                index,
                $"Item at index {index} body deserialized to null.",
                context.HttpContext.Request.Path), null)
            : (null, entity);
    }

    private async Task<(BatchItemResult? Error, TEntity? Entity)> ValidateWithOriginalAsync(
        int index,
        TKey id,
        TEntity entity,
        TEntity existing,
        BatchContext<TEntity, TKey> context)
    {
        _ = EntityKeyHelper.TrySetEntityKeyParts(
            entity,
            id,
            context.EndpointConfig.KeyRouteParts);

        var validationError = ValidateEntity(index, entity, context);
        if (validationError is not null)
        {
            return (validationError, null);
        }

        if (context.Pipeline is not null)
        {
            var hookContext = context.Pipeline.CreateContext(
                context.HttpContext,
                RestLibOperation.BatchUpdate,
                resourceId: id,
                entity: entity,
                originalEntity: existing);

            var hookError = await RunPrePersistHooksAsync(index, context.Pipeline, hookContext);
            if (hookError is not null)
            {
                return (hookError, null);
            }

            entity = hookContext.Entity ?? entity;
        }

        _ = EntityKeyHelper.TrySetEntityKeyParts(
            entity,
            id,
            context.EndpointConfig.KeyRouteParts);
        return (null, entity);
    }
}
