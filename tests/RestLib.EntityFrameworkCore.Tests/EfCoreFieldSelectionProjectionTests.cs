using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RestLib.Configuration;
using RestLib.EntityFrameworkCore.Tests.Fakes;
using RestLib.FieldSelection;
using RestLib.Filtering;
using RestLib.Pagination;
using RestLib.Serialization;
using RestLib.Sorting;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

/// <summary>
/// Tests EF Core field-selection projection pushdown behavior.
/// </summary>
[Trait("Category", "IMP-2")]
public class EfCoreFieldSelectionProjectionTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = RestLibJsonOptions.CreateDefault();
    private readonly List<string> _sqlLogMessages = [];

    private IHost _host = null!;
    private HttpClient _client = null!;
    private TestDbContext _db = null!;

    public async Task InitializeAsync()
    {
        (_host, _client, _db) = await new EfCoreTestHostBuilder<ProductEntity, Guid>("/api/products")
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.AllowFieldSelection(
                    p => p.Id,
                    p => p.ProductName,
                    p => p.UnitPrice,
                    p => p.StockQuantity,
                    p => p.CreatedAt,
                    p => p.OptionalDescription,
                    p => p.IsActive);
                config.AllowSorting(p => p.UnitPrice, p => p.ProductName);
                config.AllowFiltering(p => p.IsActive);
            })
            .WithRepositoryOptions(options =>
            {
                options.EnableProjectionPushdown = true;
                options.KeySelector = product => product.Id;
            })
            .WithServices(services =>
            {
                services.AddLogging(builder =>
                {
                    builder.AddProvider(new ListLoggerProvider(_sqlLogMessages));
                });
            })
            .BuildAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task GetAll_WithProjectedFields_ReturnsOnlyRequestedFields()
    {
        // Arrange
        await SeedProductsAsync(CreateProduct(name: "Projected", unitPrice: 42m, stockQuantity: 7, isActive: true));

        // Act
        var response = await _client.GetAsync("/api/products?fields=product_name");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var firstItem = json.GetProperty("items")[0];
        firstItem.TryGetProperty("product_name", out _).Should().BeTrue();
        firstItem.TryGetProperty("id", out _).Should().BeFalse();
        firstItem.TryGetProperty("unit_price", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GetById_WithProjectedFields_ReturnsOnlyRequestedFields()
    {
        // Arrange
        var product = CreateProduct(name: "Single Projected", unitPrice: 11m, stockQuantity: 2, isActive: true);
        await SeedProductsAsync(product);

        // Act
        var response = await _client.GetAsync($"/api/products/{product.Id}?fields=product_name");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("product_name", out _).Should().BeTrue();
        json.TryGetProperty("id", out _).Should().BeFalse();
        json.TryGetProperty("unit_price", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GetAll_WithFilterAndSortExcludedFromResponse_StillReturnsProjectedShape()
    {
        // Arrange
        await SeedProductsAsync(
            CreateProduct(name: "B", unitPrice: 20m, stockQuantity: 1, isActive: true),
            CreateProduct(name: "A", unitPrice: 10m, stockQuantity: 2, isActive: true),
            CreateProduct(name: "C", unitPrice: 30m, stockQuantity: 3, isActive: false));

        // Act
        var response = await _client.GetAsync("/api/products?fields=product_name&is_active=true&sort=unit_price:asc");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(2);
        items[0].GetProperty("product_name").GetString().Should().Be("A");
        items[1].GetProperty("product_name").GetString().Should().Be("B");
        items[0].TryGetProperty("unit_price", out _).Should().BeFalse();
        items[0].TryGetProperty("is_active", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GetAll_WithProjectionPushdown_SelectsRequestedPlusRequiredColumnsOnly()
    {
        // Arrange
        await SeedProductsAsync(
            CreateProduct(name: "B", unitPrice: 20m, stockQuantity: 1, isActive: true),
            CreateProduct(name: "A", unitPrice: 10m, stockQuantity: 2, isActive: true),
            CreateProduct(name: "C", unitPrice: 30m, stockQuantity: 3, isActive: false));
        _sqlLogMessages.Clear();

        // Act
        var response = await _client.GetAsync("/api/products?fields=product_name&is_active=true&sort=unit_price:asc");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var sql = string.Join("\n", _sqlLogMessages);

        // Assert
        sql.Should().Contain("\"Id\"");
        sql.Should().Contain("\"ProductName\"");
        sql.Should().Contain("\"UnitPrice\"");
        sql.Should().Contain("\"IsActive\"");
        sql.Should().NotContain("\"StockQuantity\"");
        sql.Should().NotContain("\"OptionalDescription\"");
    }

    [Fact]
    public async Task RepositoryProjection_WithNonProjectableRequestedField_ReturnsNull()
    {
        // Arrange
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BlobProjectionTestDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new BlobProjectionTestDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var repository = new EfCoreRepository<BlobProjectionTestDbContext, ProductWithBlobEntity, Guid>(
            context,
            new EfCoreRepositoryOptions<ProductWithBlobEntity, Guid>
            {
                KeySelector = entity => entity.Id,
                EnableProjectionPushdown = true
            });
        var selectedFields = new SelectedField[]
        {
            new() { PropertyName = nameof(ProductWithBlobEntity.Blob), QueryParameterName = "blob" }
        };

        // Act
        var result = await repository.GetAllProjectedAsync(new PaginationRequest { Limit = 10 }, selectedFields);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAll_WithNonProjectableRequestedField_FallsBackToPostFetchProjection()
    {
        // Arrange
        await using var harness = await BlobProjectionHostHarness.BuildAsync();
        harness.DbContext.Products.Add(new ProductWithBlobEntity
        {
            Id = Guid.NewGuid(),
            Name = "Blob Product",
            Blob = [1, 2, 3]
        });
        await harness.DbContext.SaveChangesAsync();

        // Act
        var response = await harness.Client.GetAsync("/api/blob-products?fields=name,blob");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var firstItem = json.GetProperty("items")[0];
        firstItem.GetProperty("name").GetString().Should().Be("Blob Product");
        firstItem.TryGetProperty("blob", out var blob).Should().BeTrue();
        blob.GetString().Should().NotBeNullOrEmpty();
    }

    private static ProductEntity CreateProduct(
        string name,
        decimal unitPrice,
        int stockQuantity,
        bool isActive)
    {
        return new ProductEntity
        {
            Id = Guid.NewGuid(),
            ProductName = name,
            UnitPrice = unitPrice,
            StockQuantity = stockQuantity,
            CreatedAt = DateTime.UtcNow,
            OptionalDescription = "desc",
            IsActive = isActive
        };
    }

    private async Task SeedProductsAsync(params ProductEntity[] products)
    {
        _db.Products.AddRange(products);
        await _db.SaveChangesAsync();
    }

    private sealed class ListLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _messages;

        public ListLoggerProvider(List<string> messages)
        {
            _messages = messages;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new ListLogger(_messages);
        }

        public void Dispose()
        {
        }
    }

    private sealed class ListLogger : ILogger
    {
        private readonly List<string> _messages;

        public ListLogger(List<string> messages)
        {
            _messages = messages;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _messages.Add(formatter(state, exception));
        }
    }

    private sealed class BlobProjectionTestDbContext : DbContext
    {
        public BlobProjectionTestDbContext(DbContextOptions<BlobProjectionTestDbContext> options)
            : base(options)
        {
        }

        public DbSet<ProductWithBlobEntity> Products => Set<ProductWithBlobEntity>();
    }

    private sealed class ProductWithBlobEntity
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public byte[] Blob { get; set; } = [];
    }

    private sealed class BlobProjectionHostHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly IServiceScope _scope;

        private BlobProjectionHostHarness(
            IHost host,
            HttpClient client,
            BlobProjectionTestDbContext dbContext,
            SqliteConnection connection,
            IServiceScope scope)
        {
            Host = host;
            Client = client;
            DbContext = dbContext;
            _connection = connection;
            _scope = scope;
        }

        public IHost Host { get; }

        public HttpClient Client { get; }

        public BlobProjectionTestDbContext DbContext { get; }

        public static async Task<BlobProjectionHostHarness> BuildAsync()
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
                            services.AddRestLib();
                            services.AddSingleton(connection);
                            services.AddDbContext<BlobProjectionTestDbContext>(options => options.UseSqlite(connection));
                            services.AddRestLibEfCore<BlobProjectionTestDbContext, ProductWithBlobEntity, Guid>(options =>
                            {
                                options.KeySelector = entity => entity.Id;
                                options.EnableProjectionPushdown = true;
                            });
                            services.AddRouting();
                        })
                        .Configure(app =>
                        {
                            app.UseRouting();
                            app.UseEndpoints(endpoints =>
                            {
                                endpoints.MapRestLib<ProductWithBlobEntity, Guid>("/api/blob-products", config =>
                                {
                                    config.AllowAnonymous();
                                    config.AllowFieldSelection(p => p.Id, p => p.Name, p => p.Blob);
                                });
                            });
                        });
                })
                .Build();

            await host.StartAsync();

            using (var initializationScope = host.Services.CreateScope())
            {
                var initializationContext = initializationScope.ServiceProvider.GetRequiredService<BlobProjectionTestDbContext>();
                await initializationContext.Database.EnsureCreatedAsync();
            }

            var scope = host.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<BlobProjectionTestDbContext>();

            return new BlobProjectionHostHarness(host, host.GetTestClient(), dbContext, connection, scope);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            _scope.Dispose();
            await Host.StopAsync();
            Host.Dispose();
            await _connection.DisposeAsync();
        }
    }
}
