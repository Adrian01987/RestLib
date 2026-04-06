using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestLib.Abstractions;
using RestLib.Configuration;

namespace RestLib.Tests.Fakes;

/// <summary>
/// Shared builder that eliminates duplicated test-host setup across integration test classes.
/// Supports the common pattern: register RestLib + repository, configure endpoints, start TestServer.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
public sealed class TestHostBuilder<TEntity, TKey>
    where TEntity : class
    where TKey : notnull
{
    private readonly IRepository<TEntity, TKey> _repository;
    private readonly string _route;
    private Action<RestLibOptions>? _configureOptions;
    private Action<RestLibEndpointConfiguration<TEntity, TKey>>? _configureEndpoint;
    private Action<IServiceCollection>? _configureServices;
    private Action<IApplicationBuilder>? _configureMiddleware;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestHostBuilder{TEntity, TKey}"/> class.
    /// </summary>
    /// <param name="repository">The repository instance to register.</param>
    /// <param name="route">The route prefix for the REST endpoints.</param>
    public TestHostBuilder(IRepository<TEntity, TKey> repository, string route)
    {
        _repository = repository;
        _route = route;
    }

    /// <summary>
    /// Configures RestLib options (e.g. EnableETagSupport, pagination limits).
    /// </summary>
    /// <param name="configure">Action to configure <see cref="RestLibOptions"/>.</param>
    /// <returns>This builder for chaining.</returns>
    public TestHostBuilder<TEntity, TKey> WithOptions(Action<RestLibOptions> configure)
    {
        _configureOptions = configure;
        return this;
    }

    /// <summary>
    /// Configures the endpoint (e.g. AllowAnonymous, AllowSorting, AllowFiltering).
    /// </summary>
    /// <param name="configure">Action to configure <see cref="RestLibEndpointConfiguration{TEntity, TKey}"/>.</param>
    /// <returns>This builder for chaining.</returns>
    public TestHostBuilder<TEntity, TKey> WithEndpoint(Action<RestLibEndpointConfiguration<TEntity, TKey>> configure)
    {
        _configureEndpoint = configure;
        return this;
    }

    /// <summary>
    /// Registers additional services (e.g. authentication, rate limiting).
    /// </summary>
    /// <param name="configure">Action to configure additional services.</param>
    /// <returns>This builder for chaining.</returns>
    public TestHostBuilder<TEntity, TKey> WithServices(Action<IServiceCollection> configure)
    {
        _configureServices = configure;
        return this;
    }

    /// <summary>
    /// Adds middleware between UseRouting and UseEndpoints (e.g. UseAuthentication, UseRateLimiter).
    /// </summary>
    /// <param name="configure">Action to configure additional middleware.</param>
    /// <returns>This builder for chaining.</returns>
    public TestHostBuilder<TEntity, TKey> WithMiddleware(Action<IApplicationBuilder> configure)
    {
        _configureMiddleware = configure;
        return this;
    }

    /// <summary>
    /// Builds and starts the test host, returning the host and an <see cref="HttpClient"/>.
    /// </summary>
    /// <returns>A tuple of the started host and HTTP client.</returns>
    public (IHost Host, HttpClient Client) Build()
    {
        var host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRestLib(_configureOptions ?? (_ => { }));
                        services.AddSingleton<IRepository<TEntity, TKey>>(_repository);
                        services.AddRouting();
                        _configureServices?.Invoke(services);
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        _configureMiddleware?.Invoke(app);
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapRestLib<TEntity, TKey>(_route, _configureEndpoint ?? (_ => { }));
                        });
                    });
            })
            .Build();

        host.Start();
        return (host, host.GetTestClient());
    }
}
