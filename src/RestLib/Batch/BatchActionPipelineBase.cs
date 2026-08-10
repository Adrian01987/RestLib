using Microsoft.AspNetCore.Http;
using RestLib.Configuration;
using RestLib.Hooks;
using RestLib.Internal;
using RestLib.Logging;
using RestLib.Responses;
using RestLib.Serialization;
using RestLib.Validation;

namespace RestLib.Batch;

/// <summary>
/// Defines the common state machine used by mapped and unmapped batch actions.
/// </summary>
/// <typeparam name="TKey">The resource key type.</typeparam>
/// <typeparam name="TRawItem">The item type deserialized from the request.</typeparam>
/// <typeparam name="TValidItem">The validated item type passed to persistence.</typeparam>
/// <typeparam name="TContext">The batch execution context type.</typeparam>
internal abstract class BatchActionPipelineBase<TKey, TRawItem, TValidItem, TContext>
    where TKey : notnull
    where TContext : BatchPipelineContext<TKey>
{
    /// <summary>
    /// Gets the operation represented by the pipeline.
    /// </summary>
    protected abstract RestLibOperation Operation { get; }

    /// <summary>
    /// Gets a value indicating whether the action supports bulk persistence.
    /// </summary>
    protected virtual bool HasBulkPath => true;

    /// <summary>
    /// Processes a batch request through deserialization, validation, and persistence.
    /// </summary>
    /// <param name="items">The ordered members of the accepted items array.</param>
    /// <param name="context">The batch execution context.</param>
    /// <returns>The response containing one result per submitted item.</returns>
    internal async Task<BatchResponse> ProcessAsync(
        IReadOnlyList<BatchItemInput> items,
        TContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();

        var results = new BatchItemResult?[items.Count];
        var validItems = new List<TValidItem>();

        for (var index = 0; index < items.Count; index++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var input = items[index];
            TRawItem? rawItem;
            var deserialized = true;
            if (input.HasDeserializationError)
            {
                deserialized = false;
                rawItem = default;
            }
            else if (input.HasDeserializedValue)
            {
                rawItem = (TRawItem?)input.DeserializedValue;
            }
            else
            {
                deserialized = JsonDeserializationHelper.TryDeserializeItem(
                    input.JsonValue,
                    context.JsonOptions,
                    out rawItem,
                    context.Logger);
            }

            if (!deserialized)
            {
                results[index] = BadRequestResult(
                    index,
                    $"The batch item at index {index} could not be deserialized.",
                    context.HttpContext.Request.Path);
                continue;
            }

            var (error, validItem) = await ValidateItemAsync(index, rawItem, context);
            if (error is not null)
            {
                results[index] = error;
                continue;
            }

            validItems.Add(validItem!);
        }

        await ExecuteAsync(validItems, results, context);

        return new BatchResponse { Items = results.ToList()! };
    }

    /// <summary>
    /// Runs the request hook stages.
    /// </summary>
    /// <typeparam name="THookModel">The hook model type.</typeparam>
    /// <param name="index">The item index.</param>
    /// <param name="pipeline">The hook pipeline.</param>
    /// <param name="hookContext">The hook context.</param>
    /// <returns>An error result when a hook short-circuits; otherwise, <c>null</c>.</returns>
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
    /// Runs the before-persist hook stage.
    /// </summary>
    /// <typeparam name="THookModel">The hook model type.</typeparam>
    /// <param name="index">The item index.</param>
    /// <param name="pipeline">The hook pipeline.</param>
    /// <param name="hookContext">The hook context.</param>
    /// <returns>An error result when the hook short-circuits; otherwise, <c>null</c>.</returns>
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
    /// Runs all hook stages that precede persistence.
    /// </summary>
    /// <typeparam name="THookModel">The hook model type.</typeparam>
    /// <param name="index">The item index.</param>
    /// <param name="pipeline">The hook pipeline.</param>
    /// <param name="hookContext">The hook context.</param>
    /// <returns>An error result when a hook short-circuits; otherwise, <c>null</c>.</returns>
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
    /// Creates a bad-request item result.
    /// </summary>
    /// <param name="index">The item index.</param>
    /// <param name="detail">The error detail.</param>
    /// <param name="instance">The request path.</param>
    /// <returns>The item result.</returns>
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
    /// Creates a validation-failed item result.
    /// </summary>
    /// <param name="index">The item index.</param>
    /// <param name="validationResult">The validation result.</param>
    /// <param name="instance">The request path.</param>
    /// <returns>The item result.</returns>
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
    /// Creates an item result from a hook short-circuit.
    /// </summary>
    /// <typeparam name="THookModel">The hook model type.</typeparam>
    /// <param name="index">The item index.</param>
    /// <param name="hookContext">The hook context.</param>
    /// <returns>The item result.</returns>
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
    /// Creates an item result from an after-persist hook short-circuit.
    /// </summary>
    /// <param name="index">The item index.</param>
    /// <param name="earlyResult">The hook result.</param>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <returns>The item result.</returns>
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
    /// Validates one deserialized item.
    /// </summary>
    /// <param name="index">The original item index.</param>
    /// <param name="rawItem">The deserialized item.</param>
    /// <param name="context">The batch context.</param>
    /// <returns>An error or a validated item.</returns>
    protected abstract Task<(BatchItemResult? Error, TValidItem? ValidItem)> ValidateItemAsync(
        int index,
        TRawItem? rawItem,
        TContext context);

    /// <summary>
    /// Gets the original request index from a validated item.
    /// </summary>
    /// <param name="validItem">The validated item.</param>
    /// <returns>The original item index.</returns>
    protected abstract int GetIndex(TValidItem validItem);

    /// <summary>
    /// Gets the resource ID represented by a validated item, when available.
    /// </summary>
    /// <param name="validItem">The validated item.</param>
    /// <returns>The resource ID.</returns>
    protected virtual TKey? GetResourceId(TValidItem validItem) => default;

    /// <summary>
    /// Determines whether a bulk repository is available for the current context.
    /// </summary>
    /// <param name="context">The batch context.</param>
    /// <returns><c>true</c> when bulk persistence can be used.</returns>
    protected abstract bool CanUseBulk(TContext context);

    /// <summary>
    /// Persists the validated items using the bulk repository contract.
    /// </summary>
    /// <param name="validItems">The validated items.</param>
    /// <param name="results">The indexed results.</param>
    /// <param name="context">The batch context.</param>
    protected abstract Task PersistBulkAsync(
        List<TValidItem> validItems,
        BatchItemResult?[] results,
        TContext context);

    /// <summary>
    /// Persists one validated item using the ordinary repository contract.
    /// </summary>
    /// <param name="validItem">The validated item.</param>
    /// <param name="results">The indexed results.</param>
    /// <param name="context">The batch context.</param>
    protected abstract Task PersistSingleItemAsync(
        TValidItem validItem,
        BatchItemResult?[] results,
        TContext context);

    /// <summary>
    /// Executes the active model's error hook for a failed item.
    /// </summary>
    /// <param name="validItem">The failed validated item.</param>
    /// <param name="exception">The persistence exception.</param>
    /// <param name="context">The batch context.</param>
    /// <returns>The hook handling state and optional result.</returns>
    protected abstract Task<(bool Handled, IResult? Result)> ExecuteErrorHookAsync(
        TValidItem validItem,
        Exception exception,
        TContext context);

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

    private static BatchItemResult BuildHandledErrorResult(
        int index,
        Exception exception,
        IResult errorResult,
        TContext context)
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
        TContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();

        if (validItems.Count == 0)
        {
            return;
        }

        if (HasBulkPath && CanUseBulk(context))
        {
            try
            {
                await PersistBulkAsync(validItems, results, context);
            }
            catch (Exception bulkException) when (
                bulkException is BulkPersistenceException or BatchRepositoryContractException)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                var persistenceException = bulkException is BulkPersistenceException
                    ? bulkException.InnerException ?? bulkException
                    : bulkException;
                var actionName = Operation.ToString().ToLowerInvariant();
                if (persistenceException is BatchRepositoryContractException)
                {
                    RestLibLogMessages.BatchRepositoryContractViolated(
                        context.Logger,
                        actionName,
                        validItems.Count,
                        persistenceException);
                }
                else
                {
                    RestLibLogMessages.BulkPersistenceFailed(
                        context.Logger,
                        actionName,
                        validItems.Count,
                        persistenceException);
                }

                var failedItems = validItems
                    .Where(item => results[GetIndex(item)] is null)
                    .ToList();

                foreach (var item in failedItems)
                {
                    context.CancellationToken.ThrowIfCancellationRequested();

                    var index = GetIndex(item);
                    results[index] = await HandleItemErrorAsync(
                        item,
                        persistenceException,
                        context);
                }
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
        TContext context)
    {
        foreach (var item in validItems)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var index = GetIndex(item);
            try
            {
                await PersistSingleItemAsync(item, results, context);
            }
            catch (Exception exception) when (
                !RequestCancellation.IsRequested(exception, context.CancellationToken))
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                var actionName = Operation.ToString().ToLowerInvariant();
                RestLibLogMessages.BatchItemPersistenceFailed(
                    context.Logger,
                    actionName,
                    index,
                    exception);

                results[index] = await HandleItemErrorAsync(item, exception, context);
            }
        }
    }

    private async Task<BatchItemResult> HandleItemErrorAsync(
        TValidItem validItem,
        Exception exception,
        TContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        var index = GetIndex(validItem);

        try
        {
            var (handled, errorResult) = await ExecuteErrorHookAsync(validItem, exception, context);
            context.CancellationToken.ThrowIfCancellationRequested();

            if (handled && errorResult is not null)
            {
                return BuildHandledErrorResult(index, exception, errorResult, context);
            }
        }
        catch (Exception hookException) when (
            !RequestCancellation.IsRequested(hookException, context.CancellationToken))
        {
            var actionName = Operation.ToString().ToLowerInvariant();
            RestLibLogMessages.BatchErrorHookSwallowed(
                context.Logger,
                actionName,
                index,
                hookException);
        }

        return ExceptionResult(
            index,
            exception,
            context.HttpContext.Request.Path,
            context.Options);
    }
}
