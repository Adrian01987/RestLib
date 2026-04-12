using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestLib.Abstractions;

namespace RestLib.EntityFrameworkCore;

/// <summary>
/// Extension methods for configuring EF Core-backed repository services.
/// </summary>
public static class EfCoreServiceExtensions
{
    /// <summary>
    /// Registers an EF Core-backed repository for the specified entity type.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type that contains a <c>DbSet&lt;TEntity&gt;</c>.</typeparam>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The primary key type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <typeparamref name="TContext"/> does not contain a
    /// <c>DbSet&lt;TEntity&gt;</c> property.
    /// </exception>
    public static IServiceCollection AddRestLibEfCore<TContext, TEntity, TKey>(
        this IServiceCollection services)
        where TContext : DbContext
        where TEntity : class
        where TKey : notnull
    {
        return services.AddRestLibEfCore<TContext, TEntity, TKey>(_ => { });
    }

    /// <summary>
    /// Registers an EF Core-backed repository for the specified entity type
    /// with custom configuration options.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type that contains a <c>DbSet&lt;TEntity&gt;</c>.</typeparam>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The primary key type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">A callback to configure repository options.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <typeparamref name="TContext"/> does not contain a
    /// <c>DbSet&lt;TEntity&gt;</c> property.
    /// </exception>
    public static IServiceCollection AddRestLibEfCore<TContext, TEntity, TKey>(
        this IServiceCollection services,
        Action<EfCoreRepositoryOptions<TEntity, TKey>> configure)
        where TContext : DbContext
        where TEntity : class
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new EfCoreRepositoryOptions<TEntity, TKey>();
        configure(options);

        var hasDbSetProperty = typeof(TContext)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Any(property => property.PropertyType == typeof(DbSet<TEntity>));

        if (!hasDbSetProperty)
        {
            throw new InvalidOperationException(
                $"DbContext type '{typeof(TContext).Name}' does not contain a DbSet<{typeof(TEntity).Name}> property.");
        }

        options.KeySelector ??= ResolveKeySelector<TContext, TEntity, TKey>(services);

        services.AddSingleton(options);
        services.AddScoped<EfCoreRepository<TContext, TEntity, TKey>>();
        services.AddScoped<IRepository<TEntity, TKey>>(
            sp => sp.GetRequiredService<EfCoreRepository<TContext, TEntity, TKey>>());
        services.AddScoped<IBatchRepository<TEntity, TKey>>(
            sp => sp.GetRequiredService<EfCoreRepository<TContext, TEntity, TKey>>());
        services.AddScoped<ICountableRepository<TEntity, TKey>>(
            sp => sp.GetRequiredService<EfCoreRepository<TContext, TEntity, TKey>>());

        return services;
    }

    private static Expression<Func<TEntity, TKey>> ResolveKeySelector<TContext, TEntity, TKey>(
        IServiceCollection services)
        where TContext : DbContext
        where TEntity : class
        where TKey : notnull
    {
        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        var entityType = context.Model.FindEntityType(typeof(TEntity));
        if (entityType is null)
        {
            throw new InvalidOperationException(
                $"Entity type '{typeof(TEntity).Name}' is not part of the EF Core model for DbContext '{typeof(TContext).Name}'.");
        }

        var primaryKey = entityType.FindPrimaryKey();
        if (primaryKey is null)
        {
            throw new InvalidOperationException(
                $"Entity type '{typeof(TEntity).Name}' has no primary key configured in the EF Core model.");
        }

        if (primaryKey.Properties.Count > 1)
        {
            var propertyNames = string.Join(", ", primaryKey.Properties.Select(property => property.Name));
            throw new NotSupportedException(
                $"Entity type '{typeof(TEntity).Name}' has a composite primary key, which is not supported. Composite keys have {primaryKey.Properties.Count} properties: {propertyNames}. Only single-property primary keys are supported.");
        }

        var keyProperty = primaryKey.Properties[0];
        if (keyProperty.ClrType != typeof(TKey))
        {
            throw new InvalidOperationException(
                $"Entity type '{typeof(TEntity).Name}' has primary key property '{keyProperty.Name}' of type '{keyProperty.ClrType.Name}', but the registration specifies TKey as '{typeof(TKey).Name}'.");
        }

        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        var propertyAccess = keyProperty.PropertyInfo is not null
            ? Expression.Property(parameter, keyProperty.PropertyInfo)
            : Expression.Property(parameter, keyProperty.Name);

        return Expression.Lambda<Func<TEntity, TKey>>(propertyAccess, parameter);
    }
}
