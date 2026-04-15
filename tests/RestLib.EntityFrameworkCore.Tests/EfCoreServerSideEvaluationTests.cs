using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RestLib.EntityFrameworkCore.Tests.Fakes;
using RestLib.Filtering;
using RestLib.Pagination;
using RestLib.Sorting;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

/// <summary>
/// Verifies that all filtering, sorting, and pagination queries execute entirely
/// server-side with no EF Core client-side evaluation warnings.
/// </summary>
/// <remarks>
/// As of this writing, all supported filter operators exercised here (Eq, Contains, In),
/// sorting, and Skip/Take pagination translate to server-side SQL without client-side
/// evaluation warnings.
/// </remarks>
[Trait("Category", "Story10.1")]
public class EfCoreServerSideEvaluationTests : IAsyncLifetime
{
    private readonly List<string> _logMessages = [];

    private SqliteConnection _connection = null!;
    private TestDbContext _context = null!;
    private EfCoreRepository<TestDbContext, ProductEntity, Guid> _repository = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(_connection)
            .LogTo(message => _logMessages.Add(message), LogLevel.Warning)
            .EnableSensitiveDataLogging()
            .Options;

        _context = new TestDbContext(options);
        await _context.Database.EnsureCreatedAsync();

        var repoOptions = new EfCoreRepositoryOptions<ProductEntity, Guid>
        {
            KeySelector = entity => entity.Id
        };
        _repository = new EfCoreRepository<TestDbContext, ProductEntity, Guid>(_context, repoOptions);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task GetAll_EqFilterOnGuid_NoClientSideEvaluation()
    {
        // Arrange
        var targetId = Guid.NewGuid();
        var target = CreateProduct(name: "Target");
        target.Id = targetId;
        await SeedProductsAsync(target, CreateProduct(name: "Other"));

        var filter = CreateFilterValue(
            propertyName: "Id",
            queryParameterName: "id",
            propertyType: typeof(Guid),
            rawValue: targetId.ToString(),
            typedValue: targetId);
        var request = CreatePaginationRequest(filters: [filter]);

        // Act
        var result = await _repository.GetAllAsync(request);

        // Assert
        AssertNoClientSideEvaluationWarnings();
        result.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetAll_EqFilterOnString_NoClientSideEvaluation()
    {
        // Arrange
        await SeedProductsAsync(
            CreateProduct(name: "Widget"),
            CreateProduct(name: "Gadget"),
            CreateProduct(name: "Widget"));

        var filter = CreateFilterValue(
            propertyName: "ProductName",
            queryParameterName: "product_name",
            propertyType: typeof(string),
            rawValue: "Widget",
            typedValue: "Widget");
        var request = CreatePaginationRequest(filters: [filter]);

        // Act
        var result = await _repository.GetAllAsync(request);

        // Assert
        AssertNoClientSideEvaluationWarnings();
        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAll_EqFilterOnBool_NoClientSideEvaluation()
    {
        // Arrange
        await SeedProductsAsync(
            CreateProduct(name: "Active 1", isActive: true),
            CreateProduct(name: "Active 2", isActive: true),
            CreateProduct(name: "Inactive", isActive: false));

        var filter = CreateFilterValue(
            propertyName: "IsActive",
            queryParameterName: "is_active",
            propertyType: typeof(bool),
            rawValue: "true",
            typedValue: true);
        var request = CreatePaginationRequest(filters: [filter]);

        // Act
        var result = await _repository.GetAllAsync(request);

        // Assert
        AssertNoClientSideEvaluationWarnings();
        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAll_EqFilterOnInt_NoClientSideEvaluation()
    {
        // Arrange
        await SeedProductsAsync(
            CreateProduct(name: "P1", stockQuantity: 5),
            CreateProduct(name: "P2", stockQuantity: 10),
            CreateProduct(name: "P3", stockQuantity: 5));

        var filter = CreateFilterValue(
            propertyName: "StockQuantity",
            queryParameterName: "stock_quantity",
            propertyType: typeof(int),
            rawValue: "5",
            typedValue: 5);
        var request = CreatePaginationRequest(filters: [filter]);

        // Act
        var result = await _repository.GetAllAsync(request);

        // Assert
        AssertNoClientSideEvaluationWarnings();
        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAll_EqFilterOnDecimal_NoClientSideEvaluation()
    {
        // Arrange
        await SeedProductsAsync(
            CreateProduct(name: "P1", unitPrice: 10m),
            CreateProduct(name: "P2", unitPrice: 20m),
            CreateProduct(name: "P3", unitPrice: 10m));

        var filter = CreateFilterValue(
            propertyName: "UnitPrice",
            queryParameterName: "unit_price",
            propertyType: typeof(decimal),
            rawValue: "10",
            typedValue: 10m);
        var request = CreatePaginationRequest(filters: [filter]);

        // Act
        var result = await _repository.GetAllAsync(request);

        // Assert
        AssertNoClientSideEvaluationWarnings();
        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAll_ContainsStringFilter_NoClientSideEvaluation()
    {
        // Arrange
        await SeedProductsAsync(
            CreateProduct(name: "Blue Widget"),
            CreateProduct(name: "Red Widget"),
            CreateProduct(name: "Green Gadget"));

        var filter = CreateFilterValue(
            propertyName: "ProductName",
            queryParameterName: "product_name",
            propertyType: typeof(string),
            rawValue: "Widget",
            typedValue: "Widget",
            @operator: FilterOperator.Contains);
        var request = CreatePaginationRequest(filters: [filter]);

        // Act
        var result = await _repository.GetAllAsync(request);

        // Assert
        AssertNoClientSideEvaluationWarnings();
        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAll_InFilter_NoClientSideEvaluation()
    {
        // Arrange
        await SeedProductsAsync(
            CreateProduct(name: "Product A", status: "Active"),
            CreateProduct(name: "Product B", status: "Draft"),
            CreateProduct(name: "Product C", status: "Archived"),
            CreateProduct(name: "Product D", status: "Active"));

        var filter = CreateFilterValue(
            propertyName: "Status",
            queryParameterName: "status",
            propertyType: typeof(string),
            rawValue: "Active,Draft",
            typedValues: ["Active", "Draft"],
            @operator: FilterOperator.In);
        var request = CreatePaginationRequest(filters: [filter]);

        // Act
        var result = await _repository.GetAllAsync(request);

        // Assert
        AssertNoClientSideEvaluationWarnings();
        result.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetAll_SortWithPagination_NoClientSideEvaluation()
    {
        // Arrange
        await SeedProductsAsync(
            CreateProduct(name: "Product E", unitPrice: 50m),
            CreateProduct(name: "Product A", unitPrice: 10m),
            CreateProduct(name: "Product D", unitPrice: 40m),
            CreateProduct(name: "Product B", unitPrice: 20m),
            CreateProduct(name: "Product C", unitPrice: 30m));

        var sortField = new SortField
        {
            PropertyName = "UnitPrice",
            QueryParameterName = "unit_price",
            Direction = SortDirection.Asc
        };
        var request = CreatePaginationRequest(sortFields: [sortField], limit: 2);

        // Act
        var result = await _repository.GetAllAsync(request);

        // Assert
        AssertNoClientSideEvaluationWarnings();
        result.Items.Should().HaveCount(2);
        result.NextCursor.Should().NotBeNull();

        _logMessages.Clear();
        var page2Request = CreatePaginationRequest(sortFields: [sortField], limit: 2, cursor: result.NextCursor);
        var page2 = await _repository.GetAllAsync(page2Request);

        AssertNoClientSideEvaluationWarnings();
        page2.Items.Should().HaveCount(2);
    }

    /// <summary>
    /// Creates a <see cref="PaginationRequest"/> with the specified filters and sort fields.
    /// </summary>
    /// <param name="filters">The filters to apply.</param>
    /// <param name="sortFields">The sort fields to apply.</param>
    /// <param name="limit">The maximum number of items to return.</param>
    /// <param name="cursor">The cursor for the current page.</param>
    /// <returns>A configured pagination request.</returns>
    private static PaginationRequest CreatePaginationRequest(
        IReadOnlyList<FilterValue>? filters = null,
        IReadOnlyList<SortField>? sortFields = null,
        int limit = 100,
        string? cursor = null)
    {
        return new PaginationRequest
        {
            Filters = filters ?? [],
            SortFields = sortFields ?? [],
            Limit = limit,
            Cursor = cursor ?? string.Empty
        };
    }

    /// <summary>
    /// Creates a test <see cref="ProductEntity"/> with the specified property values.
    /// </summary>
    /// <param name="name">The product name.</param>
    /// <param name="unitPrice">The unit price.</param>
    /// <param name="stockQuantity">The stock quantity.</param>
    /// <param name="isActive">Whether the product is active.</param>
    /// <param name="status">The product status.</param>
    /// <returns>A configured product entity.</returns>
    private static ProductEntity CreateProduct(
        string name = "Test Product",
        decimal unitPrice = 10.00m,
        int stockQuantity = 5,
        bool isActive = true,
        string? status = null)
    {
        return new ProductEntity
        {
            Id = Guid.NewGuid(),
            ProductName = name,
            UnitPrice = unitPrice,
            StockQuantity = stockQuantity,
            CreatedAt = DateTime.UtcNow,
            IsActive = isActive,
            Status = status
        };
    }

    /// <summary>
    /// Creates a filter value for repository-level testing.
    /// </summary>
    /// <param name="propertyName">The C# property name.</param>
    /// <param name="queryParameterName">The query parameter name.</param>
    /// <param name="propertyType">The property CLR type.</param>
    /// <param name="rawValue">The raw string value.</param>
    /// <param name="typedValue">The typed single value.</param>
    /// <param name="typedValues">The typed values for In filters.</param>
    /// <param name="operator">The filter operator.</param>
    /// <returns>A configured filter value.</returns>
    private static FilterValue CreateFilterValue(
        string propertyName,
        string queryParameterName,
        Type propertyType,
        string rawValue,
        object? typedValue = null,
        IReadOnlyList<object?>? typedValues = null,
        FilterOperator @operator = FilterOperator.Eq)
    {
        return new FilterValue
        {
            PropertyName = propertyName,
            QueryParameterName = queryParameterName,
            PropertyType = propertyType,
            RawValue = rawValue,
            TypedValue = typedValue,
            TypedValues = typedValues,
            Operator = @operator
        };
    }

    /// <summary>
    /// Seeds products into the test database.
    /// </summary>
    /// <param name="products">The products to seed.</param>
    /// <returns>A task that completes when seeding is done.</returns>
    private async Task SeedProductsAsync(params ProductEntity[] products)
    {
        _context.Products.RemoveRange(_context.Products);
        await _context.SaveChangesAsync();
        _context.Products.AddRange(products);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        _logMessages.Clear();
    }

    /// <summary>
    /// Asserts that no client-side evaluation warnings were recorded during the query.
    /// </summary>
    private void AssertNoClientSideEvaluationWarnings()
    {
        var queryWarnings = _logMessages
            .Where(msg =>
                msg.Contains("could not be translated", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("will be evaluated locally", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("client evaluation", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("switching to client", StringComparison.OrdinalIgnoreCase))
            .ToList();

        queryWarnings.Should().BeEmpty(
            "all queries should be translated to SQL without client-side evaluation, but the following warnings were logged: {0}",
            string.Join(Environment.NewLine, queryWarnings));
    }
}
