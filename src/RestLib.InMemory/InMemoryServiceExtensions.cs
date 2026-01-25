using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RestLib.Abstractions;

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
    var repository = new InMemoryRepository<TEntity, TKey>(keySelector, keyGenerator, null);
    services.AddSingleton<IRepository<TEntity, TKey>>(repository);
    services.AddSingleton(repository);
    return services;
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
    services.AddSingleton<IRepository<TEntity, TKey>>(repository);
    services.AddSingleton(repository);
    return services;
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
    var repository = new InMemoryRepository<TEntity, TKey>(keySelector, keyGenerator);
    repository.Seed(seedData);
    services.AddSingleton<IRepository<TEntity, TKey>>(repository);
    services.AddSingleton(repository);
    return services;
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
    services.AddSingleton<IRepository<TEntity, TKey>>(repository);
    services.AddSingleton(repository);
    return services;
  }
}
