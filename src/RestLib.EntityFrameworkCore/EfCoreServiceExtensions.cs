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
    /// <typeparam name="TContext">The DbContext type that includes <typeparamref name="TEntity"/> in its EF Core model.</typeparam>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The primary key type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
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
    /// <typeparam name="TContext">The DbContext type that includes <typeparamref name="TEntity"/> in its EF Core model.</typeparam>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The primary key type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">A callback to configure repository options.</param>
    /// <returns>The service collection for chaining.</returns>
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
}
