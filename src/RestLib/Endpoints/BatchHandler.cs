using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RestLib.Abstractions;
using RestLib.Batch;
using RestLib.Configuration;
using RestLib.Hooks;
using RestLib.Logging;

namespace RestLib.Endpoints;

/// <summary>
/// Handles POST requests for batch operations.
/// </summary>
internal static class BatchHandler
{
    /// <summary>
    /// Creates the delegate for the Batch endpoint.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="config">The endpoint configuration.</param>
    /// <returns>The request delegate.</returns>
    internal static Func<HttpContext, Task<IResult>>
        CreateDelegate<TEntity, TKey>(RestLibEndpointConfiguration<TEntity, TKey> config)
        where TEntity : class
        where TKey : notnull
    {
        return async httpContext =>
        {
            var (jsonOptions, options) = OptionsResolver.ResolveOptions(httpContext);
            var logger = RestLibLoggerResolver.ResolveLogger(httpContext, "RestLib.Batch");
            var repository = httpContext.RequestServices.GetRequiredService<IRepository<TEntity, TKey>>();
            var ct = httpContext.RequestAborted;
            var instance = httpContext.Request.Path.ToString();

            // Deserialize the envelope
            BatchRequestEnvelope? envelope;
            try
            {
                envelope = await httpContext.Request.ReadFromJsonAsync<BatchRequestEnvelope>(jsonOptions, ct);
            }
            catch (JsonException ex)
            {
                RestLibLogMessages.BatchEnvelopeDeserializationFailed(logger, ex);
                return Responses.ProblemDetailsResult.InvalidBatchRequest(
                    "The request body is not valid JSON.",
                    instance: instance,
                    jsonOptions: jsonOptions,
                    logger: logger);
            }

            if (envelope is null)
            {
                return Responses.ProblemDetailsResult.InvalidBatchRequest(
                    "The request body is empty.",
                    instance: instance,
                    jsonOptions: jsonOptions,
                    logger: logger);
            }

            // Validate action
            if (!Enum.TryParse<BatchAction>(envelope.Action, ignoreCase: true, out var action))
            {
                var allowedActions = config.EnabledBatchActions
                    .Select(a => a.ToString().ToLowerInvariant());
                return Responses.ProblemDetailsResult.InvalidBatchRequest(
                    $"'{envelope.Action}' is not a valid batch action.",
                    errors: new Dictionary<string, string[]>
                    {
                        ["action"] = [$"'{envelope.Action}' is not a valid batch action. Allowed actions: {string.Join(", ", allowedActions)}."]
                    },
                    instance: instance,
                    jsonOptions: jsonOptions,
                    logger: logger);
            }

            // Verify action is enabled
            if (!config.IsBatchActionEnabled(action))
            {
                var enabledActions = config.EnabledBatchActions
                    .Select(a => a.ToString().ToLowerInvariant());
                return Responses.ProblemDetailsResult.BatchActionNotEnabled(
                    action.ToString().ToLowerInvariant(),
                    enabledActions,
                    instance: instance,
                    jsonOptions: jsonOptions,
                    logger: logger);
            }

            // Validate items array exists
            if (envelope.Items.ValueKind == JsonValueKind.Undefined
                || envelope.Items.ValueKind == JsonValueKind.Null)
            {
                return Responses.ProblemDetailsResult.InvalidBatchRequest(
                    "The 'items' array is required.",
                    instance: instance,
                    jsonOptions: jsonOptions,
                    logger: logger);
            }

            if (envelope.Items.ValueKind != JsonValueKind.Array)
            {
                return Responses.ProblemDetailsResult.InvalidBatchRequest(
                    "The 'items' property must be an array.",
                    instance: instance,
                    jsonOptions: jsonOptions,
                    logger: logger);
            }

            var itemCount = envelope.Items.GetArrayLength();

            // Validate non-empty
            if (itemCount == 0)
            {
                return Responses.ProblemDetailsResult.InvalidBatchRequest(
                    "The 'items' array must contain at least one item.",
                    instance: instance,
                    jsonOptions: jsonOptions,
                    logger: logger);
            }

            // Validate batch size
            if (options.MaxBatchSize > 0 && itemCount > options.MaxBatchSize)
            {
                return Responses.ProblemDetailsResult.BatchSizeExceeded(
                    itemCount,
                    options.MaxBatchSize,
                    instance: instance,
                    jsonOptions: jsonOptions,
                    logger: logger);
            }

            // Create hook pipeline if hooks are configured
            var pipeline = config.Hooks is not null ? new HookPipeline<TEntity, TKey>(config.Hooks, logger) : null;

            var actionName = action.ToString().ToLowerInvariant();
            RestLibLogMessages.BatchRequestReceived(logger, actionName, itemCount);

            // Resolve optional batch repository for bulk-optimized operations
            var batchRepository = httpContext.RequestServices
                .GetService<IBatchRepository<TEntity, TKey>>();

            // Build the shared batch context
            var batchContext = new BatchContext<TEntity, TKey>
            {
                HttpContext = httpContext,
                Repository = repository,
                BatchRepository = batchRepository,
                Pipeline = pipeline,
                Options = options,
                JsonOptions = jsonOptions,
                CancellationToken = ct,
                EndpointConfig = config,
                CollectionPath = GetCollectionPathFromBatchPath(instance),
                Logger = logger
            };

            // Dispatch to the appropriate pipeline
            var response = action switch
            {
                BatchAction.Create => await new BatchCreatePipeline<TEntity, TKey>()
                    .ProcessAsync(envelope.Items, batchContext),
                BatchAction.Update => await new BatchUpdatePipeline<TEntity, TKey>()
                    .ProcessAsync(envelope.Items, batchContext),
                BatchAction.Patch => await new BatchPatchPipeline<TEntity, TKey>()
                    .ProcessAsync(envelope.Items, batchContext),
                BatchAction.Delete => await new BatchDeletePipeline<TEntity, TKey>()
                    .ProcessAsync(envelope.Items, batchContext),
                _ => throw new InvalidOperationException($"Unexpected batch action: {action}")
            };

            // BeforeResponse hook — runs once for the entire batch, after all items are processed.
            if (pipeline is not null)
            {
                var batchOperation = action switch
                {
                    BatchAction.Create => RestLibOperation.BatchCreate,
                    BatchAction.Update => RestLibOperation.BatchUpdate,
                    BatchAction.Patch => RestLibOperation.BatchPatch,
                    BatchAction.Delete => RestLibOperation.BatchDelete,
                    _ => RestLibOperation.BatchCreate
                };
                var hookContext = pipeline.CreateContext(httpContext, batchOperation);
                var beforeResponseResult = await HookHelper.ExecuteHookAsync(
                    pipeline.ExecuteBeforeResponseAsync, hookContext);
                if (beforeResponseResult is not null) return beforeResponseResult;
            }

            // Determine response status code
            var allSucceeded = response.Items.All(r => r.Status is >= 200 and < 300);
            var statusCode = allSucceeded
                ? StatusCodes.Status200OK
                : StatusCodes.Status207MultiStatus;

            var succeeded = response.Items.Count(r => r.Status is >= 200 and < 300);
            var failed = response.Items.Count - succeeded;
            RestLibLogMessages.BatchCompleted(logger, actionName, response.Items.Count, succeeded, failed, statusCode);

            return Results.Json(response, jsonOptions, statusCode: statusCode);
        };
    }

    /// <summary>
    /// Extracts the collection path from the batch endpoint path by removing the trailing "/batch" segment.
    /// For example, "/api/products/batch" becomes "/api/products".
    /// </summary>
    /// <param name="batchPath">The full batch endpoint path.</param>
    /// <returns>The collection path.</returns>
    private static string GetCollectionPathFromBatchPath(string batchPath)
    {
        const string batchSuffix = "/batch";
        if (batchPath.EndsWith(batchSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return batchPath[..^batchSuffix.Length];
        }

        return batchPath;
    }
}
