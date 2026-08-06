using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RestLib.Endpoints;
using RestLib.Hypermedia;
using RestLib.Responses;
using RestLib.Validation;

namespace RestLib.Batch;

/// <summary>
/// Adapts the common batch state machine to mapped API and persistence models.
/// </summary>
/// <typeparam name="TApiModel">The API model type.</typeparam>
/// <typeparam name="TDbModel">The persistence model type.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
/// <typeparam name="TRawItem">The deserialized request item type.</typeparam>
/// <typeparam name="TValidItem">The validated persistence item type.</typeparam>
internal abstract class MappedBatchActionPipeline<TApiModel, TDbModel, TKey, TRawItem, TValidItem>
    : BatchActionPipelineBase<TKey, TRawItem, TValidItem, MappedBatchContext<TApiModel, TDbModel, TKey>>
    where TApiModel : class
    where TDbModel : class
    where TKey : notnull
{
    /// <summary>
    /// Gets the success status code for the action.
    /// </summary>
    protected abstract int SuccessStatusCode { get; }

    /// <summary>
    /// Validates an API entity using the resource's configured rules.
    /// </summary>
    /// <param name="index">The item index.</param>
    /// <param name="apiEntity">The API entity.</param>
    /// <param name="context">The mapped batch context.</param>
    /// <returns>An error item when validation fails; otherwise, <c>null</c>.</returns>
    protected static BatchItemResult? ValidateApiEntity(
        int index,
        TApiModel apiEntity,
        MappedBatchContext<TApiModel, TDbModel, TKey> context)
    {
        if (!context.Options.EnableValidation)
        {
            return null;
        }

        var validationResult = RestLibResourceValidator.Validate(
            apiEntity,
            context.EndpointConfig,
            context.JsonOptions.PropertyNamingPolicy);
        return validationResult.IsValid
            ? null
            : ValidationFailedResult(index, validationResult, context.HttpContext.Request.Path);
    }

    /// <summary>
    /// Creates a not-found item result for the API resource.
    /// </summary>
    /// <typeparam name="TId">The ID type.</typeparam>
    /// <param name="index">The item index.</param>
    /// <param name="id">The missing ID.</param>
    /// <param name="instance">The request path.</param>
    /// <returns>The item result.</returns>
    protected static BatchItemResult NotFoundResult<TId>(int index, TId id, string? instance)
    {
        return new BatchItemResult
        {
            Index = index,
            Status = StatusCodes.Status404NotFound,
            Error = ProblemDetailsFactory.NotFound(typeof(TApiModel).Name, id!, instance)
        };
    }

    /// <summary>
    /// Preserves the configured persistence key after mapping or hook replacement.
    /// </summary>
    /// <param name="dbEntity">The persistence entity.</param>
    /// <param name="id">The resource ID.</param>
    /// <param name="context">The mapped batch context.</param>
    /// <returns><c>true</c> when the key was applied.</returns>
    protected static bool TrySetDbEntityKey(
        TDbModel dbEntity,
        TKey id,
        MappedBatchContext<TApiModel, TDbModel, TKey> context)
    {
        ArgumentNullException.ThrowIfNull(dbEntity);
        ArgumentNullException.ThrowIfNull(context);

        return EntityKeyHelper.TrySetEntityKeyParts(
            dbEntity,
            id,
            context.EndpointConfig.KeyRouteParts);
    }

    /// <summary>
    /// Extracts the API entity used by error hooks from a validated item.
    /// </summary>
    /// <param name="validItem">The validated item.</param>
    /// <returns>The API entity, when available.</returns>
    protected virtual TApiModel? GetApiEntity(TValidItem validItem) => default;

    /// <summary>
    /// Extracts the persistence entity used by error hooks from a validated item.
    /// </summary>
    /// <param name="validItem">The validated item.</param>
    /// <returns>The persistence entity, when available.</returns>
    protected virtual TDbModel? GetDbEntity(TValidItem validItem) => default;

    /// <summary>
    /// Runs the active after-persist hook and builds the API response entity.
    /// </summary>
    /// <param name="index">The item index.</param>
    /// <param name="dbEntity">The persisted entity.</param>
    /// <param name="resourceId">The resource ID.</param>
    /// <param name="context">The mapped batch context.</param>
    /// <param name="mappedApiEntity">A previously mapped API entity, when available.</param>
    /// <returns>The completed item result.</returns>
    protected async Task<BatchItemResult> RunAfterPersistAndBuildResultAsync(
        int index,
        TDbModel dbEntity,
        TKey? resourceId,
        MappedBatchContext<TApiModel, TDbModel, TKey> context,
        TApiModel? mappedApiEntity = null)
    {
        var apiEntity = mappedApiEntity ?? context.Mapper.ToApi(dbEntity);
        var entityKey = EntityKeyHelper.GetEntityKey(apiEntity, context.EndpointConfig.KeySelector);

        if (context.DbPipeline is not null)
        {
            var afterContext = context.DbPipeline.CreateContext(
                context.HttpContext,
                Operation,
                resourceId: resourceId,
                entity: dbEntity);
            var shouldContinue = await context.DbPipeline.ExecuteAfterPersistAsync(afterContext);
            if (!shouldContinue)
            {
                return BuildHookResultItem(index, afterContext.EarlyResult, context.HttpContext);
            }

            dbEntity = afterContext.Entity ?? dbEntity;
            if (entityKey is not null)
            {
                _ = EntityKeyHelper.TrySetEntityKeyParts(
                    dbEntity,
                    entityKey,
                    context.EndpointConfig.KeyRouteParts);
            }

            apiEntity = context.Mapper.ToApi(dbEntity);
        }
        else if (context.ApiPipeline is not null)
        {
            var afterContext = context.ApiPipeline.CreateContext(
                context.HttpContext,
                Operation,
                resourceId: resourceId,
                entity: apiEntity);
            var shouldContinue = await context.ApiPipeline.ExecuteAfterPersistAsync(afterContext);
            if (!shouldContinue)
            {
                return BuildHookResultItem(index, afterContext.EarlyResult, context.HttpContext);
            }

            apiEntity = afterContext.Entity ?? apiEntity;
        }

        if (entityKey is not null)
        {
            _ = EntityKeyHelper.TrySetEntityKeyParts(
                apiEntity,
                entityKey,
                context.EndpointConfig.KeyRouteParts);
        }

        object resultEntity = apiEntity;
        if (context.Options.EnableHateoas && entityKey is not null)
        {
            var customLinksProvider = context.HttpContext.RequestServices
                .GetService<IHateoasLinkProvider<TApiModel, TKey>>();
            var customLinks = customLinksProvider?.GetLinks(apiEntity, entityKey);
            var links = HateoasLinkBuilder.BuildEntityLinks(
                context.HttpContext.Request,
                context.CollectionPath,
                entityKey,
                context.EndpointConfig,
                customLinks);
            resultEntity = HateoasHelper.EntityWithLinks<TApiModel, TKey>(
                apiEntity,
                links,
                context.JsonOptions);
        }

        return new BatchItemResult
        {
            Index = index,
            Status = SuccessStatusCode,
            Entity = resultEntity
        };
    }

    /// <summary>
    /// Correlates bulk persistence results and builds API response items.
    /// </summary>
    /// <param name="validItems">The submitted valid items.</param>
    /// <param name="bulkResults">The persistence results.</param>
    /// <param name="results">The indexed response items.</param>
    /// <param name="context">The mapped batch context.</param>
    /// <param name="allowMissingResults">Whether omitted keyed results represent missing resources.</param>
    /// <param name="expectedResultKeys">Caller-supplied create keys by input position.</param>
    protected async Task ProcessBulkResultsAsync(
        List<TValidItem> validItems,
        IReadOnlyList<TDbModel> bulkResults,
        BatchItemResult?[] results,
        MappedBatchContext<TApiModel, TDbModel, TKey> context,
        bool allowMissingResults = false,
        IReadOnlyDictionary<int, TKey>? expectedResultKeys = null)
    {
        var actionName = Operation.ToString().ToLowerInvariant();
        BatchRepositoryResultContract.ValidateNonNull(bulkResults, actionName);

        IReadOnlyList<int?> correlations;
        IReadOnlyList<TApiModel?> mappedApiEntities = Array.Empty<TApiModel?>();

        if (allowMissingResults)
        {
            var submittedKeys = new List<TKey>(validItems.Count);
            foreach (var validItem in validItems)
            {
                var resourceId = GetResourceId(validItem);
                if (resourceId is null)
                {
                    throw new BatchRepositoryContractException(
                        $"The '{actionName}' mapped batch pipeline could not determine a submitted resource key.");
                }

                submittedKeys.Add(resourceId);
            }

            var apiEntities = bulkResults.Select(context.Mapper.ToApi).ToList();
            var returnedKeys = new List<TKey>(apiEntities.Count);
            foreach (var apiEntity in apiEntities)
            {
                if (!EntityKeyHelper.TryGetEntityKey(
                        apiEntity,
                        context.EndpointConfig.KeySelector,
                        out var key))
                {
                    throw new BatchRepositoryContractException(
                        $"The batch repository contract was violated for '{actionName}': " +
                        "a returned entity did not expose its API resource key.");
                }

                returnedKeys.Add(key);
            }

            correlations = BatchRepositoryResultContract.CorrelateByKey(
                submittedKeys,
                returnedKeys,
                actionName);
            mappedApiEntities = apiEntities;
        }
        else
        {
            BatchRepositoryResultContract.ValidateComplete(
                bulkResults,
                validItems.Count,
                actionName);
            if (expectedResultKeys is not null && expectedResultKeys.Count > 0)
            {
                var apiEntities = new TApiModel?[bulkResults.Count];
                foreach (var (resultIndex, expectedKey) in expectedResultKeys)
                {
                    var apiEntity = context.Mapper.ToApi(bulkResults[resultIndex]);
                    apiEntities[resultIndex] = apiEntity;
                    if (!EntityKeyHelper.TryGetEntityKey(
                            apiEntity,
                            context.EndpointConfig.KeySelector,
                            out var returnedKey) ||
                        !EqualityComparer<TKey>.Default.Equals(returnedKey, expectedKey))
                    {
                        throw new BatchRepositoryContractException(
                            $"The batch repository contract was violated for '{actionName}': " +
                            "a caller-supplied key was returned at a different position.");
                    }
                }

                mappedApiEntities = apiEntities;
            }

            correlations = Enumerable.Range(0, validItems.Count)
                .Select(static resultIndex => (int?)resultIndex)
                .ToArray();
        }

        for (var itemIndex = 0; itemIndex < validItems.Count; itemIndex++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var validItem = validItems[itemIndex];
            var originalIndex = GetIndex(validItem);
            var resultIndex = correlations[itemIndex];
            if (resultIndex is null)
            {
                results[originalIndex] = NotFoundResult(
                    originalIndex,
                    GetResourceId(validItem)!,
                    context.HttpContext.Request.Path);
                continue;
            }

            var dbEntity = bulkResults[resultIndex.Value];
            var apiEntity = resultIndex.Value < mappedApiEntities.Count
                ? mappedApiEntities[resultIndex.Value]
                : null;

            results[originalIndex] = await RunAfterPersistAndBuildResultAsync(
                originalIndex,
                dbEntity,
                GetResourceId(validItem),
                context,
                apiEntity);
        }
    }

    /// <inheritdoc/>
    protected override bool CanUseBulk(MappedBatchContext<TApiModel, TDbModel, TKey> context)
    {
        return context.BatchRepository is not null;
    }

    /// <inheritdoc/>
    protected override async Task<(bool Handled, IResult? Result)> ExecuteErrorHookAsync(
        TValidItem validItem,
        Exception exception,
        MappedBatchContext<TApiModel, TDbModel, TKey> context)
    {
        if (context.DbPipeline is not null)
        {
            var errorContext = context.DbPipeline.CreateErrorContext(
                context.HttpContext,
                Operation,
                exception,
                GetResourceId(validItem),
                GetDbEntity(validItem));
            return await context.DbPipeline.ExecuteOnErrorAsync(errorContext);
        }

        if (context.ApiPipeline is not null)
        {
            var errorContext = context.ApiPipeline.CreateErrorContext(
                context.HttpContext,
                Operation,
                exception,
                GetResourceId(validItem),
                GetApiEntity(validItem));
            return await context.ApiPipeline.ExecuteOnErrorAsync(errorContext);
        }

        return (false, null);
    }
}
