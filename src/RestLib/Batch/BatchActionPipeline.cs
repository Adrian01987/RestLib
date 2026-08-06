using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RestLib.Endpoints;
using RestLib.Hypermedia;
using RestLib.Responses;
using RestLib.Validation;

namespace RestLib.Batch;

/// <summary>
/// Adapts the common batch state machine to a single entity model.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
/// <typeparam name="TRawItem">The deserialized request item type.</typeparam>
/// <typeparam name="TValidItem">The validated persistence item type.</typeparam>
internal abstract class BatchActionPipeline<TEntity, TKey, TRawItem, TValidItem>
    : BatchActionPipelineBase<TKey, TRawItem, TValidItem, BatchContext<TEntity, TKey>>
    where TEntity : class
    where TKey : notnull
{
    /// <summary>
    /// Gets the success status code for the action.
    /// </summary>
    protected abstract int SuccessStatusCode { get; }

    /// <summary>
    /// Validates an entity using the resource's configured rules.
    /// </summary>
    /// <param name="index">The item index.</param>
    /// <param name="entity">The entity to validate.</param>
    /// <param name="context">The batch context.</param>
    /// <returns>An error item when validation fails; otherwise, <c>null</c>.</returns>
    protected static BatchItemResult? ValidateEntity(
        int index,
        TEntity entity,
        BatchContext<TEntity, TKey> context)
    {
        if (!context.Options.EnableValidation)
        {
            return null;
        }

        var validationResult = RestLibResourceValidator.Validate(
            entity,
            context.EndpointConfig,
            context.JsonOptions.PropertyNamingPolicy);
        return validationResult.IsValid
            ? null
            : ValidationFailedResult(index, validationResult, context.HttpContext.Request.Path);
    }

    /// <summary>
    /// Creates a not-found item result for the configured resource.
    /// </summary>
    /// <typeparam name="TId">The ID type.</typeparam>
    /// <param name="index">The item index.</param>
    /// <param name="entityName">The entity name.</param>
    /// <param name="id">The missing ID.</param>
    /// <param name="instance">The request path.</param>
    /// <param name="keyRouteParts">The configured key route metadata.</param>
    /// <returns>The item result.</returns>
    protected static BatchItemResult NotFoundResult<TId>(
        int index,
        string entityName,
        TId id,
        string? instance,
        IReadOnlyList<Configuration.RestLibKeyRoutePart<TId>> keyRouteParts)
        where TId : notnull
    {
        return new BatchItemResult
        {
            Index = index,
            Status = StatusCodes.Status404NotFound,
            Error = ProblemDetailsFactory.NotFound(entityName, id, keyRouteParts, instance)
        };
    }

    /// <summary>
    /// Extracts the entity used by error hooks from a validated item.
    /// </summary>
    /// <param name="validItem">The validated item.</param>
    /// <returns>The entity, when available.</returns>
    protected virtual TEntity? GetEntity(TValidItem validItem) => default;

    /// <summary>
    /// Runs the after-persist hook and builds a response entity.
    /// </summary>
    /// <param name="index">The item index.</param>
    /// <param name="entity">The persisted entity.</param>
    /// <param name="resourceId">The resource ID.</param>
    /// <param name="context">The batch context.</param>
    /// <returns>The completed item result.</returns>
    protected async Task<BatchItemResult> RunAfterPersistAndBuildResultAsync(
        int index,
        TEntity entity,
        TKey? resourceId,
        BatchContext<TEntity, TKey> context)
    {
        var entityKey = EntityKeyHelper.GetEntityKey(entity, context.EndpointConfig.KeySelector);

        if (context.Pipeline is not null)
        {
            var afterContext = context.Pipeline.CreateContext(
                context.HttpContext,
                Operation,
                resourceId: resourceId,
                entity: entity);
            var shouldContinue = await context.Pipeline.ExecuteAfterPersistAsync(afterContext);
            if (!shouldContinue)
            {
                return BuildHookResultItem(index, afterContext.EarlyResult, context.HttpContext);
            }

            entity = afterContext.Entity ?? entity;
            if (entityKey is not null)
            {
                _ = EntityKeyHelper.TrySetEntityKeyParts(
                    entity,
                    entityKey,
                    context.EndpointConfig.KeyRouteParts);
            }
        }

        object resultEntity = entity;
        if (context.Options.EnableHateoas && entityKey is not null)
        {
            var customLinksProvider = context.HttpContext.RequestServices
                .GetService<IHateoasLinkProvider<TEntity, TKey>>();
            var customLinks = customLinksProvider?.GetLinks(entity, entityKey);
            var links = HateoasLinkBuilder.BuildEntityLinks(
                context.HttpContext.Request,
                context.CollectionPath,
                entityKey,
                context.EndpointConfig,
                customLinks);
            resultEntity = HateoasHelper.EntityWithLinks<TEntity, TKey>(
                entity,
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
    /// Correlates bulk repository results and builds response items.
    /// </summary>
    /// <param name="validItems">The submitted valid items.</param>
    /// <param name="bulkResults">The repository results.</param>
    /// <param name="results">The indexed response items.</param>
    /// <param name="context">The batch context.</param>
    /// <param name="allowMissingResults">Whether omitted keyed results represent missing resources.</param>
    /// <param name="expectedResultKeys">Caller-supplied create keys by input position.</param>
    protected async Task ProcessBulkResultsAsync(
        List<TValidItem> validItems,
        IReadOnlyList<TEntity> bulkResults,
        BatchItemResult?[] results,
        BatchContext<TEntity, TKey> context,
        bool allowMissingResults = false,
        IReadOnlyDictionary<int, TKey>? expectedResultKeys = null)
    {
        var actionName = Operation.ToString().ToLowerInvariant();
        IReadOnlyList<int?> correlations;

        if (allowMissingResults)
        {
            BatchRepositoryResultContract.ValidateNonNull(bulkResults, actionName);

            var submittedKeys = new List<TKey>(validItems.Count);
            foreach (var validItem in validItems)
            {
                var resourceId = GetResourceId(validItem);
                if (resourceId is null)
                {
                    throw new BatchRepositoryContractException(
                        $"The '{actionName}' batch pipeline could not determine a submitted resource key.");
                }

                submittedKeys.Add(resourceId);
            }

            var returnedKeys = new List<TKey>(bulkResults.Count);
            foreach (var entity in bulkResults)
            {
                if (!EntityKeyHelper.TryGetEntityKey(
                        entity,
                        context.EndpointConfig.KeySelector,
                        out var key))
                {
                    throw new BatchRepositoryContractException(
                        $"The batch repository contract was violated for '{actionName}': " +
                        "a returned entity did not expose its resource key.");
                }

                returnedKeys.Add(key);
            }

            correlations = BatchRepositoryResultContract.CorrelateByKey(
                submittedKeys,
                returnedKeys,
                actionName);
        }
        else
        {
            BatchRepositoryResultContract.ValidateComplete(
                bulkResults,
                validItems.Count,
                actionName);
            if (expectedResultKeys is not null)
            {
                foreach (var (resultIndex, expectedKey) in expectedResultKeys)
                {
                    var entity = bulkResults[resultIndex];
                    if (!EntityKeyHelper.TryGetEntityKey(
                            entity,
                            context.EndpointConfig.KeySelector,
                            out var returnedKey) ||
                        !EqualityComparer<TKey>.Default.Equals(returnedKey, expectedKey))
                    {
                        throw new BatchRepositoryContractException(
                            $"The batch repository contract was violated for '{actionName}': " +
                            "a caller-supplied key was returned at a different position.");
                    }
                }
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
                var resourceId = GetResourceId(validItem)!;
                results[originalIndex] = NotFoundResult(
                    originalIndex,
                    typeof(TEntity).Name,
                    resourceId,
                    context.HttpContext.Request.Path,
                    context.EndpointConfig.KeyRouteParts);
                continue;
            }

            var entity = bulkResults[resultIndex.Value];
            results[originalIndex] = await RunAfterPersistAndBuildResultAsync(
                originalIndex,
                entity,
                GetResourceId(validItem),
                context);
        }
    }

    /// <inheritdoc/>
    protected override bool CanUseBulk(BatchContext<TEntity, TKey> context)
    {
        return context.BatchRepository is not null;
    }

    /// <inheritdoc/>
    protected override async Task<(bool Handled, IResult? Result)> ExecuteErrorHookAsync(
        TValidItem validItem,
        Exception exception,
        BatchContext<TEntity, TKey> context)
    {
        if (context.Pipeline is null)
        {
            return (false, null);
        }

        var errorContext = context.Pipeline.CreateErrorContext(
            context.HttpContext,
            Operation,
            exception,
            GetResourceId(validItem),
            GetEntity(validItem));
        return await context.Pipeline.ExecuteOnErrorAsync(errorContext);
    }
}
