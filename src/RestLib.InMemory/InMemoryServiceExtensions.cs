using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RestLib.Abstractions;
using RestLib.Serialization;

namespace RestLib.InMemory;

/// <summary>
/// Extension methods for configuring in-memory repository services.
/// </summary>
public static class InMemoryServiceExtensions
{
    /// <summary>
    /// Registers an in-memory repository for the specified entity type.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="keySelector">Function to extract the key from an entity.</param>
    /// <param name="keyGenerator">Function to generate a new key for entity creation.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRestLibInMemory<TEntity, TKey>(
        this IServiceCollection services,
        Func<TEntity, TKey> keySelector,
        Func<TKey> keyGenerator)
        where TEntity : class
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        ArgumentNullException.ThrowIfNull(keyGenerator);

        return RegisterRepository(
            services,
            serviceProvider => new InMemoryRepository<TEntity, TKey>(
                keySelector,
                keyGenerator,
                ResolveJsonOptions(serviceProvider)));
    }

    /// <summary>
    /// Registers an in-memory repository with an explicit key comparer.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="keySelector">Function to extract the key from an entity.</param>
    /// <param name="keyGenerator">Function to generate a new key for entity creation.</param>
    /// <param name="keyComparer">Comparer used for default ordering and sort tie-breaking.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRestLibInMemory<TEntity, TKey>(
        this IServiceCollection services,
        Func<TEntity, TKey> keySelector,
        Func<TKey> keyGenerator,
        IComparer<TKey> keyComparer)
        where TEntity : class
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        ArgumentNullException.ThrowIfNull(keyGenerator);
        ArgumentNullException.ThrowIfNull(keyComparer);

        return RegisterRepository(
            services,
            serviceProvider => new InMemoryRepository<TEntity, TKey>(
                keySelector,
                keyGenerator,
                ResolveJsonOptions(serviceProvider),
                keyComparer));
    }

    /// <summary>
    /// Registers an in-memory repository with an explicit generated-key assigner.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="keySelector">Function to extract the key from an entity.</param>
    /// <param name="keyGenerator">Function to generate a new key for entity creation.</param>
    /// <param name="keyAssigner">
    /// Function that assigns a generated key and returns the entity instance to store.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRestLibInMemory<TEntity, TKey>(
        this IServiceCollection services,
        Func<TEntity, TKey> keySelector,
        Func<TKey> keyGenerator,
        Func<TEntity, TKey, TEntity> keyAssigner)
        where TEntity : class
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        ArgumentNullException.ThrowIfNull(keyGenerator);
        ArgumentNullException.ThrowIfNull(keyAssigner);

        return RegisterRepository(
            services,
            serviceProvider => new InMemoryRepository<TEntity, TKey>(
                keySelector,
                keyGenerator,
                ResolveJsonOptions(serviceProvider),
                keyAssigner));
    }

    /// <summary>
    /// Registers an in-memory repository with explicit generated-key assignment and key comparison.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="keySelector">Function to extract the key from an entity.</param>
    /// <param name="keyGenerator">Function to generate a new key for entity creation.</param>
    /// <param name="keyAssigner">
    /// Function that assigns a generated key and returns the entity instance to store.
    /// </param>
    /// <param name="keyComparer">Comparer used for default ordering and sort tie-breaking.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRestLibInMemory<TEntity, TKey>(
        this IServiceCollection services,
        Func<TEntity, TKey> keySelector,
        Func<TKey> keyGenerator,
        Func<TEntity, TKey, TEntity> keyAssigner,
        IComparer<TKey> keyComparer)
        where TEntity : class
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        ArgumentNullException.ThrowIfNull(keyGenerator);
        ArgumentNullException.ThrowIfNull(keyAssigner);
        ArgumentNullException.ThrowIfNull(keyComparer);

        return RegisterRepository(
            services,
            serviceProvider => new InMemoryRepository<TEntity, TKey>(
                keySelector,
                keyGenerator,
                ResolveJsonOptions(serviceProvider),
                keyAssigner,
                keyComparer));
    }

    /// <summary>
    /// Registers an in-memory repository for the specified entity type with custom JSON options.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="keySelector">Function to extract the key from an entity.</param>
    /// <param name="keyGenerator">Function to generate a new key for entity creation.</param>
    /// <param name="jsonOptions">JSON serializer options for patch operations.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRestLibInMemoryWithOptions<TEntity, TKey>(
        this IServiceCollection services,
        Func<TEntity, TKey> keySelector,
        Func<TKey> keyGenerator,
        JsonSerializerOptions jsonOptions)
        where TEntity : class
        where TKey : notnull
    {
        var repository = new InMemoryRepository<TEntity, TKey>(keySelector, keyGenerator, jsonOptions);
        return RegisterRepository(services, repository);
    }

    /// <summary>
    /// Registers an in-memory repository with custom JSON options and an explicit generated-key assigner.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="keySelector">Function to extract the key from an entity.</param>
    /// <param name="keyGenerator">Function to generate a new key for entity creation.</param>
    /// <param name="jsonOptions">JSON serializer options for patch operations.</param>
    /// <param name="keyAssigner">
    /// Function that assigns a generated key and returns the entity instance to store.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRestLibInMemoryWithOptions<TEntity, TKey>(
        this IServiceCollection services,
        Func<TEntity, TKey> keySelector,
        Func<TKey> keyGenerator,
        JsonSerializerOptions jsonOptions,
        Func<TEntity, TKey, TEntity> keyAssigner)
        where TEntity : class
        where TKey : notnull
    {
        var repository = new InMemoryRepository<TEntity, TKey>(keySelector, keyGenerator, jsonOptions, keyAssigner);
        return RegisterRepository(services, repository);
    }

    /// <summary>
    /// Registers an in-memory repository for the specified entity type with seeded data.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="keySelector">Function to extract the key from an entity.</param>
    /// <param name="keyGenerator">Function to generate a new key for entity creation.</param>
    /// <param name="seedData">Initial data to seed the repository with.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRestLibInMemoryWithData<TEntity, TKey>(
        this IServiceCollection services,
        Func<TEntity, TKey> keySelector,
        Func<TKey> keyGenerator,
        IEnumerable<TEntity> seedData)
        where TEntity : class
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        ArgumentNullException.ThrowIfNull(keyGenerator);
        ArgumentNullException.ThrowIfNull(seedData);
        var initialData = seedData.ToArray();

        return RegisterRepository(
            services,
            serviceProvider =>
            {
                var repository = new InMemoryRepository<TEntity, TKey>(
                    keySelector,
                    keyGenerator,
                    ResolveJsonOptions(serviceProvider));
                repository.Seed(initialData);
                return repository;
            });
    }

    /// <summary>
    /// Registers a seeded in-memory repository with an explicit generated-key assigner.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="keySelector">Function to extract the key from an entity.</param>
    /// <param name="keyGenerator">Function to generate a new key for entity creation.</param>
    /// <param name="seedData">Initial data to seed the repository with.</param>
    /// <param name="keyAssigner">
    /// Function that assigns a generated key and returns the entity instance to store.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRestLibInMemoryWithData<TEntity, TKey>(
        this IServiceCollection services,
        Func<TEntity, TKey> keySelector,
        Func<TKey> keyGenerator,
        IEnumerable<TEntity> seedData,
        Func<TEntity, TKey, TEntity> keyAssigner)
        where TEntity : class
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        ArgumentNullException.ThrowIfNull(keyGenerator);
        ArgumentNullException.ThrowIfNull(seedData);
        ArgumentNullException.ThrowIfNull(keyAssigner);
        var initialData = seedData.ToArray();

        return RegisterRepository(
            services,
            serviceProvider =>
            {
                var repository = new InMemoryRepository<TEntity, TKey>(
                    keySelector,
                    keyGenerator,
                    ResolveJsonOptions(serviceProvider),
                    keyAssigner);
                repository.Seed(initialData);
                return repository;
            });
    }

    /// <summary>
    /// Registers an in-memory repository for the specified entity type with seeded data and custom JSON options.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="keySelector">Function to extract the key from an entity.</param>
    /// <param name="keyGenerator">Function to generate a new key for entity creation.</param>
    /// <param name="seedData">Initial data to seed the repository with.</param>
    /// <param name="jsonOptions">JSON serializer options for patch operations.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRestLibInMemoryWithDataAndOptions<TEntity, TKey>(
        this IServiceCollection services,
        Func<TEntity, TKey> keySelector,
        Func<TKey> keyGenerator,
        IEnumerable<TEntity> seedData,
        JsonSerializerOptions jsonOptions)
        where TEntity : class
        where TKey : notnull
    {
        var repository = new InMemoryRepository<TEntity, TKey>(keySelector, keyGenerator, jsonOptions);
        repository.Seed(seedData);
        return RegisterRepository(services, repository);
    }

    /// <summary>
    /// Registers a seeded in-memory repository with custom JSON options and an explicit generated-key assigner.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="keySelector">Function to extract the key from an entity.</param>
    /// <param name="keyGenerator">Function to generate a new key for entity creation.</param>
    /// <param name="seedData">Initial data to seed the repository with.</param>
    /// <param name="jsonOptions">JSON serializer options for patch operations.</param>
    /// <param name="keyAssigner">
    /// Function that assigns a generated key and returns the entity instance to store.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRestLibInMemoryWithDataAndOptions<TEntity, TKey>(
        this IServiceCollection services,
        Func<TEntity, TKey> keySelector,
        Func<TKey> keyGenerator,
        IEnumerable<TEntity> seedData,
        JsonSerializerOptions jsonOptions,
        Func<TEntity, TKey, TEntity> keyAssigner)
        where TEntity : class
        where TKey : notnull
    {
        var repository = new InMemoryRepository<TEntity, TKey>(keySelector, keyGenerator, jsonOptions, keyAssigner);
        repository.Seed(seedData);
        return RegisterRepository(services, repository);
    }

    private static IServiceCollection RegisterRepository<TEntity, TKey>(
        IServiceCollection services,
        InMemoryRepository<TEntity, TKey> repository)
        where TEntity : class
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(repository);

        services.AddSingleton<IRepository<TEntity, TKey>>(repository);
        services.AddSingleton<IBatchRepository<TEntity, TKey>>(repository);
        services.AddSingleton<IConditionalWriteRepository<TEntity, TKey>>(repository);
        services.AddSingleton<ICountableRepository<TEntity, TKey>>(repository);
        services.AddSingleton<IQueryCountableRepository<TEntity, TKey>>(repository);
        services.AddSingleton(repository);
        return services;
    }

    private static IServiceCollection RegisterRepository<TEntity, TKey>(
        IServiceCollection services,
        Func<IServiceProvider, InMemoryRepository<TEntity, TKey>> repositoryFactory)
        where TEntity : class
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(repositoryFactory);

        var syncRoot = new object();
        InMemoryRepository<TEntity, TKey>? repository = null;

        InMemoryRepository<TEntity, TKey> ResolveRepository(IServiceProvider serviceProvider)
        {
            lock (syncRoot)
            {
                return repository ??= repositoryFactory(serviceProvider);
            }
        }

        services.AddSingleton<InMemoryRepository<TEntity, TKey>>(ResolveRepository);
        services.AddSingleton<IRepository<TEntity, TKey>>(ResolveRepository);
        services.AddSingleton<IBatchRepository<TEntity, TKey>>(ResolveRepository);
        services.AddSingleton<IConditionalWriteRepository<TEntity, TKey>>(ResolveRepository);
        services.AddSingleton<ICountableRepository<TEntity, TKey>>(ResolveRepository);
        services.AddSingleton<IQueryCountableRepository<TEntity, TKey>>(ResolveRepository);
        return services;
    }

    private static JsonSerializerOptions ResolveJsonOptions(IServiceProvider serviceProvider) =>
        serviceProvider.GetService<JsonSerializerOptions>() ?? RestLibJsonOptions.CreateDefault();
}
