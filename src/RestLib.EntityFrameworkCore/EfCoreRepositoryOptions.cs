using System.Linq.Expressions;
using Microsoft.Extensions.Logging;

namespace RestLib.EntityFrameworkCore;

/// <summary>
/// Configuration options for the EF Core repository adapter.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The primary key type.</typeparam>
public class EfCoreRepositoryOptions<TEntity, TKey>
    where TEntity : class
    where TKey : notnull
{
    /// <summary>
    /// Gets or sets the expression used to extract the resource key from an entity.
    /// When <c>null</c>, the adapter attempts to resolve the key automatically
    /// from EF Core model metadata. Explicit selectors must access direct mapped
    /// properties and should identify a unique resource key.
    /// </summary>
    public Expression<Func<TEntity, TKey>>? KeySelector { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether read operations should use
    /// <c>AsNoTracking</c>. Default is <c>true</c>.
    /// </summary>
    public bool UseAsNoTracking { get; set; } = true;

    /// <summary>
    /// Gets or sets how PATCH operations handle unknown or forbidden fields.
    /// Default is <see cref="EfCorePatchUnknownFieldBehavior.Permissive"/>.
    /// </summary>
    public EfCorePatchUnknownFieldBehavior PatchUnknownFieldBehavior { get; set; }
        = EfCorePatchUnknownFieldBehavior.Permissive;

    /// <summary>
    /// Gets or sets an optional logger used for adapter-level fallback warnings.
    /// </summary>
    public ILogger? Logger { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether field-selection projection pushdown is enabled.
    /// When enabled, GET endpoints may translate direct scalar field-selection requests into
    /// SQL <c>SELECT</c> projections. Nested paths and requests requiring HATEOAS, ETags, or
    /// hooks fall back to full entity materialization. Default is <c>false</c>.
    /// </summary>
    public bool EnableProjectionPushdown { get; set; }
}
