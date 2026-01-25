using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RestLib.Abstractions;
using RestLib.Configuration;
using RestLib.Serialization;

namespace RestLib;

/// <summary>
/// Extension methods for registering RestLib services with the dependency injection container.
/// </summary>
public static class RestLibServiceExtensions
{
  /// <summary>
  /// Adds RestLib core services to the service collection.
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <param name="configure">Optional action to configure RestLib options.</param>
  /// <returns>The service collection for chaining.</returns>
  public static IServiceCollection AddRestLib(
      this IServiceCollection services,
      Action<RestLibOptions>? configure = null)
  {
    ArgumentNullException.ThrowIfNull(services);

    // Register options
    var options = new RestLibOptions();
    configure?.Invoke(options);

    services.TryAddSingleton(options);

    // Register JSON serializer options configured per RestLib settings
    var jsonOptions = RestLibJsonOptions.Create(options);
    services.TryAddSingleton(jsonOptions);

    // Configure HTTP JSON options for request deserialization (Minimal APIs)
    services.Configure<JsonOptions>(httpJsonOptions =>
    {
      httpJsonOptions.SerializerOptions.PropertyNamingPolicy = options.JsonNamingPolicy;
      httpJsonOptions.SerializerOptions.PropertyNameCaseInsensitive = true;
      httpJsonOptions.SerializerOptions.DefaultIgnoreCondition = options.OmitNullValues
            ? JsonIgnoreCondition.WhenWritingNull
            : JsonIgnoreCondition.Never;
    });

    return services;
  }

  /// <summary>
  /// Registers a repository implementation for the specified entity and key types.
  /// </summary>
  /// <typeparam name="TEntity">The entity type.</typeparam>
  /// <typeparam name="TKey">The key type.</typeparam>
  /// <typeparam name="TRepository">The repository implementation type.</typeparam>
  /// <param name="services">The service collection.</param>
  /// <param name="lifetime">The service lifetime. Defaults to Scoped.</param>
  /// <returns>The service collection for chaining.</returns>
  public static IServiceCollection AddRepository<TEntity, TKey, TRepository>(
      this IServiceCollection services,
      ServiceLifetime lifetime = ServiceLifetime.Scoped)
      where TEntity : class
      where TRepository : class, IRepository<TEntity, TKey>
  {
    ArgumentNullException.ThrowIfNull(services);

    var descriptor = new ServiceDescriptor(
        typeof(IRepository<TEntity, TKey>),
        typeof(TRepository),
        lifetime);

    services.Add(descriptor);

    return services;
  }

  /// <summary>
  /// Registers a repository instance for the specified entity and key types.
  /// </summary>
  /// <typeparam name="TEntity">The entity type.</typeparam>
  /// <typeparam name="TKey">The key type.</typeparam>
  /// <param name="services">The service collection.</param>
  /// <param name="implementationFactory">A factory that creates the repository instance.</param>
  /// <param name="lifetime">The service lifetime. Defaults to Scoped.</param>
  /// <returns>The service collection for chaining.</returns>
  public static IServiceCollection AddRepository<TEntity, TKey>(
      this IServiceCollection services,
      Func<IServiceProvider, IRepository<TEntity, TKey>> implementationFactory,
      ServiceLifetime lifetime = ServiceLifetime.Scoped)
      where TEntity : class
  {
    ArgumentNullException.ThrowIfNull(services);
    ArgumentNullException.ThrowIfNull(implementationFactory);

    var descriptor = new ServiceDescriptor(
        typeof(IRepository<TEntity, TKey>),
        implementationFactory,
        lifetime);

    services.Add(descriptor);

    return services;
  }
}
