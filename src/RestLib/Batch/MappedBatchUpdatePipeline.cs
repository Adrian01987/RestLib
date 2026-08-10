using System.Text.Json;
using Microsoft.AspNetCore.Http;
using RestLib.Endpoints;
using RestLib.Logging;

namespace RestLib.Batch;

/// <summary>
/// Batch update pipeline for mapped API and DB models.
/// </summary>
/// <typeparam name="TApiModel">The API model type.</typeparam>
/// <typeparam name="TDbModel">The DB model type.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
internal sealed class MappedBatchUpdatePipeline<TApiModel, TDbModel, TKey>
    : MappedBatchActionPipeline<TApiModel, TDbModel, TKey, BatchUpdateItem<TKey>, (int Index, TKey Id, TApiModel ApiEntity, TDbModel? DbEntity)>
    where TApiModel : class
    where TDbModel : class
    where TKey : notnull
{
    /// <inheritdoc/>
    protected override int SuccessStatusCode => StatusCodes.Status200OK;

    /// <inheritdoc/>
    protected override RestLibOperation Operation => RestLibOperation.BatchUpdate;

    /// <inheritdoc/>
    protected override Task<(BatchItemResult? Error, (int Index, TKey Id, TApiModel ApiEntity, TDbModel? DbEntity) ValidItem)> ValidateBulkItemAsync(
        int index,
        BatchUpdateItem<TKey>? item,
        MappedBatchContext<TApiModel, TDbModel, TKey> context)
    {
        var (error, apiEntity) = DeserializeBody(index, item, context);
        return Task.FromResult(error is not null
            ? (error, default((int Index, TKey Id, TApiModel ApiEntity, TDbModel? DbEntity)))
            : (null, (index, item!.Id, apiEntity!, null)));
    }

    /// <inheritdoc/>
    protected override async Task<(BatchItemResult? Error, (int Index, TKey Id, TApiModel ApiEntity, TDbModel? DbEntity) ValidItem)> ValidateItemAsync(
        int index,
        BatchUpdateItem<TKey>? item,
        MappedBatchContext<TApiModel, TDbModel, TKey> context)
    {
        var (error, apiEntity) = DeserializeBody(index, item, context);
        if (error is not null)
        {
            return (error, default);
        }

        var existingDb = await context.Repository.GetByIdAsync(item!.Id, context.CancellationToken);
        if (existingDb is null)
        {
            return (NotFoundResult(index, item.Id!, context.HttpContext.Request.Path), default);
        }

        return await ValidateWithOriginalAsync(
            index,
            item.Id,
            apiEntity!,
            existingDb,
            context);
    }

    /// <inheritdoc/>
    protected override int GetIndex((int Index, TKey Id, TApiModel ApiEntity, TDbModel? DbEntity) validItem) => validItem.Index;

    /// <inheritdoc/>
    protected override TKey? GetResourceId((int Index, TKey Id, TApiModel ApiEntity, TDbModel? DbEntity) validItem) => validItem.Id;

    /// <inheritdoc/>
    protected override TApiModel? GetApiEntity((int Index, TKey Id, TApiModel ApiEntity, TDbModel? DbEntity) validItem) => validItem.ApiEntity;

    /// <inheritdoc/>
    protected override TDbModel? GetDbEntity((int Index, TKey Id, TApiModel ApiEntity, TDbModel? DbEntity) validItem) => validItem.DbEntity;

    /// <inheritdoc/>
    protected override async Task PersistBulkAsync(
        List<(int Index, TKey Id, TApiModel ApiEntity, TDbModel? DbEntity)> validItems,
        BatchItemResult?[] results,
        MappedBatchContext<TApiModel, TDbModel, TKey> context)
    {
        var ids = validItems.Select(static item => item.Id).ToList();
        var originals = await BulkPersistenceExecutor.ExecuteAsync(
            () => context.BatchRepository!.GetByIdsAsync(ids, context.CancellationToken),
            context.CancellationToken);
        BatchRepositoryResultContract.ValidateLookup(
            originals,
            ids,
            entity => EntityKeyHelper.GetEntityKey(
                context.Mapper.ToApi(entity),
                context.EndpointConfig.KeySelector),
            "update");
        var itemsToPersist = new List<(
            int Index,
            TKey Id,
            TApiModel ApiEntity,
            TDbModel? DbEntity)>();

        for (var itemPosition = 0; itemPosition < validItems.Count; itemPosition++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var candidate = validItems[itemPosition];
            if (!originals.TryGetValue(candidate.Id, out var existingDb))
            {
                results[candidate.Index] = NotFoundResult(
                    candidate.Index,
                    candidate.Id,
                    context.HttpContext.Request.Path);
                continue;
            }

            var (error, itemToPersist) = await ValidateWithOriginalAsync(
                candidate.Index,
                candidate.Id,
                candidate.ApiEntity,
                existingDb,
                context);
            if (error is not null)
            {
                results[candidate.Index] = error;
                continue;
            }

            validItems[itemPosition] = itemToPersist;
            itemsToPersist.Add(itemToPersist);
        }

        if (itemsToPersist.Count == 0)
        {
            return;
        }

        var entities = itemsToPersist.Select(static item => item.DbEntity!).ToList();
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
        (int Index, TKey Id, TApiModel ApiEntity, TDbModel? DbEntity) validItem,
        BatchItemResult?[] results,
        MappedBatchContext<TApiModel, TDbModel, TKey> context)
    {
        var (index, id, _, dbEntity) = validItem;
        var updated = await context.Repository.UpdateAsync(id, dbEntity!, context.CancellationToken);
        if (updated is null)
        {
            results[index] = NotFoundResult(index, id!, context.HttpContext.Request.Path);
            return;
        }

        results[index] = await RunAfterPersistAndBuildResultAsync(index, updated, id, context);
    }

    private (BatchItemResult? Error, TApiModel? ApiEntity) DeserializeBody(
        int index,
        BatchUpdateItem<TKey>? item,
        MappedBatchContext<TApiModel, TDbModel, TKey> context)
    {
        if (item is null)
        {
            return (BadRequestResult(
                index,
                $"Item at index {index} could not be deserialized.",
                context.HttpContext.Request.Path), null);
        }

        TApiModel? apiEntity;
        try
        {
            apiEntity = item.Body.Deserialize<TApiModel>(context.JsonOptions);
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

        return apiEntity is null
            ? (BadRequestResult(
                index,
                $"Item at index {index} body deserialized to null.",
                context.HttpContext.Request.Path), null)
            : (null, apiEntity);
    }

    private async Task<(
        BatchItemResult? Error,
        (int Index, TKey Id, TApiModel ApiEntity, TDbModel? DbEntity) ValidItem)>
        ValidateWithOriginalAsync(
            int index,
            TKey id,
            TApiModel apiEntity,
            TDbModel existingDb,
            MappedBatchContext<TApiModel, TDbModel, TKey> context)
    {
        if (context.DbPipeline is not null)
        {
            var dbEntity = context.Mapper.ToDb(apiEntity);
            _ = TrySetDbEntityKey(dbEntity, id, context);
            var hookContext = context.DbPipeline.CreateContext(
                context.HttpContext,
                RestLibOperation.BatchUpdate,
                resourceId: id,
                entity: dbEntity,
                originalEntity: existingDb);

            var received = await context.DbPipeline.ExecuteOnRequestReceivedAsync(hookContext);
            if (!received)
            {
                return (HookShortCircuitResult(index, hookContext), default);
            }

            dbEntity = hookContext.Entity ?? dbEntity;
            _ = TrySetDbEntityKey(dbEntity, id, context);
            apiEntity = context.Mapper.ToApi(dbEntity);

            var validationError = ValidateApiEntity(index, apiEntity, context);
            if (validationError is not null)
            {
                return (validationError, default);
            }

            hookContext.Entity = dbEntity;
            var validated = await context.DbPipeline.ExecuteOnRequestValidatedAsync(hookContext);
            if (!validated)
            {
                return (HookShortCircuitResult(index, hookContext), default);
            }

            dbEntity = hookContext.Entity ?? dbEntity;
            _ = TrySetDbEntityKey(dbEntity, id, context);
            apiEntity = context.Mapper.ToApi(dbEntity);

            validationError = ValidateApiEntity(index, apiEntity, context);
            if (validationError is not null)
            {
                return (validationError, default);
            }

            hookContext.Entity = dbEntity;
            var hookError = await RunBeforePersistHookAsync(
                index,
                context.DbPipeline,
                hookContext);
            if (hookError is not null)
            {
                return (hookError, default);
            }

            dbEntity = hookContext.Entity ?? dbEntity;
            apiEntity = context.Mapper.ToApi(dbEntity);
            _ = TrySetDbEntityKey(dbEntity, id, context);
            return (null, (index, id, apiEntity, dbEntity));
        }

        if (context.ApiPipeline is not null)
        {
            var existingApi = context.Mapper.ToApi(existingDb);
            var hookContext = context.ApiPipeline.CreateContext(
                context.HttpContext,
                RestLibOperation.BatchUpdate,
                resourceId: id,
                entity: apiEntity,
                originalEntity: existingApi);

            var received = await context.ApiPipeline.ExecuteOnRequestReceivedAsync(hookContext);
            if (!received)
            {
                return (HookShortCircuitResult(index, hookContext), default);
            }

            apiEntity = hookContext.Entity ?? apiEntity;
            var dbEntity = context.Mapper.ToDb(apiEntity);
            _ = TrySetDbEntityKey(dbEntity, id, context);
            apiEntity = context.Mapper.ToApi(dbEntity);

            var validationError = ValidateApiEntity(index, apiEntity, context);
            if (validationError is not null)
            {
                return (validationError, default);
            }

            hookContext.Entity = apiEntity;
            hookContext.SetOriginalEntity(existingApi);
            var validated = await context.ApiPipeline.ExecuteOnRequestValidatedAsync(hookContext);
            if (!validated)
            {
                return (HookShortCircuitResult(index, hookContext), default);
            }

            apiEntity = hookContext.Entity ?? apiEntity;
            validationError = ValidateApiEntity(index, apiEntity, context);
            if (validationError is not null)
            {
                return (validationError, default);
            }

            hookContext.Entity = apiEntity;
            hookContext.SetOriginalEntity(existingApi);
            var hookError = await RunBeforePersistHookAsync(
                index,
                context.ApiPipeline,
                hookContext);
            if (hookError is not null)
            {
                return (hookError, default);
            }

            apiEntity = hookContext.Entity ?? apiEntity;
            dbEntity = context.Mapper.ToDb(apiEntity);
            _ = TrySetDbEntityKey(dbEntity, id, context);
            return (null, (index, id, apiEntity, dbEntity));
        }

        var directDbEntity = context.Mapper.ToDb(apiEntity);
        _ = TrySetDbEntityKey(directDbEntity, id, context);
        apiEntity = context.Mapper.ToApi(directDbEntity);

        var directValidationError = ValidateApiEntity(index, apiEntity, context);
        return directValidationError is not null
            ? (directValidationError, default)
            : (null, (index, id, apiEntity, directDbEntity));
    }
}
