using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
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
[Trait("Category", "Story7.1.Projection")]
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
    public async Task GetAll_PushdownEnabled_NestedSelection_FallsBackToFullMaterialization()
    {
        // Arrange
        var sqlLogMessages = new List<string>();
        var (host, client, dbContext) = await CreateOrderHostAsync(sqlLogMessages);

        try
        {
            await SeedOrdersAsync(dbContext);
            sqlLogMessages.Clear();

            // Act
            var response = await client.GetAsync("/api/orders?fields=order_number,customer.email");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var firstItem = json.GetProperty("items")[0];
            firstItem.TryGetProperty("order_number", out _).Should().BeTrue();
            firstItem.TryGetProperty("customer.email", out _).Should().BeTrue();

            var sql = string.Join("\n", sqlLogMessages);
            sql.Should().Contain("\"TotalAmount\"");
            sql.Should().Contain("\"CustomerId\"");
        }
        finally
        {
            await DisposeHostAsync(host, client);
        }
    }

    [Fact]
    public async Task GetById_PushdownEnabled_WithETag_FallsBackToFullMaterialization()
    {
        // Arrange
        var sqlLogMessages = new List<string>();
        var (host, client, dbContext) = await CreateProductHostAsync(
            sqlLogMessages,
            configureOptions: options => options.EnableETagSupport = true,
            configureRepositoryOptions: options => options.EnableProjectionPushdown = true);

        try
        {
            var product = CreateProduct(name: "ETag Fallback", unitPrice: 12m, stockQuantity: 4, isActive: true);
            await SeedProductsAsync(dbContext, product);
            sqlLogMessages.Clear();

            // Act
            var response = await client.GetAsync($"/api/products/{product.Id}?fields=product_name");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var sql = string.Join("\n", sqlLogMessages);
            sql.Should().Contain("\"StockQuantity\"");
            sql.Should().Contain("\"OptionalDescription\"");
        }
        finally
        {
            await DisposeHostAsync(host, client);
        }
    }

    [Fact]
    public async Task GetAll_PushdownEnabled_WithHateoas_FallsBackToFullMaterialization()
    {
        // Arrange
        var sqlLogMessages = new List<string>();
        var (host, client, dbContext) = await CreateProductHostAsync(
            sqlLogMessages,
            configureOptions: options => options.EnableHateoas = true,
            configureRepositoryOptions: options => options.EnableProjectionPushdown = true);

        try
        {
            await SeedProductsAsync(dbContext, CreateProduct(name: "HATEOAS Fallback", unitPrice: 13m, stockQuantity: 5, isActive: true));
            sqlLogMessages.Clear();

            // Act
            var response = await client.GetAsync("/api/products?fields=product_name");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var sql = string.Join("\n", sqlLogMessages);
            sql.Should().Contain("\"StockQuantity\"");
            sql.Should().Contain("\"OptionalDescription\"");
        }
        finally
        {
            await DisposeHostAsync(host, client);
        }
    }

    [Fact]
    public async Task GetAll_PushdownEnabled_WithHooks_FallsBackToFullMaterialization()
    {
        // Arrange
        var sqlLogMessages = new List<string>();
        var (host, client, dbContext) = await CreateProductHostAsync(
            sqlLogMessages,
            configureEndpoint: config => config.UseHooks(hooks => hooks.OnRequestReceived = _ => Task.CompletedTask),
            configureRepositoryOptions: options => options.EnableProjectionPushdown = true);

        try
        {
            await SeedProductsAsync(dbContext, CreateProduct(name: "Hook Fallback", unitPrice: 14m, stockQuantity: 6, isActive: true));
            sqlLogMessages.Clear();

            // Act
            var response = await client.GetAsync("/api/products?fields=product_name");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var sql = string.Join("\n", sqlLogMessages);
            sql.Should().Contain("\"StockQuantity\"");
            sql.Should().Contain("\"OptionalDescription\"");
        }
        finally
        {
            await DisposeHostAsync(host, client);
        }
    }

    [Fact]
    public async Task GetAll_PushdownDisabledByDefault_BehaviorUnchanged()
    {
        // Arrange
        var sqlLogMessages = new List<string>();
        var (host, client, dbContext) = await CreateProductHostAsync(sqlLogMessages);

        try
        {
            await SeedProductsAsync(dbContext, CreateProduct(name: "Default Disabled", unitPrice: 15m, stockQuantity: 8, isActive: true));
            sqlLogMessages.Clear();

            // Act
            var response = await client.GetAsync("/api/products?fields=product_name");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var firstItem = json.GetProperty("items")[0];
            firstItem.TryGetProperty("product_name", out _).Should().BeTrue();
            firstItem.TryGetProperty("stock_quantity", out _).Should().BeFalse();

            var sql = string.Join("\n", sqlLogMessages);
            sql.Should().Contain("\"StockQuantity\"");
            sql.Should().Contain("\"OptionalDescription\"");
        }
        finally
        {
            await DisposeHostAsync(host, client);
        }
    }

    [Fact]
    public async Task GetAll_PushdownEnabled_NoSelection_ReturnsAllColumns_NoFallbackError()
    {
        // Arrange
        await SeedProductsAsync(CreateProduct(name: "No Selection", unitPrice: 16m, stockQuantity: 9, isActive: true));
        _sqlLogMessages.Clear();

        // Act
        var response = await _client.GetAsync("/api/products");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var firstItem = json.GetProperty("items")[0];
        firstItem.TryGetProperty("product_name", out _).Should().BeTrue();
        firstItem.TryGetProperty("stock_quantity", out _).Should().BeTrue();

        var sql = string.Join("\n", _sqlLogMessages);
        sql.Should().Contain("\"StockQuantity\"");
        sql.Should().Contain("\"OptionalDescription\"");
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

    private static async Task<(IHost Host, HttpClient Client, TestDbContext DbContext)> CreateProductHostAsync(
        List<string> sqlLogMessages,
        Action<RestLibOptions>? configureOptions = null,
        Action<RestLibEndpointConfiguration<ProductEntity, Guid>>? configureEndpoint = null,
        Action<EfCoreRepositoryOptions<ProductEntity, Guid>>? configureRepositoryOptions = null)
    {
        var builder = new EfCoreTestHostBuilder<ProductEntity, Guid>("/api/products")
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.AllowFieldSelection(
                    product => product.Id,
                    product => product.ProductName,
                    product => product.UnitPrice,
                    product => product.StockQuantity,
                    product => product.CreatedAt,
                    product => product.OptionalDescription,
                    product => product.IsActive);
                config.AllowSorting(product => product.UnitPrice, product => product.ProductName);
                config.AllowFiltering(product => product.IsActive);
                configureEndpoint?.Invoke(config);
            })
            .WithRepositoryOptions(options =>
            {
                options.KeySelector = product => product.Id;
                configureRepositoryOptions?.Invoke(options);
            })
            .WithServices(services =>
            {
                services.AddLogging(builder =>
                {
                    builder.AddProvider(new ListLoggerProvider(sqlLogMessages));
                });
            });

        if (configureOptions is not null)
        {
            builder.WithOptions(configureOptions);
        }

        return await builder.BuildAsync();
    }

    private static async Task<(IHost Host, HttpClient Client, TestDbContext DbContext)> CreateOrderHostAsync(
        List<string> sqlLogMessages)
    {
        return await new EfCoreTestHostBuilder<OrderEntity, Guid>("/api/orders")
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.AllowFieldSelection(
                    order => order.Id,
                    order => order.OrderNumber,
                    order => order.Customer!.Email);
                config.AllowSorting(order => order.OrderNumber);
            })
            .WithRepositoryOptions(options =>
            {
                options.EnableProjectionPushdown = true;
                options.KeySelector = order => order.Id;
            })
            .WithServices(services =>
            {
                services.AddLogging(builder =>
                {
                    builder.AddProvider(new ListLoggerProvider(sqlLogMessages));
                });
            })
            .BuildAsync();
    }

    private static async Task SeedProductsAsync(TestDbContext dbContext, params ProductEntity[] products)
    {
        dbContext.Products.AddRange(products);
        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedOrdersAsync(TestDbContext dbContext)
    {
        var customer = new OrderCustomerEntity
        {
            Id = Guid.NewGuid(),
            Name = "Ada",
            Email = "ada@example.com"
        };

        dbContext.Customers.Add(customer);
        dbContext.Orders.Add(new OrderEntity
        {
            Id = Guid.NewGuid(),
            OrderNumber = "ORD-101",
            TotalAmount = 42m,
            CustomerId = customer.Id,
            Customer = customer
        });

        await dbContext.SaveChangesAsync();
    }

    private static async Task DisposeHostAsync(IHost host, HttpClient client)
    {
        client.Dispose();
        await host.StopAsync();
        host.Dispose();
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
}
