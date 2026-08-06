using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using RestLib.Configuration;

namespace RestLib.Batch;

/// <summary>
/// Holds the transport and execution state shared by every batch pipeline.
/// </summary>
/// <typeparam name="TKey">The resource key type.</typeparam>
internal abstract class BatchPipelineContext<TKey>
    where TKey : notnull
{
    /// <summary>
    /// Gets the current HTTP context.
    /// </summary>
    internal required HttpContext HttpContext { get; init; }

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
    /// Gets the collection route path used for HATEOAS links.
    /// </summary>
    internal required string CollectionPath { get; init; }
}
