using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestLib.Configuration;

namespace RestLib.EntityFrameworkCore.Tests.Fakes;

/// <summary>
/// Shared builder that eliminates duplicated test-host setup across EF Core
/// integration test classes. Creates an <see cref="IHost"/> with <see cref="TestServer"/>,
/// registers EF Core-backed RestLib services, and provides access to
/// <see cref="TestDbContext"/> for seeding and verification.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
public sealed class EfCoreTestHostBuilder<TEntity, TKey>
    where TEntity : class
    where TKey : notnull
{
    private readonly string _route;
    private Action<RestLibOptions>? _configureOptions;
    private Action<RestLibEndpointConfiguration<TEntity, TKey>>? _configureEndpoint;
    private Action<IServiceCollection>? _configureServices;
    private Action<EfCoreRepositoryOptions<TEntity, TKey>>? _configureRepositoryOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="EfCoreTestHostBuilder{TEntity, TKey}"/> class.
    /// </summary>
    /// <param name="route">The route prefix for the REST endpoints (e.g., "/api/products").</param>
    public EfCoreTestHostBuilder(string route)
    {
        _route = route;
    }

    /// <summary>
    /// Configures RestLib options (e.g. EnableETagSupport, pagination limits).
    /// </summary>
    /// <param name="configure">Action to configure <see cref="RestLibOptions"/>.</param>
    /// <returns>This builder for chaining.</returns>
    public EfCoreTestHostBuilder<TEntity, TKey> WithOptions(Action<RestLibOptions> configure)
    {
        _configureOptions = configure;
        return this;
    }

    /// <summary>
    /// Configures the endpoint (e.g. AllowAnonymous, AllowSorting, AllowFiltering).
    /// </summary>
    /// <param name="configure">Action to configure <see cref="RestLibEndpointConfiguration{TEntity, TKey}"/>.</param>
    /// <returns>This builder for chaining.</returns>
    public EfCoreTestHostBuilder<TEntity, TKey> WithEndpoint(Action<RestLibEndpointConfiguration<TEntity, TKey>> configure)
    {
        _configureEndpoint = configure;
        return this;
    }

    /// <summary>
    /// Registers additional services beyond the default RestLib and EF Core registrations.
    /// </summary>
    /// <param name="configure">Action to configure additional services.</param>
    /// <returns>This builder for chaining.</returns>
    public EfCoreTestHostBuilder<TEntity, TKey> WithServices(Action<IServiceCollection> configure)
    {
        _configureServices = configure;
        return this;
    }

    /// <summary>
    /// Configures EF Core repository options (e.g. KeySelector, UseAsNoTracking).
    /// </summary>
    /// <param name="configure">Action to configure <see cref="EfCoreRepositoryOptions{TEntity, TKey}"/>.</param>
    /// <returns>This builder for chaining.</returns>
    public EfCoreTestHostBuilder<TEntity, TKey> WithRepositoryOptions(Action<EfCoreRepositoryOptions<TEntity, TKey>> configure)
    {
        _configureRepositoryOptions = configure;
        return this;
    }

    /// <summary>
    /// Builds and starts the test host, returning the host, an <see cref="HttpClient"/>,
    /// and a <see cref="TestDbContext"/> for seeding and verification.
    /// </summary>
    /// <returns>A tuple of the started host, HTTP client, and test database context.</returns>
    public async Task<(IHost Host, HttpClient Client, TestDbContext DbContext)> BuildAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRestLib(_configureOptions ?? (_ => { }));
                        services.AddSingleton(connection);
                        services.AddDbContext<TestDbContext>(options => options.UseSqlite(connection));

                        if (_configureRepositoryOptions is not null)
                        {
                            services.AddRestLibEfCore<TestDbContext, TEntity, TKey>(_configureRepositoryOptions);
                        }
                        else
                        {
                            services.AddRestLibEfCore<TestDbContext, TEntity, TKey>();
                        }

                        services.AddRouting();
                        _configureServices?.Invoke(services);
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapRestLib<TEntity, TKey>(_route, _configureEndpoint ?? (_ => { }));
                        });
                    });
            })
            .Build();

        await host.StartAsync();

        using (var initializationScope = host.Services.CreateScope())
        {
            var initializationContext = initializationScope.ServiceProvider.GetRequiredService<TestDbContext>();
            await initializationContext.Database.EnsureCreatedAsync();
        }

        var testScope = host.Services.CreateScope();
        var testDbContext = testScope.ServiceProvider.GetRequiredService<TestDbContext>();
        var wrappedHost = new ScopedHost(host, testScope);

        return (wrappedHost, host.GetTestClient(), testDbContext);
    }

    private sealed class ScopedHost : IHost
    {
        private readonly IHost _innerHost;
        private readonly IServiceScope _scope;

        /// <summary>
        /// Initializes a new instance of the <see cref="ScopedHost"/> class.
        /// </summary>
        /// <param name="innerHost">The inner host.</param>
        /// <param name="scope">The scope kept alive for the returned test DbContext.</param>
        public ScopedHost(IHost innerHost, IServiceScope scope)
        {
            _innerHost = innerHost;
            _scope = scope;
        }

        /// <inheritdoc />
        public IServiceProvider Services => _innerHost.Services;

        /// <inheritdoc />
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            await _innerHost.StartAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            await _innerHost.StopAsync(cancellationToken);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _scope.Dispose();
            _innerHost.Dispose();
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
