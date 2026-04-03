using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RestLib.Abstractions;
using RestLib.Batch;
using RestLib.Configuration;
using RestLib.Hooks;

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
    {
        return async (HttpContext httpContext) =>
        {
            var (jsonOptions, options) = EndpointHelpers.ResolveOptions(httpContext);
            var repository = httpContext.RequestServices.GetRequiredService<IRepository<TEntity, TKey>>();
            var ct = httpContext.RequestAborted;
            var instance = httpContext.Request.Path.ToString();

            // Deserialize the envelope
            BatchRequestEnvelope? envelope;
            try
            {
                envelope = await httpContext.Request.ReadFromJsonAsync<BatchRequestEnvelope>(jsonOptions, ct);
            }
            catch (JsonException)
            {
                return Responses.ProblemDetailsResult.InvalidBatchRequest(
                    "The request body is not valid JSON.",
                    instance: instance,
                    jsonOptions: jsonOptions);
            }

            if (envelope is null)
            {
                return Responses.ProblemDetailsResult.InvalidBatchRequest(
                    "The request body is empty.",
                    instance: instance,
                    jsonOptions: jsonOptions);
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
                    jsonOptions: jsonOptions);
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
                    jsonOptions: jsonOptions);
            }

            // Validate items array exists
            if (envelope.Items.ValueKind == JsonValueKind.Undefined
                || envelope.Items.ValueKind == JsonValueKind.Null)
            {
                return Responses.ProblemDetailsResult.InvalidBatchRequest(
                    "The 'items' array is required.",
                    instance: instance,
                    jsonOptions: jsonOptions);
            }

            if (envelope.Items.ValueKind != JsonValueKind.Array)
            {
                return Responses.ProblemDetailsResult.InvalidBatchRequest(
                    "The 'items' property must be an array.",
                    instance: instance,
                    jsonOptions: jsonOptions);
            }

            var itemCount = envelope.Items.GetArrayLength();

            // Validate non-empty
            if (itemCount == 0)
            {
                return Responses.ProblemDetailsResult.InvalidBatchRequest(
                    "The 'items' array must contain at least one item.",
                    instance: instance,
                    jsonOptions: jsonOptions);
            }

            // Validate batch size
            if (options.MaxBatchSize > 0 && itemCount > options.MaxBatchSize)
            {
                return Responses.ProblemDetailsResult.BatchSizeExceeded(
                    itemCount,
                    options.MaxBatchSize,
                    instance: instance,
                    jsonOptions: jsonOptions);
            }

            // Create hook pipeline if hooks are configured
            var pipeline = config.Hooks is not null ? new HookPipeline<TEntity, TKey>(config.Hooks) : null;

            // Resolve optional batch repository for bulk-optimized operations
            var batchRepository = httpContext.RequestServices
                .GetService<IBatchRepository<TEntity, TKey>>();

            // Dispatch to the appropriate processor
            var response = action switch
            {
                BatchAction.Create => await BatchProcessor.ProcessCreateAsync(
                    envelope.Items, httpContext, repository, batchRepository, config,
                    pipeline, options, jsonOptions, ct),
                BatchAction.Update => await BatchProcessor.ProcessUpdateAsync(
                    envelope.Items, httpContext, repository, batchRepository, config,
                    pipeline, options, jsonOptions, ct),
                BatchAction.Patch => await BatchProcessor.ProcessPatchAsync(
                    envelope.Items, httpContext, repository, batchRepository, config,
                    pipeline, options, jsonOptions, ct),
                BatchAction.Delete => await BatchProcessor.ProcessDeleteAsync(
                    envelope.Items, httpContext, repository, batchRepository, config,
                    pipeline, jsonOptions, ct),
                _ => throw new InvalidOperationException($"Unexpected batch action: {action}")
            };

            // Determine response status code
            var allSucceeded = response.Items.All(r => r.Status is >= 200 and < 300);
            var statusCode = allSucceeded
                ? StatusCodes.Status200OK
                : StatusCodes.Status207MultiStatus;

            return Results.Json(response, jsonOptions, statusCode: statusCode);
        };
    }
}
