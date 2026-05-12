using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
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
/// Abstract base class implementing the template method for mapped batch operations.
/// </summary>
/// <typeparam name="TApiModel">The API model type.</typeparam>
/// <typeparam name="TDbModel">The DB model type.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
/// <typeparam name="TRawItem">The deserialized item type from JSON.</typeparam>
/// <typeparam name="TValidItem">The validated item type passed to persistence.</typeparam>
internal abstract class MappedBatchActionPipeline<TApiModel, TDbModel, TKey, TRawItem, TValidItem>
    where TApiModel : class
    where TDbModel : class
    where TKey : notnull
{
    /// <summary>
    /// Gets the HTTP success status code for this action.
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
    /// Gets a value indicating whether this action supports a bulk persistence path.
    /// </summary>
    protected virtual bool HasBulkPath => true;

    /// <summary>
    /// Orchestrates the full mapped batch processing pipeline.
    /// </summary>
    /// <param name="itemsElement">The raw JSON items array.</param>
    /// <param name="context">The mapped batch context.</param>
    /// <returns>The batch response with per-item results.</returns>
    internal async Task<BatchResponse> ProcessAsync(
        JsonElement itemsElement,
        MappedBatchContext<TApiModel, TDbModel, TKey> context)
    {
        var items = JsonDeserializationHelper.DeserializeArray<TRawItem>(itemsElement, context.JsonOptions, context.Logger);
        if (items is null)
        {
            return SingleErrorResponse(
                0,
                StatusCodes.Status400BadRequest,
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
    /// Runs the request hook stages and returns an error result if any stage short-circuits.
    /// </summary>
    /// <typeparam name="THookModel">The hook model type.</typeparam>
    /// <param name="index">The item index.</param>
    /// <param name="pipeline">The hook pipeline.</param>
    /// <param name="hookContext">The hook context.</param>
    /// <returns>An error result if short-circuited, or <c>null</c>.</returns>
    protected static async Task<BatchItemResult?> RunRequestHooksAsync<THookModel>(
        int index,
        HookPipeline<THookModel, TKey> pipeline,
        HookContext<THookModel, TKey> hookContext)
        where THookModel : class
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

        return null;
    }

    /// <summary>
    /// Runs the before-persist hook stage and returns an error result if it short-circuits.
    /// </summary>
    /// <typeparam name="THookModel">The hook model type.</typeparam>
    /// <param name="index">The item index.</param>
    /// <param name="pipeline">The hook pipeline.</param>
    /// <param name="hookContext">The hook context.</param>
    /// <returns>An error result if short-circuited, or <c>null</c>.</returns>
    protected static async Task<BatchItemResult?> RunBeforePersistHookAsync<THookModel>(
        int index,
        HookPipeline<THookModel, TKey> pipeline,
        HookContext<THookModel, TKey> hookContext)
        where THookModel : class
    {
        var before = await pipeline.ExecuteBeforePersistAsync(hookContext);
        if (!before)
        {
            return HookShortCircuitResult(index, hookContext);
        }

        return null;
    }

    /// <summary>
    /// Runs the request and before-persist hook stages.
    /// </summary>
    /// <typeparam name="THookModel">The hook model type.</typeparam>
    /// <param name="index">The item index.</param>
    /// <param name="pipeline">The hook pipeline.</param>
    /// <param name="hookContext">The hook context.</param>
    /// <returns>An error result if short-circuited, or <c>null</c>.</returns>
    protected static async Task<BatchItemResult?> RunPrePersistHooksAsync<THookModel>(
        int index,
        HookPipeline<THookModel, TKey> pipeline,
        HookContext<THookModel, TKey> hookContext)
        where THookModel : class
    {
        var requestHookError = await RunRequestHooksAsync(index, pipeline, hookContext);
        if (requestHookError is not null)
        {
            return requestHookError;
        }

        return await RunBeforePersistHookAsync(index, pipeline, hookContext);
    }

    /// <summary>
    /// Validates an API entity using data annotations and JSON-declared rules.
    /// </summary>
    /// <param name="index">The item index.</param>
    /// <param name="apiEntity">The API entity to validate.</param>
    /// <param name="context">The mapped batch context.</param>
    /// <returns>An error result if validation fails, or <c>null</c>.</returns>
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
    /// <param name="id">The entity ID.</param>
    /// <param name="instance">The request path.</param>
    /// <returns>A not found batch item result.</returns>
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
            Error = ProblemDetailsFactory.ValidationFailed(validationResult.Errors, instance)
        };
    }

    /// <summary>
    /// Preserves the configured DB key property on a mapped model when one can be
    /// identified.
    /// </summary>
    /// <param name="dbEntity">The DB model instance.</param>
    /// <param name="id">The resource ID.</param>
    /// <param name="context">The mapped batch context.</param>
    protected static bool TrySetDbEntityKey(
        TDbModel dbEntity,
        TKey id,
        MappedBatchContext<TApiModel, TDbModel, TKey> context)
    {
        ArgumentNullException.ThrowIfNull(dbEntity);
        ArgumentNullException.ThrowIfNull(context);

        return EntityKeyHelper.TrySetEntityKeyParts(dbEntity, id, context.EndpointConfig.KeyRouteParts);
    }

    /// <summary>
    /// Creates a batch item result from a hook short-circuit during validation.
    /// </summary>
    /// <typeparam name="THookModel">The hook model type.</typeparam>
    /// <param name="index">The item index.</param>
    /// <param name="hookContext">The hook context with the early result.</param>
    /// <returns>A batch item result reflecting the hook's short-circuit response.</returns>
    protected static BatchItemResult HookShortCircuitResult<THookModel>(
        int index,
        HookContext<THookModel, TKey> hookContext)
        where THookModel : class
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
    /// Builds a batch item result from a hook's early result after persist.
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

    /// <summary>
    /// Validates a single deserialized item.
    /// </summary>
    /// <param name="index">The item index within the batch.</param>
    /// <param name="rawItem">The deserialized raw item.</param>
    /// <param name="context">The mapped batch context.</param>
    /// <returns>A tuple of (error or null, validated item or default).</returns>
    protected abstract Task<(BatchItemResult? Error, TValidItem? ValidItem)> ValidateItemAsync(
        int index,
        TRawItem? rawItem,
        MappedBatchContext<TApiModel, TDbModel, TKey> context);

    /// <summary>
    /// Extracts the original index from a validated item.
    /// </summary>
    /// <param name="validItem">The validated item.</param>
    /// <returns>The zero-based index.</returns>
    protected abstract int GetIndex(TValidItem validItem);

    /// <summary>
    /// Persists a single validated item and populates the result.
    /// </summary>
    /// <param name="validItem">The validated item.</param>
    /// <param name="results">The results array to populate.</param>
    /// <param name="context">The mapped batch context.</param>
    protected abstract Task PersistSingleItemAsync(
        TValidItem validItem,
        BatchItemResult?[] results,
        MappedBatchContext<TApiModel, TDbModel, TKey> context);

    /// <summary>
    /// Extracts the resource ID from a validated item for hook context, if applicable.
    /// </summary>
    /// <param name="validItem">The validated item.</param>
    /// <returns>The resource ID, or default.</returns>
    protected virtual TKey? GetResourceId(TValidItem validItem) => default;

    /// <summary>
    /// Extracts the API entity from a validated item for error hooks, if applicable.
    /// </summary>
    /// <param name="validItem">The validated item.</param>
    /// <returns>The API entity, or null.</returns>
    protected virtual TApiModel? GetApiEntity(TValidItem validItem) => default;

    /// <summary>
    /// Extracts the DB entity from a validated item for error hooks, if applicable.
    /// </summary>
    /// <param name="validItem">The validated item.</param>
    /// <returns>The DB entity, or null.</returns>
    protected virtual TDbModel? GetDbEntity(TValidItem validItem) => default;

    /// <summary>
    /// Persists all validated items using the bulk repository path.
    /// </summary>
    /// <param name="validItems">The validated items.</param>
    /// <param name="results">The results array to populate.</param>
    /// <param name="context">The mapped batch context.</param>
    protected virtual async Task PersistBulkAsync(
        List<TValidItem> validItems,
        BatchItemResult?[] results,
        MappedBatchContext<TApiModel, TDbModel, TKey> context) =>
        await PersistIndividuallyAsync(validItems, results, context);

    /// <summary>
    /// Runs the after-persist hook and builds the success result.
    /// </summary>
    /// <param name="index">The item index.</param>
    /// <param name="dbEntity">The persisted DB entity.</param>
    /// <param name="resourceId">The resource ID for hook context.</param>
    /// <param name="context">The mapped batch context.</param>
    /// <returns>The batch item result.</returns>
    protected async Task<BatchItemResult> RunAfterPersistAndBuildResultAsync(
        int index,
        TDbModel dbEntity,
        TKey? resourceId,
        MappedBatchContext<TApiModel, TDbModel, TKey> context)
    {
        var apiEntity = context.Mapper.ToApi(dbEntity);

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

        object resultEntity = apiEntity;
        if (context.Options.EnableHateoas)
        {
            var entityKey = EntityKeyHelper.GetEntityKey(apiEntity, context.EndpointConfig.KeySelector);
            if (entityKey is not null)
            {
                var customLinksProvider = context.HttpContext.RequestServices.GetService<IHateoasLinkProvider<TApiModel, TKey>>();
                var customLinks = customLinksProvider?.GetLinks(apiEntity, entityKey);
                var links = HateoasLinkBuilder.BuildEntityLinks(
                    context.HttpContext.Request,
                    context.CollectionPath,
                    entityKey,
                    context.EndpointConfig,
                    customLinks);
                resultEntity = HateoasHelper.EntityWithLinks<TApiModel, TKey>(apiEntity, links, context.JsonOptions);
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
    /// Processes bulk results by running after-persist hooks and building success results.
    /// </summary>
    /// <param name="validItems">The validated items that were persisted.</param>
    /// <param name="bulkResults">The DB entities returned from the bulk operation.</param>
    /// <param name="results">The results array to populate.</param>
    /// <param name="context">The mapped batch context.</param>
    protected async Task ProcessBulkResultsAsync(
        List<TValidItem> validItems,
        IReadOnlyList<TDbModel> bulkResults,
        BatchItemResult?[] results,
        MappedBatchContext<TApiModel, TDbModel, TKey> context)
    {
        for (var j = 0; j < validItems.Count; j++)
        {
            var index = GetIndex(validItems[j]);
            var dbEntity = bulkResults[j];

            results[index] = await RunAfterPersistAndBuildResultAsync(
                index,
                dbEntity,
                GetResourceId(validItems[j]),
                context);
        }
    }

    /// <summary>
    /// Creates a batch item result from an exception.
    /// </summary>
    /// <param name="index">The item index.</param>
    /// <param name="exception">The exception.</param>
    /// <param name="instance">The request path.</param>
    /// <param name="options">The RestLib options.</param>
    /// <returns>A batch item result describing the exception.</returns>
    private static BatchItemResult ExceptionResult(
        int index,
        Exception exception,
        string? instance,
        RestLibOptions options)
    {
        var detail = options.IncludeExceptionDetailsInErrors
            ? $"{exception.GetType().Name}: {exception.Message}"
            : "An internal error occurred while processing this item.";

        return new BatchItemResult
        {
            Index = index,
            Status = StatusCodes.Status500InternalServerError,
            Error = ProblemDetailsFactory.InternalError(detail: detail, instance: instance)
        };
    }

    private static bool IsConcurrencyException(Exception exception) =>
        exception.GetType().FullName == "Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException";

    private static BatchItemResult BuildHandledErrorResult(
        int index,
        Exception exception,
        IResult errorResult,
        MappedBatchContext<TApiModel, TDbModel, TKey> context)
    {
        var statusCode = errorResult is IStatusCodeHttpResult statusResult
            ? statusResult.StatusCode ?? StatusCodes.Status500InternalServerError
            : StatusCodes.Status500InternalServerError;

        var error = errorResult is IValueHttpResult { Value: RestLibProblemDetails problem }
            ? problem
            : ProblemDetailsFactory.InternalError(
                detail: context.Options.IncludeExceptionDetailsInErrors
                    ? $"{exception.GetType().Name}: {exception.Message}"
                    : "An internal error occurred while processing this item.",
                instance: context.HttpContext.Request.Path);

        return new BatchItemResult
        {
            Index = index,
            Status = statusCode,
            Error = error
        };
    }

    private async Task ExecuteAsync(
        List<TValidItem> validItems,
        BatchItemResult?[] results,
        MappedBatchContext<TApiModel, TDbModel, TKey> context)
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
                            GetApiEntity(item),
                            GetDbEntity(item));
                    }

                    return;
                }

                var actionName = Operation.ToString().ToLowerInvariant();
                RestLibLogMessages.BulkPersistenceFallback(
                    context.Logger,
                    actionName,
                    validItems.Count,
                    bulkException);

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

    private async Task PersistIndividuallyAsync(
        List<TValidItem> validItems,
        BatchItemResult?[] results,
        MappedBatchContext<TApiModel, TDbModel, TKey> context)
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
                    index,
                    ex,
                    context,
                    GetResourceId(item),
                    GetApiEntity(item),
                    GetDbEntity(item));
            }
        }
    }

    private async Task<BatchItemResult> HandleItemErrorAsync(
        int index,
        Exception exception,
        MappedBatchContext<TApiModel, TDbModel, TKey> context,
        TKey? resourceId = default,
        TApiModel? apiEntity = default,
        TDbModel? dbEntity = default)
    {
        try
        {
            if (context.DbPipeline is not null)
            {
                var errorContext = context.DbPipeline.CreateErrorContext(
                    context.HttpContext,
                    Operation,
                    exception,
                    resourceId,
                    dbEntity);
                var (handled, errorResult) = await context.DbPipeline.ExecuteOnErrorAsync(errorContext);
                if (handled && errorResult is not null)
                {
                    return BuildHandledErrorResult(index, exception, errorResult, context);
                }
            }
            else if (context.ApiPipeline is not null)
            {
                var errorContext = context.ApiPipeline.CreateErrorContext(
                    context.HttpContext,
                    Operation,
                    exception,
                    resourceId,
                    apiEntity);
                var (handled, errorResult) = await context.ApiPipeline.ExecuteOnErrorAsync(errorContext);
                if (handled && errorResult is not null)
                {
                    return BuildHandledErrorResult(index, exception, errorResult, context);
                }
            }
        }
        catch (Exception hookException)
        {
            var actionName = Operation.ToString().ToLowerInvariant();
            RestLibLogMessages.BatchErrorHookSwallowed(
                context.Logger,
                actionName,
                index,
                hookException);
        }

        return ExceptionResult(index, exception, context.HttpContext.Request.Path, context.Options);
    }
}
