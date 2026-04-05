using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RestLib.Abstractions;
using RestLib.Configuration;
using RestLib.Hooks;
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

    ValidateOptions(options);

    services.TryAddSingleton(options);

    // Register endpoint name registry for unique OpenAPI operation IDs
    services.TryAddSingleton(new EndpointNameRegistry());

    // Register tag description registry and document transformer
    var tagDescriptionRegistry = new TagDescriptionRegistry();
    services.TryAddSingleton(tagDescriptionRegistry);
    services.ConfigureAll<OpenApiOptions>(openApiOptions =>
    {
        openApiOptions.AddDocumentTransformer(new TagDescriptionDocumentTransformer(tagDescriptionRegistry));
    });

    // Register JSON serializer options configured per RestLib settings
    var jsonOptions = RestLibJsonOptions.Create(options);
    services.TryAddSingleton(jsonOptions);
    services.TryAddSingleton(new RestLibJsonResourceRegistry());

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
  /// Registers a JSON-backed RestLib resource definition for a typed entity.
  /// </summary>
  /// <typeparam name="TEntity">The entity type.</typeparam>
  /// <typeparam name="TKey">The key type.</typeparam>
  /// <param name="services">The service collection.</param>
  /// <param name="configuration">The JSON resource configuration.</param>
  /// <returns>The service collection for chaining.</returns>
  public static IServiceCollection AddJsonResource<TEntity, TKey>(
      this IServiceCollection services,
      RestLibJsonResourceConfiguration configuration)
      where TEntity : class
  {
    ArgumentNullException.ThrowIfNull(services);
    ArgumentNullException.ThrowIfNull(configuration);

    var registry = GetOrCreateJsonResourceRegistry(services);
    registry.Add(configuration.Name, endpoints =>
    {
      endpoints.MapRestLib<TEntity, TKey>(configuration.Route, config =>
      {
        RestLibJsonResourceBuilder.Apply(config, configuration);

        var hooks = RestLibJsonResourceBuilder.BuildHooks<TEntity, TKey>(endpoints.ServiceProvider, configuration.Hooks);
        if (hooks is not null)
        {
          config.UseHooks(existingHooks =>
          {
            existingHooks.OnRequestReceived = hooks.OnRequestReceived;
            existingHooks.OnRequestValidated = hooks.OnRequestValidated;
            existingHooks.BeforePersist = hooks.BeforePersist;
            existingHooks.AfterPersist = hooks.AfterPersist;
            existingHooks.BeforeResponse = hooks.BeforeResponse;
            existingHooks.OnError = hooks.OnError;
          });
        }
      });
    });

    return services;
  }

  /// <summary>
  /// Loads a JSON resource configuration section and registers a typed RestLib resource.
  /// </summary>
  /// <typeparam name="TEntity">The entity type.</typeparam>
  /// <typeparam name="TKey">The key type.</typeparam>
  /// <param name="services">The service collection.</param>
  /// <param name="configurationSection">The configuration section containing a resource definition.</param>
  /// <returns>The service collection for chaining.</returns>
  public static IServiceCollection AddJsonResource<TEntity, TKey>(
      this IServiceCollection services,
      IConfigurationSection configurationSection)
      where TEntity : class
  {
    ArgumentNullException.ThrowIfNull(services);
    ArgumentNullException.ThrowIfNull(configurationSection);

    var configuration = configurationSection.Get<RestLibJsonResourceConfiguration>()
                        ?? throw new InvalidOperationException(
                            $"Configuration section '{configurationSection.Path}' does not contain a valid RestLib resource definition.");

    return services.AddJsonResource<TEntity, TKey>(configuration);
  }

  /// <summary>
  /// Registers a named standard hook for JSON-backed resource configuration.
  /// </summary>
  /// <typeparam name="TEntity">The entity type.</typeparam>
  /// <typeparam name="TKey">The key type.</typeparam>
  /// <param name="services">The service collection.</param>
  /// <param name="name">The unique hook name.</param>
  /// <param name="hook">The hook delegate.</param>
  /// <returns>The service collection for chaining.</returns>
  public static IServiceCollection AddNamedHook<TEntity, TKey>(
      this IServiceCollection services,
      string name,
      RestLibHookDelegate<TEntity, TKey> hook)
      where TEntity : class
  {
    ArgumentNullException.ThrowIfNull(services);
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ArgumentNullException.ThrowIfNull(hook);

    var resolver = GetOrCreateNamedHookResolver<TEntity, TKey>(services);
    resolver.Add(name, hook);

    return services;
  }

  /// <summary>
  /// Registers a named error hook for JSON-backed resource configuration.
  /// </summary>
  /// <typeparam name="TEntity">The entity type.</typeparam>
  /// <typeparam name="TKey">The key type.</typeparam>
  /// <param name="services">The service collection.</param>
  /// <param name="name">The unique hook name.</param>
  /// <param name="hook">The error hook delegate.</param>
  /// <returns>The service collection for chaining.</returns>
  public static IServiceCollection AddNamedErrorHook<TEntity, TKey>(
      this IServiceCollection services,
      string name,
      RestLibErrorHookDelegate<TEntity, TKey> hook)
      where TEntity : class
  {
    ArgumentNullException.ThrowIfNull(services);
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ArgumentNullException.ThrowIfNull(hook);

    var resolver = GetOrCreateNamedHookResolver<TEntity, TKey>(services);
    resolver.AddError(name, hook);

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

  private static RestLibJsonResourceRegistry GetOrCreateJsonResourceRegistry(IServiceCollection services)
  {
    var descriptor = services.LastOrDefault(d =>
        d.ServiceType == typeof(RestLibJsonResourceRegistry) &&
        d.ImplementationInstance is RestLibJsonResourceRegistry);

    if (descriptor?.ImplementationInstance is RestLibJsonResourceRegistry existingRegistry)
    {
      return existingRegistry;
    }

    var registry = new RestLibJsonResourceRegistry();
    services.TryAddSingleton(registry);
    return registry;
  }

  private static RestLibNamedHookResolver<TEntity, TKey> GetOrCreateNamedHookResolver<TEntity, TKey>(
      IServiceCollection services)
      where TEntity : class
  {
    var descriptor = services.LastOrDefault(d =>
        d.ServiceType == typeof(RestLibNamedHookResolver<TEntity, TKey>) &&
        d.ImplementationInstance is RestLibNamedHookResolver<TEntity, TKey>);

    if (descriptor?.ImplementationInstance is RestLibNamedHookResolver<TEntity, TKey> existingResolver)
    {
      return existingResolver;
    }

    var resolver = new RestLibNamedHookResolver<TEntity, TKey>();
    services.TryAddSingleton(resolver);
    services.TryAddSingleton<IRestLibNamedHookResolver<TEntity, TKey>>(sp =>
        sp.GetRequiredService<RestLibNamedHookResolver<TEntity, TKey>>());
    return resolver;
  }

  /// <summary>
  /// Validates <see cref="RestLibOptions"/> and throws <see cref="InvalidOperationException"/>
  /// if any values are out of range.
  /// </summary>
  private static void ValidateOptions(RestLibOptions options)
  {
    if (options.DefaultPageSize <= 0)
    {
      throw new InvalidOperationException(
          $"RestLibOptions.DefaultPageSize must be greater than 0. Current value: {options.DefaultPageSize}.");
    }

    if (options.MaxPageSize <= 0)
    {
      throw new InvalidOperationException(
          $"RestLibOptions.MaxPageSize must be greater than 0. Current value: {options.MaxPageSize}.");
    }

    if (options.DefaultPageSize > options.MaxPageSize)
    {
      throw new InvalidOperationException(
          $"RestLibOptions.DefaultPageSize ({options.DefaultPageSize}) must not exceed MaxPageSize ({options.MaxPageSize}).");
    }

    if (options.MaxBatchSize < 0)
    {
      throw new InvalidOperationException(
          $"RestLibOptions.MaxBatchSize must be 0 or greater. Current value: {options.MaxBatchSize}.");
    }

    if (options.MaxFilterInListSize <= 0)
    {
      throw new InvalidOperationException(
          $"RestLibOptions.MaxFilterInListSize must be greater than 0. Current value: {options.MaxFilterInListSize}.");
    }
  }
}
