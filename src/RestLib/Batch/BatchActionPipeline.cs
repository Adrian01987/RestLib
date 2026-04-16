using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RestLib.Abstractions;
using RestLib.Configuration;
using RestLib.Endpoints;
using RestLib.Hooks;
using RestLib.Hypermedia;
using RestLib.Logging;
using RestLib.Responses;
using RestLib.Serialization;
using RestLib.Validation;

namespace RestLib.Batch;

/// <summary>
/// Abstract base class implementing the Template Method pattern for batch operations.
/// Defines the fixed algorithm skeleton (deserialize, validate, persist, build response)
/// and delegates action-specific steps to concrete subclasses.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
/// <typeparam name="TRawItem">The deserialized item type from JSON (e.g. TEntity, BatchUpdateItem, TKey).</typeparam>
/// <typeparam name="TValidItem">The validated item type passed to execution (e.g. (int, TEntity), (int, TKey, TEntity)).</typeparam>
internal abstract class BatchActionPipeline<TEntity, TKey, TRawItem, TValidItem>
    where TEntity : class
    where TKey : notnull
{
    // ── Properties ──────────────────────────────────────────────────────

    /// <summary>
    /// Gets the HTTP success status code for this action (e.g. 201 for create, 200 for update).
    /// </summary>
    protected abstract int SuccessStatusCode { get; }

    /// <summary>
    /// Gets the <see cref="RestLibOperation"/> for this batch action.
    /// </summary>
    protected abstract RestLibOperation Operation { get; }

    /// <summary>
    /// Gets the error message used when the items array cannot be deserialized.
    /// </summary>
    protected virtual string DeserializationErrorMessage =>
        "The 'items' array could not be deserialized.";

    /// <summary>
    /// Gets a value indicating whether this action supports a bulk persistence path
    /// via <see cref="IBatchRepository{TEntity, TKey}"/>. Defaults to <c>true</c>.
    /// </summary>
    protected virtual bool HasBulkPath => true;

    // ── Internal methods ────────────────────────────────────────────────

    /// <summary>
    /// Orchestrates the full batch processing pipeline: deserialize, validate, persist, build response.
    /// This is the Template Method.
    /// </summary>
    /// <param name="itemsElement">The raw JSON items array.</param>
    /// <param name="context">The batch context with shared services.</param>
    /// <returns>The batch response with per-item results.</returns>
    internal async Task<BatchResponse> ProcessAsync(
        JsonElement itemsElement,
        BatchContext<TEntity, TKey> context)
    {
        var items = JsonDeserializationHelper.DeserializeArray<TRawItem>(itemsElement, context.JsonOptions, context.Logger);
        if (items is null)
        {
            return SingleErrorResponse(0, StatusCodes.Status400BadRequest,
                ProblemDetailsFactory.BadRequest(
                    DeserializationErrorMessage,
                    context.HttpContext.Request.Path));
        }

        var results = new BatchItemResult?[items.Count];
        var validItems = new List<TValidItem>();

        for (var i = 0; i < items.Count; i++)
        {
            var (error, validItem) = await ValidateItemAsync(i, items[i], context);
            if (error is not null)
            {
                results[i] = error;
                continue;
            }

            validItems.Add(validItem!);
        }

        await ExecuteAsync(validItems, results, context);

        return new BatchResponse { Items = results.ToList()! };
    }

    // ── Protected static methods ────────────────────────────────────────

    /// <summary>
    /// Creates a batch response with a single error entry.
    /// </summary>
    /// <param name="index">The item index for the error.</param>
    /// <param name="status">The HTTP status code.</param>
    /// <param name="error">The problem details describing the error.</param>
    /// <returns>A batch response containing a single error item.</returns>
    protected static BatchResponse SingleErrorResponse(
        int index,
        int status,
        RestLibProblemDetails error)
    {
        return new BatchResponse
        {
            Items = [new BatchItemResult { Index = index, Status = status, Error = error }]
        };
    }

    /// <summary>
    /// Runs the three pre-persist hook stages (OnRequestReceived, OnRequestValidated,
    /// BeforePersist) and returns an error result if any stage short-circuits.
    /// </summary>
    /// <param name="index">The item index.</param>
    /// <param name="pipeline">The hook pipeline.</param>
    /// <param name="hookContext">The hook context.</param>
    /// <returns>An error result if short-circuited, or <c>null</c>.</returns>
    protected static async Task<BatchItemResult?> RunPrePersistHooksAsync(
        int index,
        HookPipeline<TEntity, TKey> pipeline,
        HookContext<TEntity, TKey> hookContext)
    {
        var received = await pipeline.ExecuteOnRequestReceivedAsync(hookContext);
        if (!received)
        {
            return HookShortCircuitResult(index, hookContext);
        }

        var validated = await pipeline.ExecuteOnRequestValidatedAsync(hookContext);
        if (!validated)
        {
            return HookShortCircuitResult(index, hookContext);
        }

        var before = await pipeline.ExecuteBeforePersistAsync(hookContext);
        if (!before)
        {
            return HookShortCircuitResult(index, hookContext);
        }

        return null;
    }

    /// <summary>
    /// Validates an entity using data annotations.
    /// Returns an error result if validation fails, or <c>null</c> if valid.
    /// </summary>
    /// <param name="index">The item index.</param>
    /// <param name="entity">The entity to validate.</param>
    /// <param name="context">The batch context.</param>
    /// <returns>An error result if validation fails, or <c>null</c>.</returns>
    protected static BatchItemResult? ValidateEntity(
        int index,
        TEntity entity,
        BatchContext<TEntity, TKey> context)
    {
        if (!context.Options.EnableValidation)
        {
            return null;
        }

        var validationResult = EntityValidator.Validate(entity, context.JsonOptions.PropertyNamingPolicy);
        if (!validationResult.IsValid)
        {
            return ValidationFailedResult(index, validationResult, context.HttpContext.Request.Path);
        }

        return null;
    }

    /// <summary>
    /// Creates a 400 Bad Request batch item result.
    /// </summary>
    /// <param name="index">The item index.</param>
    /// <param name="detail">The error detail message.</param>
    /// <param name="instance">The request path.</param>
    /// <returns>A bad request batch item result.</returns>
    protected static BatchItemResult BadRequestResult(int index, string detail, string? instance)
    {
        return new BatchItemResult
        {
            Index = index,
            Status = StatusCodes.Status400BadRequest,
            Error = ProblemDetailsFactory.BadRequest(detail, instance)
        };
    }

    /// <summary>
    /// Creates a 404 Not Found batch item result.
    /// </summary>
    /// <typeparam name="TId">The key type.</typeparam>
    /// <param name="index">The item index.</param>
    /// <param name="entityName">The entity type name.</param>
    /// <param name="id">The entity ID.</param>
    /// <param name="instance">The request path.</param>
    /// <returns>A not found batch item result.</returns>
    protected static BatchItemResult NotFoundResult<TId>(int index, string entityName, TId id, string? instance)
    {
        return new BatchItemResult
        {
            Index = index,
            Status = StatusCodes.Status404NotFound,
            Error = ProblemDetailsFactory.NotFound(entityName, id!, instance)
        };
    }

    /// <summary>
    /// Creates a 400 Validation Failed batch item result.
    /// </summary>
    /// <param name="index">The item index.</param>
    /// <param name="validationResult">The validation result with errors.</param>
    /// <param name="instance">The request path.</param>
    /// <returns>A validation failed batch item result.</returns>
    protected static BatchItemResult ValidationFailedResult(
        int index,
        EntityValidationResult validationResult,
        string? instance)
    {
        return new BatchItemResult
        {
            Index = index,
            Status = StatusCodes.Status400BadRequest,
            Error = ProblemDetailsFactory.ValidationFailed(
                validationResult.Errors,
                instance)
        };
    }

    /// <summary>
    /// Creates a batch item result from a hook short-circuit during validation.
    /// </summary>
    /// <param name="index">The item index.</param>
    /// <param name="hookContext">The hook context with the early result.</param>
    /// <returns>A batch item result reflecting the hook's short-circuit response.</returns>
    protected static BatchItemResult HookShortCircuitResult(
        int index,
        HookContext<TEntity, TKey> hookContext)
    {
        if (hookContext.EarlyResult is null)
        {
            return new BatchItemResult
            {
                Index = index,
                Status = StatusCodes.Status500InternalServerError,
                Error = ProblemDetailsFactory.InternalError(
                    detail: "The operation was short-circuited by a hook.")
            };
        }

        var statusCode = hookContext.EarlyResult is IStatusCodeHttpResult statusResult
            ? statusResult.StatusCode ?? StatusCodes.Status500InternalServerError
            : StatusCodes.Status500InternalServerError;

        var error = hookContext.EarlyResult is IValueHttpResult { Value: RestLibProblemDetails problem }
            ? problem
            : ProblemDetailsFactory.HookShortCircuit(statusCode);

        return new BatchItemResult
        {
            Index = index,
            Status = statusCode,
            Error = error
        };
    }

    /// <summary>
    /// Builds a <see cref="BatchItemResult"/> from a hook's early result after persist.
    /// </summary>
    /// <param name="index">The item index.</param>
    /// <param name="earlyResult">The early result set by the hook.</param>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <returns>A batch item result reflecting the hook's short-circuit response.</returns>
    protected static BatchItemResult BuildHookResultItem(
        int index,
        IResult? earlyResult,
        HttpContext httpContext)
    {
        var statusCode = earlyResult is IStatusCodeHttpResult statusResult
            ? statusResult.StatusCode ?? StatusCodes.Status500InternalServerError
            : StatusCodes.Status500InternalServerError;

        var error = earlyResult is IValueHttpResult { Value: RestLibProblemDetails problem }
            ? problem
            : ProblemDetailsFactory.InternalError(
                detail: "Hook short-circuited after persist.",
                instance: httpContext.Request.Path);

        return new BatchItemResult
        {
            Index = index,
            Status = statusCode,
            Error = error
        };
    }

    // ── Protected abstract methods ──────────────────────────────────────

    /// <summary>
    /// Validates a single deserialized item. Returns an error result or a validated item for persistence.
    /// </summary>
    /// <param name="index">The item index within the batch.</param>
    /// <param name="rawItem">The deserialized raw item.</param>
    /// <param name="context">The batch context.</param>
    /// <returns>A tuple of (error or null, validated item or default).</returns>
    protected abstract Task<(BatchItemResult? Error, TValidItem? ValidItem)> ValidateItemAsync(
        int index,
        TRawItem? rawItem,
        BatchContext<TEntity, TKey> context);

    /// <summary>
    /// Extracts the original index from a validated item.
    /// </summary>
    /// <param name="validItem">The validated item.</param>
    /// <returns>The zero-based index.</returns>
    protected abstract int GetIndex(TValidItem validItem);

    /// <summary>
    /// Persists a single validated item and populates the result.
    /// Each subclass implements this with its specific persistence logic,
    /// null-handling, and after-persist hooks.
    /// </summary>
    /// <param name="validItem">The validated item.</param>
    /// <param name="results">The results array to populate.</param>
    /// <param name="context">The batch context.</param>
    protected abstract Task PersistSingleItemAsync(
        TValidItem validItem,
        BatchItemResult?[] results,
        BatchContext<TEntity, TKey> context);

    // ── Protected virtual methods ───────────────────────────────────────

    /// <summary>
    /// Extracts the resource ID from a validated item for hook context, if applicable.
    /// Returns <c>default</c> if the action has no resource ID (e.g. create).
    /// </summary>
    /// <param name="validItem">The validated item.</param>
    /// <returns>The resource ID, or default.</returns>
    protected virtual TKey? GetResourceId(TValidItem validItem) => default;

    /// <summary>
    /// Extracts the entity from a validated item for error hook context, if applicable.
    /// </summary>
    /// <param name="validItem">The validated item.</param>
    /// <returns>The entity, or null.</returns>
    protected virtual TEntity? GetEntity(TValidItem validItem) => default;

    /// <summary>
    /// Persists all validated items using the bulk repository path.
    /// Subclasses override this to call the appropriate <c>XxxManyAsync</c> method.
    /// The default implementation falls back to individual persistence.
    /// </summary>
    /// <param name="validItems">The validated items.</param>
    /// <param name="results">The results array to populate.</param>
    /// <param name="context">The batch context.</param>
    protected virtual async Task PersistBulkAsync(
        List<TValidItem> validItems,
        BatchItemResult?[] results,
        BatchContext<TEntity, TKey> context) =>
            await PersistIndividuallyAsync(validItems, results, context);

    // ── Protected instance methods ──────────────────────────────────────

    /// <summary>
    /// Runs the AfterPersist hook for a persisted entity and builds the success result.
    /// When HATEOAS is enabled, injects <c>_links</c> into the entity.
    /// </summary>
    /// <param name="index">The item index.</param>
    /// <param name="entity">The persisted entity.</param>
    /// <param name="resourceId">The resource ID for hook context.</param>
    /// <param name="context">The batch context.</param>
    /// <returns>The batch item result.</returns>
    protected async Task<BatchItemResult> RunAfterPersistAndBuildResultAsync(
        int index,
        TEntity entity,
        TKey? resourceId,
        BatchContext<TEntity, TKey> context)
    {
        if (context.Pipeline is not null)
        {
            var afterContext = context.Pipeline.CreateContext(
                context.HttpContext, Operation,
                resourceId: resourceId, entity: entity);
            var shouldContinue = await context.Pipeline.ExecuteAfterPersistAsync(afterContext);
            if (!shouldContinue)
            {
                return BuildHookResultItem(index, afterContext.EarlyResult, context.HttpContext);
            }
        }

        // Inject HATEOAS links when enabled
        object resultEntity = entity;
        if (context.Options.EnableHateoas)
        {
            // Always extract the key from the persisted entity via KeySelector.
            // For Create actions, resourceId is default(TKey) which for value types
            // (e.g. Guid.Empty) is not null, so we cannot rely on null-coalescing.
            var entityKey = EntityKeyHelper.GetEntityKey(entity, context.EndpointConfig.KeySelector);
            if (entityKey is not null)
            {
                var customLinksProvider = context.HttpContext.RequestServices.GetService<IHateoasLinkProvider<TEntity, TKey>>();
                var customLinks = customLinksProvider?.GetLinks(entity, entityKey);
                var links = HateoasLinkBuilder.BuildEntityLinks(
                    context.HttpContext.Request, context.CollectionPath, entityKey, context.EndpointConfig, customLinks);
                resultEntity = HateoasHelper.EntityWithLinks<TEntity, TKey>(entity, links, context.JsonOptions);
            }
        }

        return new BatchItemResult
        {
            Index = index,
            Status = SuccessStatusCode,
            Entity = resultEntity
        };
    }

    /// <summary>
    /// Helper for subclass <see cref="PersistBulkAsync"/> overrides: processes bulk results
    /// by running AfterPersist hooks and building success results.
    /// </summary>
    /// <param name="validItems">The validated items that were persisted.</param>
    /// <param name="bulkResults">The entities returned from the bulk operation.</param>
    /// <param name="results">The results array to populate.</param>
    /// <param name="context">The batch context.</param>
    protected async Task ProcessBulkResultsAsync(
        List<TValidItem> validItems,
        IReadOnlyList<TEntity> bulkResults,
        BatchItemResult?[] results,
        BatchContext<TEntity, TKey> context)
    {
        for (var j = 0; j < validItems.Count; j++)
        {
            var index = GetIndex(validItems[j]);
            var entity = bulkResults[j];

            results[index] = await RunAfterPersistAndBuildResultAsync(
                index, entity, GetResourceId(validItems[j]), context);
        }
    }

    // ── Private static methods ──────────────────────────────────────────

    /// <summary>
    /// Creates a batch item result from an exception.
    /// </summary>
    private static BatchItemResult ExceptionResult(
        int index,
        Exception ex,
        string? instance,
        RestLibOptions options)
    {
        var detail = options.IncludeExceptionDetailsInErrors
            ? $"{ex.GetType().Name}: {ex.Message}"
            : "An internal error occurred while processing this item.";
        return new BatchItemResult
        {
            Index = index,
            Status = StatusCodes.Status500InternalServerError,
            Error = ProblemDetailsFactory.InternalError(detail: detail, instance: instance)
        };
    }

    private static bool IsConcurrencyException(Exception exception)
    {
        return exception.GetType().FullName == "Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException";
    }

    // ── Private instance methods ────────────────────────────────────────

    /// <summary>
    /// Chooses the bulk or individual persistence path based on availability and configuration.
    /// When the bulk path throws, falls back to individual persistence so that each item
    /// receives its own per-item error result instead of all items being blamed for one failure.
    /// </summary>
    private async Task ExecuteAsync(
        List<TValidItem> validItems,
        BatchItemResult?[] results,
        BatchContext<TEntity, TKey> context)
    {
        if (validItems.Count == 0)
        {
            return;
        }

        if (HasBulkPath && context.BatchRepository is not null)
        {
            try
            {
                await PersistBulkAsync(validItems, results, context);
            }
            catch (Exception bulkException)
            {
                if (IsConcurrencyException(bulkException))
                {
                    var failedItems = validItems
                        .Where(item => results[GetIndex(item)] is null)
                        .ToList();

                    foreach (var item in failedItems)
                    {
                        var index = GetIndex(item);
                        results[index] = await HandleItemErrorAsync(
                            index,
                            bulkException,
                            context,
                            GetResourceId(item),
                            GetEntity(item));
                    }

                    return;
                }

                // Bulk path failed — fall back to individual persistence for items that
                // don't already have a result. Some subclasses (e.g. BatchPatchPipeline)
                // may populate results during pre-validation inside PersistBulkAsync,
                // so we only retry items whose result slot is still empty.
                var actionName = Operation.ToString().ToLowerInvariant();
                RestLibLogMessages.BulkPersistenceFallback(
                    context.Logger, actionName, validItems.Count, bulkException);

                var remainingItems = validItems
                    .Where(item => results[GetIndex(item)] is null)
                    .ToList();

                await PersistIndividuallyAsync(remainingItems, results, context);
            }
        }
        else
        {
            await PersistIndividuallyAsync(validItems, results, context);
        }
    }

    /// <summary>
    /// Persists items one at a time with per-item error handling.
    /// </summary>
    private async Task PersistIndividuallyAsync(
        List<TValidItem> validItems,
        BatchItemResult?[] results,
        BatchContext<TEntity, TKey> context)
    {
        foreach (var item in validItems)
        {
            var index = GetIndex(item);
            try
            {
                await PersistSingleItemAsync(item, results, context);
            }
            catch (Exception ex)
            {
                var actionName = Operation.ToString().ToLowerInvariant();
                RestLibLogMessages.BatchItemPersistenceFailed(context.Logger, actionName, index, ex);

                results[index] = await HandleItemErrorAsync(
                    index, ex, context, GetResourceId(item), GetEntity(item));
            }
        }
    }

    /// <summary>
    /// Runs the OnError hook for a batch item exception. If the hook handles the error,
    /// returns a result from the hook; otherwise falls back to the default exception result.
    /// </summary>
    private async Task<BatchItemResult> HandleItemErrorAsync(
        int index,
        Exception ex,
        BatchContext<TEntity, TKey> context,
        TKey? resourceId = default,
        TEntity? entity = default)
    {
        if (context.Pipeline is not null)
        {
            try
            {
                var errorContext = context.Pipeline.CreateErrorContext(
                    context.HttpContext, Operation, ex, resourceId, entity);
                var (handled, errorResult) = await context.Pipeline.ExecuteOnErrorAsync(errorContext);

                if (handled && errorResult is not null)
                {
                    var statusCode = errorResult is IStatusCodeHttpResult statusResult
                        ? statusResult.StatusCode ?? StatusCodes.Status500InternalServerError
                        : StatusCodes.Status500InternalServerError;

                    var error = errorResult is IValueHttpResult { Value: RestLibProblemDetails problem }
                        ? problem
                        : ProblemDetailsFactory.InternalError(
                            detail: context.Options.IncludeExceptionDetailsInErrors
                                ? $"{ex.GetType().Name}: {ex.Message}"
                                : "An internal error occurred while processing this item.",
                            instance: context.HttpContext.Request.Path);

                    return new BatchItemResult
                    {
                        Index = index,
                        Status = statusCode,
                        Error = error
                    };
                }
            }
            catch (Exception hookException)
            {
                // If the error hook itself throws, swallow the hook exception and fall
                // through to the default ExceptionResult so the original error is reported.
                var actionName = Operation.ToString().ToLowerInvariant();
                RestLibLogMessages.BatchErrorHookSwallowed(
                    context.Logger, actionName, index, hookException);
            }
        }

        return ExceptionResult(index, ex, context.HttpContext.Request.Path, context.Options);
    }
}
