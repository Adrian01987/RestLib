using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using RestLib.Abstractions;
using RestLib.Configuration;
using RestLib.Hooks;

namespace RestLib.Batch;

/// <summary>
/// Holds all shared services and state needed during mapped batch processing.
/// </summary>
/// <typeparam name="TApiModel">The API model type.</typeparam>
/// <typeparam name="TDbModel">The DB model type.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
internal sealed class MappedBatchContext<TApiModel, TDbModel, TKey>
    where TApiModel : class
    where TDbModel : class
    where TKey : notnull
{
    /// <summary>
    /// Gets the current HTTP context.
    /// </summary>
    internal required HttpContext HttpContext { get; init; }

    /// <summary>
    /// Gets the DB-model repository.
    /// </summary>
    internal required IRepository<TDbModel, TKey> Repository { get; init; }

    /// <summary>
    /// Gets the optional batch-optimized DB-model repository.
    /// </summary>
    internal IBatchRepository<TDbModel, TKey>? BatchRepository { get; init; }

    /// <summary>
    /// Gets the optional API-model hook pipeline.
    /// </summary>
    internal HookPipeline<TApiModel, TKey>? ApiPipeline { get; init; }

    /// <summary>
    /// Gets the optional DB-model hook pipeline.
    /// </summary>
    internal HookPipeline<TDbModel, TKey>? DbPipeline { get; init; }

    /// <summary>
    /// Gets the mapper between API and DB models.
    /// </summary>
    internal required IRestLibMapper<TApiModel, TDbModel> Mapper { get; init; }

    /// <summary>
    /// Gets the global RestLib options.
    /// </summary>
    internal required RestLibOptions Options { get; init; }

    /// <summary>
    /// Gets the JSON serializer options.
    /// </summary>
    internal required JsonSerializerOptions JsonOptions { get; init; }

    /// <summary>
    /// Gets the cancellation token.
    /// </summary>
    internal required CancellationToken CancellationToken { get; init; }

    /// <summary>
    /// Gets the logger for batch pipeline operations.
    /// </summary>
    internal required ILogger Logger { get; init; }

    /// <summary>
    /// Gets the mapped endpoint configuration.
    /// </summary>
    internal required RestLibEndpointConfiguration<TApiModel, TDbModel, TKey> EndpointConfig { get; init; }

    /// <summary>
    /// Gets the collection route path used for HATEOAS links.
    /// </summary>
    internal required string CollectionPath { get; init; }
}
