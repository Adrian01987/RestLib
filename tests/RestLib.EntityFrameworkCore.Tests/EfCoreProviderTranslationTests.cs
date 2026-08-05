using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestLib.EntityFrameworkCore.Tests.Fakes;
using RestLib.Filtering;
using RestLib.Search;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

/// <summary>
/// Verifies that RestLib's portable filter and search expressions compile through
/// the SQL Server provider without requiring a running database server.
/// </summary>
[Trait("Category", "Story5.1.1")]
[Trait("Feature", "Search")]
[Trait("Type", "Integration")]
public class EfCoreProviderTranslationTests
{
    [Fact]
    public void SqlServerProvider_PortableRelationalFilters_TranslateServerSide()
    {
        // Arrange
        using var context = CreateSqlServerContext();
        var filters = new[]
        {
            CreateFilter(nameof(ProductEntity.StockQuantity), typeof(int), "10", 10),
            CreateFilter(nameof(ProductEntity.UnitPrice), typeof(decimal), "10.5", 10.5m),
            CreateFilter(
                nameof(ProductEntity.CreatedAt),
                typeof(DateTime),
                "2026-01-02T03:04:05Z",
                new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)),
            CreateFilter(
                nameof(ProductEntity.LastModifiedAt),
                typeof(DateTime?),
                "2026-01-02T03:04:05Z",
                new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc))
        };

        // Act
        var sqlStatements = filters
            .Select(filter => ComparisonFilterBuilder.BuildPredicate<ProductEntity>(filter))
            .Select(predicate => context.Products.Where(predicate).ToQueryString())
            .ToList();

        // Assert
        sqlStatements.Should().HaveCount(filters.Length);
        sqlStatements.Should().OnlyContain(sql => sql.Contains("WHERE", StringComparison.Ordinal));
    }

    [Fact]
    public void SqlServerProvider_LiteralStringFilter_TranslatesWithEscapeClause()
    {
        // Arrange
        using var context = CreateSqlServerContext();
        var filter = new FilterValue
        {
            PropertyName = nameof(ProductEntity.ProductName),
            QueryParameterName = "product_name",
            PropertyType = typeof(string),
            RawValue = @"%_[]^\",
            TypedValue = @"%_[]^\",
            Operator = FilterOperator.Contains
        };
        var predicate = StringFilterBuilder.BuildPredicate<ProductEntity>(filter);

        // Act
        var sql = context.Products.Where(predicate).ToQueryString();

        // Assert
        sql.Should().Contain("UPPER(");
        sql.Should().Contain("LIKE");
        sql.Should().Contain("ESCAPE");
        sql.Should().Contain(@"%\%\_\[\]\^\\%");
    }

    [Fact]
    public void SqlServerProvider_CaseInsensitiveSearch_TranslatesServerSide()
    {
        // Arrange
        using var context = CreateSqlServerContext();
        var request = new SearchRequest
        {
            Term = "widget",
            QueryParameterName = "q",
            CaseSensitive = false,
            Properties =
            [
                new SearchPropertyConfiguration
                {
                    PropertyName = nameof(ProductEntity.ProductName),
                    QueryParameterName = "product_name"
                },
                new SearchPropertyConfiguration
                {
                    PropertyName = nameof(ProductEntity.OptionalDescription),
                    QueryParameterName = "optional_description"
                }
            ]
        };
        var predicate = SearchBuilder.BuildPredicate<ProductEntity>(request);

        // Act
        var sql = context.Products.Where(predicate).ToQueryString();

        // Assert
        sql.Should().Contain("UPPER(");
        sql.Should().Contain("WHERE");
    }

    private static FilterValue CreateFilter(
        string propertyName,
        Type propertyType,
        string rawValue,
        object typedValue)
    {
        return new FilterValue
        {
            PropertyName = propertyName,
            QueryParameterName = propertyName,
            PropertyType = propertyType,
            RawValue = rawValue,
            TypedValue = typedValue,
            Operator = FilterOperator.Gt
        };
    }

    private static TestDbContext CreateSqlServerContext()
    {
        const string connectionString =
            "Server=localhost;Database=RestLibProviderTranslationTests;"
            + "Integrated Security=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new TestDbContext(options);
    }
}
