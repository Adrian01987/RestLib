using System.Linq.Expressions;

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
    /// Gets or sets the expression used to extract the primary key from an entity.
    /// When <c>null</c>, the adapter attempts to resolve the key automatically
    /// from EF Core model metadata.
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
}
