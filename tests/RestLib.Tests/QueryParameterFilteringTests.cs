using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestLib.Abstractions;
using RestLib.Configuration;
using RestLib.Filtering;
using RestLib.InMemory;
using RestLib.Pagination;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Entity for filter testing with various property types.
/// </summary>
public class FilterableEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; }
    public Guid? CategoryId { get; set; }
    public DateTime CreatedAt { get; set; }
    public ProductStatus Status { get; set; }
}

public enum ProductStatus
{
    Draft,
    Active,
    Discontinued
}

/// <summary>
/// A custom type whose TypeConverter returns null from ConvertFrom.
/// </summary>
[TypeConverter(typeof(NullReturningConverter))]
public class NullConvertedType
{
    /// <summary>Gets or sets the inner value.</summary>
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// A TypeConverter that always returns null from ConvertFrom.
/// </summary>
public class NullReturningConverter : TypeConverter
{
    /// <inheritdoc />
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
    }

    /// <inheritdoc />
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        return null;
    }
}

/// <summary>
/// Entity with a custom-typed property for testing the TypeConverter null path.
/// </summary>
public class NullConverterEntity
{
    /// <summary>Gets or sets the identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the custom value.</summary>
    public NullConvertedType CustomProp { get; set; } = new();
}

/// <summary>
/// Tests for Story 4.3: Query Parameter Filtering.
/// Verifies filter configuration, snake_case param names, validation, and pagination integration.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "Filtering")]
public class QueryParameterFilteringTests : IDisposable
{
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly InMemoryRepository<FilterableEntity, Guid> _repository;

    public QueryParameterFilteringTests()
    {
        _repository = new InMemoryRepository<FilterableEntity, Guid>(e => e.Id, Guid.NewGuid);

        (_host, _client) = new TestHostBuilder<FilterableEntity, Guid>(_repository, "/api/items")
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.AllowFiltering(
                    p => p.IsActive,
                    p => p.CategoryId,
                    p => p.Quantity,
                    p => p.Status,
                    p => p.Name,
                    p => p.Price);
            })
            .Build();
    }

    public void Dispose()
    {
        _client.Dispose();
        _host.Dispose();
    }

    #region Filter Configuration Tests

    [Fact]
    [Trait("Category", "Story4.3")]
    public async Task GetAll_WithBooleanFilter_FiltersResults()
    {
        // Arrange
        _repository.Seed([
            new FilterableEntity { Id = Guid.NewGuid(), Name = "Active 1", IsActive = true },
        new FilterableEntity { Id = Guid.NewGuid(), Name = "Active 2", IsActive = true },
        new FilterableEntity { Id = Guid.NewGuid(), Name = "Inactive 1", IsActive = false },
    ]);

        // Act
        var response = await _client.GetAsync("/api/items?is_active=true");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(2);
    }

    [Fact]
    [Trait("Category", "Story4.3")]
    public async Task GetAll_WithGuidFilter_FiltersResults()
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        _repository.Seed([
            new FilterableEntity { Id = Guid.NewGuid(), Name = "Cat1", CategoryId = categoryId },
        new FilterableEntity { Id = Guid.NewGuid(), Name = "Cat1-2", CategoryId = categoryId },
        new FilterableEntity { Id = Guid.NewGuid(), Name = "Cat2", CategoryId = Guid.NewGuid() },
    ]);

        // Act
        var response = await _client.GetAsync($"/api/items?category_id={categoryId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(2);
    }

    [Fact]
    [Trait("Category", "Story4.3")]
    public async Task GetAll_WithIntFilter_FiltersResults()
    {
        // Arrange
        _repository.Seed([
            new FilterableEntity { Id = Guid.NewGuid(), Name = "Qty10", Quantity = 10 },
        new FilterableEntity { Id = Guid.NewGuid(), Name = "Qty20", Quantity = 20 },
        new FilterableEntity { Id = Guid.NewGuid(), Name = "Qty10-2", Quantity = 10 },
    ]);

        // Act
        var response = await _client.GetAsync("/api/items?quantity=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(2);
    }

    [Fact]
    [Trait("Category", "Story4.3")]
    public async Task GetAll_WithEnumFilter_FiltersResults()
    {
        // Arrange
        _repository.Seed([
            new FilterableEntity { Id = Guid.NewGuid(), Name = "Active", Status = ProductStatus.Active },
        new FilterableEntity { Id = Guid.NewGuid(), Name = "Draft", Status = ProductStatus.Draft },
        new FilterableEntity { Id = Guid.NewGuid(), Name = "Active2", Status = ProductStatus.Active },
    ]);

        // Act
        var response = await _client.GetAsync("/api/items?status=Active");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(2);
    }

    [Fact]
    [Trait("Category", "Story4.3")]
    public async Task GetAll_WithStringFilter_FiltersResults()
    {
        // Arrange
        _repository.Seed([
            new FilterableEntity { Id = Guid.NewGuid(), Name = "Widget" },
        new FilterableEntity { Id = Guid.NewGuid(), Name = "Gadget" },
        new FilterableEntity { Id = Guid.NewGuid(), Name = "Widget" },
    ]);

        // Act
        var response = await _client.GetAsync("/api/items?name=Widget");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(2);
    }

    [Fact]
    [Trait("Category", "Story4.3")]
    public async Task GetAll_WithDecimalFilter_FiltersResults()
    {
        // Arrange
        _repository.Seed([
            new FilterableEntity { Id = Guid.NewGuid(), Name = "Cheap", Price = 9.99m },
        new FilterableEntity { Id = Guid.NewGuid(), Name = "Expensive", Price = 99.99m },
        new FilterableEntity { Id = Guid.NewGuid(), Name = "Cheap2", Price = 9.99m },
    ]);

        // Act
        var response = await _client.GetAsync("/api/items?price=9.99");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(2);
    }

    #endregion

    #region Snake Case Parameter Names Tests

    [Fact]
    [Trait("Category", "Story4.3")]
    public async Task GetAll_FilterParams_UseSnakeCase()
    {
        // Arrange
        _repository.Seed([new FilterableEntity { Id = Guid.NewGuid(), Name = "Test", IsActive = true }]);

        // Act - using snake_case parameter
        var response = await _client.GetAsync("/api/items?is_active=true");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Category", "Story4.3")]
    public async Task GetAll_CamelCaseParams_NotRecognized()
    {
        // Arrange
        _repository.Seed([
            new FilterableEntity { Id = Guid.NewGuid(), Name = "Active", IsActive = true },
        new FilterableEntity { Id = Guid.NewGuid(), Name = "Inactive", IsActive = false },
    ]);

        // Act - using camelCase (should not match configured snake_case filter)
        var response = await _client.GetAsync("/api/items?isActive=true");

        // Assert - returns all items since filter not recognized
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(2); // All items, no filtering
    }

    [Fact]
    [Trait("Category", "Story4.3")]
    public void FilterConfiguration_ConvertsPropertyNamesToSnakeCase()
    {
        // Arrange
        var config = new FilterConfiguration<FilterableEntity>();

        // Act
        config.AddProperty(p => p.IsActive);
        config.AddProperty(p => p.CategoryId);
        config.AddProperty(p => p.CreatedAt);

        // Assert
        config.Properties.Should().HaveCount(3);
        config.Properties[0].QueryParameterName.Should().Be("is_active");
        config.Properties[1].QueryParameterName.Should().Be("category_id");
        config.Properties[2].QueryParameterName.Should().Be("created_at");
    }

    #endregion

    #region Invalid Filter Value Tests

    [Fact]
    [Trait("Category", "Story4.3")]
    public async Task GetAll_InvalidBooleanValue_Returns400()
    {
        // Arrange
        _repository.Seed([new FilterableEntity { Id = Guid.NewGuid(), Name = "Test" }]);

        // Act
        var response = await _client.GetAsync("/api/items?is_active=notabool");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("is_active");
        content.Should().Contain("invalid-filter");
    }

    [Fact]
    [Trait("Category", "Story4.3")]
    public async Task GetAll_InvalidGuidValue_Returns400()
    {
        // Arrange
        _repository.Seed([new FilterableEntity { Id = Guid.NewGuid(), Name = "Test" }]);

        // Act
        var response = await _client.GetAsync("/api/items?category_id=not-a-guid");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("category_id");
    }

    [Fact]
    [Trait("Category", "Story4.3")]
    public async Task GetAll_InvalidIntValue_Returns400()
    {
        // Arrange
        _repository.Seed([new FilterableEntity { Id = Guid.NewGuid(), Name = "Test" }]);

        // Act
        var response = await _client.GetAsync("/api/items?quantity=abc");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("quantity");
    }

    [Fact]
    [Trait("Category", "Story4.3")]
    public async Task GetAll_InvalidEnumValue_Returns400()
    {
        // Arrange
        _repository.Seed([new FilterableEntity { Id = Guid.NewGuid(), Name = "Test" }]);

        // Act
        var response = await _client.GetAsync("/api/items?status=InvalidStatus");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("status");
        content.Should().Contain("Draft"); // Should show valid values
    }

    [Fact]
    [Trait("Category", "Story4.3")]
    public async Task GetAll_InvalidFilterValue_ReturnsProblemDetails()
    {
        // Arrange
        _repository.Seed([new FilterableEntity { Id = Guid.NewGuid(), Name = "Test" }]);

        // Act
        var response = await _client.GetAsync("/api/items?is_active=invalid");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("type").GetString().Should().Be("/problems/invalid-filter");
        json.GetProperty("status").GetInt32().Should().Be(400);
        json.TryGetProperty("errors", out var errors).Should().BeTrue();
        errors.TryGetProperty("is_active", out _).Should().BeTrue();
    }

    #endregion

    #region Filters Combine With Pagination Tests

    [Fact]
    [Trait("Category", "Story4.3")]
    public async Task GetAll_FiltersAndLimit_CombineCorrectly()
    {
        // Arrange
        for (int i = 0; i < 20; i++)
        {
            _repository.Seed([new FilterableEntity
            {
                Id = Guid.NewGuid(),
                Name = $"Active {i}",
                IsActive = true
            }]);
        }
        _repository.Seed([new FilterableEntity { Id = Guid.NewGuid(), Name = "Inactive", IsActive = false }]);

        // Act
        var response = await _client.GetAsync("/api/items?is_active=true&limit=5");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(5);

        // Next link should be present since there are more active items
        json.TryGetProperty("next", out var next).Should().BeTrue();
        next.GetString().Should().Contain("is_active=true");
        next.GetString().Should().Contain("limit=5");
    }

    [Fact]
    [Trait("Category", "Story4.3")]
    public async Task GetAll_FiltersPreservedInPaginationLinks()
    {
        // Arrange
        for (int i = 0; i < 15; i++)
        {
            _repository.Seed([new FilterableEntity
            {
                Id = Guid.NewGuid(),
                Name = $"Active {i}",
                IsActive = true,
                Quantity = 10
            }]);
        }

        // Act
        var response = await _client.GetAsync("/api/items?is_active=true&quantity=10&limit=5");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        var self = json.GetProperty("self").GetString()!;
        var first = json.GetProperty("first").GetString()!;
        var next = json.GetProperty("next").GetString()!;

        // All links should preserve filters
        foreach (var link in new[] { self, first, next })
        {
            link.Should().Contain("is_active=true");
            link.Should().Contain("quantity=10");
            link.Should().Contain("limit=5");
        }
    }

    [Fact]
    [Trait("Category", "Story4.3")]
    public async Task GetAll_MultipleFilters_CombineWithAnd()
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        _repository.Seed([
            new FilterableEntity { Id = Guid.NewGuid(), Name = "Match1", IsActive = true, CategoryId = categoryId },
        new FilterableEntity { Id = Guid.NewGuid(), Name = "Match2", IsActive = true, CategoryId = categoryId },
        new FilterableEntity { Id = Guid.NewGuid(), Name = "ActiveOther", IsActive = true, CategoryId = Guid.NewGuid() },
        new FilterableEntity { Id = Guid.NewGuid(), Name = "InactiveMatch", IsActive = false, CategoryId = categoryId },
    ]);

        // Act - both filters must match
        var response = await _client.GetAsync($"/api/items?is_active=true&category_id={categoryId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(2); // Only items that match both filters
    }

    #endregion

    #region Filter Edge Cases

    [Fact]
    [Trait("Category", "Story4.3")]
    public async Task GetAll_NoMatchingFilters_ReturnsEmptyCollection()
    {
        // Arrange
        _repository.Seed([
            new FilterableEntity { Id = Guid.NewGuid(), Name = "Active", IsActive = true },
        new FilterableEntity { Id = Guid.NewGuid(), Name = "Active2", IsActive = true },
    ]);

        // Act
        var response = await _client.GetAsync("/api/items?is_active=false");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Story4.3")]
    public async Task GetAll_BooleanFilter_Accepts1And0()
    {
        // Arrange
        _repository.Seed([
            new FilterableEntity { Id = Guid.NewGuid(), Name = "Active", IsActive = true },
        new FilterableEntity { Id = Guid.NewGuid(), Name = "Inactive", IsActive = false },
    ]);

        // Act - use "1" for true
        var response1 = await _client.GetAsync("/api/items?is_active=1");
        var json1 = await response1.Content.ReadFromJsonAsync<JsonElement>();

        // Act - use "0" for false
        var response0 = await _client.GetAsync("/api/items?is_active=0");
        var json0 = await response0.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        json1.GetProperty("items").GetArrayLength().Should().Be(1);

        response0.StatusCode.Should().Be(HttpStatusCode.OK);
        json0.GetProperty("items").GetArrayLength().Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Story4.3")]
    public async Task GetAll_EnumFilter_CaseInsensitive()
    {
        // Arrange
        _repository.Seed([
            new FilterableEntity { Id = Guid.NewGuid(), Name = "Active", Status = ProductStatus.Active },
        new FilterableEntity { Id = Guid.NewGuid(), Name = "Draft", Status = ProductStatus.Draft },
    ]);

        // Act - lowercase enum value
        var response = await _client.GetAsync("/api/items?status=active");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("items").GetArrayLength().Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Story4.3")]
    public async Task GetAll_UnconfiguredFilter_Ignored()
    {
        // Arrange
        _repository.Seed([
            new FilterableEntity { Id = Guid.NewGuid(), Name = "Test1" },
        new FilterableEntity { Id = Guid.NewGuid(), Name = "Test2" },
    ]);

        // Act - filter on a property not configured for filtering
        var response = await _client.GetAsync("/api/items?created_at=2024-01-01");

        // Assert - should return all items (filter ignored, not an error)
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("items").GetArrayLength().Should().Be(2);
    }

    [Fact]
    [Trait("Category", "Story4.3")]
    public async Task GetAll_EmptyFilterValue_Ignored()
    {
        // Arrange
        _repository.Seed([
            new FilterableEntity { Id = Guid.NewGuid(), Name = "Test1", IsActive = true },
        new FilterableEntity { Id = Guid.NewGuid(), Name = "Test2", IsActive = false },
    ]);

        // Act - empty filter value
        var response = await _client.GetAsync("/api/items?is_active=");

        // Assert - should return all items (empty value ignored)
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("items").GetArrayLength().Should().Be(2);
    }

    #endregion
}

/// <summary>
/// Tests for filter configuration without filters (baseline behavior).
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "Filtering")]
public class NoFilterConfigurationTests : IDisposable
{
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly InMemoryRepository<FilterableEntity, Guid> _repository;

    public NoFilterConfigurationTests()
    {
        _repository = new InMemoryRepository<FilterableEntity, Guid>(e => e.Id, Guid.NewGuid);

        (_host, _client) = new TestHostBuilder<FilterableEntity, Guid>(_repository, "/api/items")
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                // No AllowFiltering call
            })
            .Build();
    }

    public void Dispose()
    {
        _client.Dispose();
        _host.Dispose();
    }

    [Fact]
    [Trait("Category", "Story4.3")]
    public async Task GetAll_WithoutFilterConfig_IgnoresFilterParams()
    {
        // Arrange
        _repository.Seed([
            new FilterableEntity { Id = Guid.NewGuid(), Name = "Active", IsActive = true },
        new FilterableEntity { Id = Guid.NewGuid(), Name = "Inactive", IsActive = false },
    ]);

        // Act - try to filter, but filters not configured
        var response = await _client.GetAsync("/api/items?is_active=true");

        // Assert - returns all items, no filtering applied
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("items").GetArrayLength().Should().Be(2);
    }

    [Fact]
    [Trait("Category", "Story4.3")]
    public async Task GetAll_WithoutFilterConfig_InvalidValueDoesNotError()
    {
        // Arrange
        _repository.Seed([new FilterableEntity { Id = Guid.NewGuid(), Name = "Test" }]);

        // Act - invalid value, but filters not configured so it's just ignored
        var response = await _client.GetAsync("/api/items?is_active=invalid");

        // Assert - still succeeds, param ignored
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

/// <summary>
/// Tests for FilterParser unit behavior.
/// </summary>
[Trait("Type", "Unit")]
[Trait("Feature", "Filtering")]
public class FilterParserTests
{
    [Fact]
    [Trait("Category", "Story4.3")]
    public void Parse_ValidBooleanTrue_ReturnsTypedValue()
    {
        // Arrange
        var config = new FilterConfiguration<FilterableEntity>();
        config.AddProperty(p => p.IsActive);

        var query = new Microsoft.AspNetCore.Http.QueryCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
            { "is_active", "true" }
            });

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Values.Should().HaveCount(1);
        result.Values[0].TypedValue.Should().Be(true);
        result.Values[0].PropertyName.Should().Be("IsActive");
    }

    [Fact]
    [Trait("Category", "Story4.3")]
    public void Parse_ValidGuid_ReturnsTypedValue()
    {
        // Arrange
        var config = new FilterConfiguration<FilterableEntity>();
        config.AddProperty(p => p.CategoryId);

        var guid = Guid.NewGuid();
        var query = new Microsoft.AspNetCore.Http.QueryCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
            { "category_id", guid.ToString() }
            });

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Values.Should().HaveCount(1);
        result.Values[0].TypedValue.Should().Be(guid);
    }

    [Fact]
    [Trait("Category", "Story4.3")]
    public void Parse_InvalidBoolean_ReturnsError()
    {
        // Arrange
        var config = new FilterConfiguration<FilterableEntity>();
        config.AddProperty(p => p.IsActive);

        var query = new Microsoft.AspNetCore.Http.QueryCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
            { "is_active", "notabool" }
            });

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].ParameterName.Should().Be("is_active");
        result.Errors[0].ProvidedValue.Should().Be("notabool");
    }

    [Fact]
    [Trait("Category", "Story4.3")]
    public void Parse_MissingFilterParam_ReturnsEmptyValues()
    {
        // Arrange
        var config = new FilterConfiguration<FilterableEntity>();
        config.AddProperty(p => p.IsActive);

        var query = new Microsoft.AspNetCore.Http.QueryCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>());

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Values.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Story4.3")]
    public void Parse_EnumValue_ParsesCorrectly()
    {
        // Arrange
        var config = new FilterConfiguration<FilterableEntity>();
        config.AddProperty(p => p.Status);

        var query = new Microsoft.AspNetCore.Http.QueryCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
            { "status", "Active" }
            });

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Values.Should().HaveCount(1);
        result.Values[0].TypedValue.Should().Be(ProductStatus.Active);
    }

    [Fact]
    [Trait("Category", "Story4.3")]
    public void Parse_InvalidEnumValue_ReturnsErrorWithValidValues()
    {
        // Arrange
        var config = new FilterConfiguration<FilterableEntity>();
        config.AddProperty(p => p.Status);

        var query = new Microsoft.AspNetCore.Http.QueryCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
            { "status", "Invalid" }
            });

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors[0].Message.Should().Contain("Draft");
        result.Errors[0].Message.Should().Contain("Active");
        result.Errors[0].Message.Should().Contain("Discontinued");
    }

    [Fact]
    [Trait("Category", "Story4.3")]
    public void Parse_TypeConverterReturnsNull_ReturnsError()
    {
        // Arrange — a custom type whose TypeConverter.ConvertFrom returns null
        var config = new FilterConfiguration<NullConverterEntity>();
        config.AddProperty(p => p.CustomProp);

        var query = new Microsoft.AspNetCore.Http.QueryCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
            { "custom_prop", "anything" }
            });

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert — should be treated as a conversion failure, not a success with null value
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].ParameterName.Should().Be("custom_prop");
        result.Errors[0].ProvidedValue.Should().Be("anything");
        result.Errors[0].Message.Should().Contain("Cannot convert");
    }

    [Fact]
    [Trait("Category", "Story4.3")]
    public void Parse_MultipleValuesForSameProperty_ReturnsError()
    {
        // Arrange
        var config = new FilterConfiguration<FilterableEntity>();
        config.AddProperty(p => p.Status);

        var query = new Microsoft.AspNetCore.Http.QueryCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
            { "status", new Microsoft.Extensions.Primitives.StringValues(["Active", "Draft"]) }
            });

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].ParameterName.Should().Be("status");
        result.Errors[0].Message.Should().Contain("Multiple values");
        result.Errors[0].Message.Should().Contain("not supported");
        result.Values.Should().BeEmpty();
    }
}

/// <summary>
/// Tests verifying OpenAPI documentation for filters.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "Filtering")]
public class FilterOpenApiTests : IDisposable
{
    private readonly IHost _host;
    private readonly HttpClient _client;

    public FilterOpenApiTests()
    {
        (_host, _client) = new TestHostBuilder<FilterableEntity, Guid>(new InMemoryRepository<FilterableEntity, Guid>(e => e.Id, Guid.NewGuid), "/api/items")
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.AllowFiltering(
                    p => p.IsActive,
                    p => p.Quantity,
                    p => p.Status);
            })
            .WithServices(services =>
            {
                services.AddEndpointsApiExplorer();
            })
            .Build();
    }

    public void Dispose()
    {
        _client.Dispose();
        _host.Dispose();
    }

    [Fact]
    [Trait("Category", "Story4.3")]
    public void FilterConfiguration_StoresPropertyMetadata()
    {
        // Arrange
        var config = new FilterConfiguration<FilterableEntity>();

        // Act
        config.AddProperty(p => p.IsActive);
        config.AddProperty(p => p.Quantity);
        config.AddProperty(p => p.Status);

        // Assert
        config.Properties.Should().HaveCount(3);

        config.Properties[0].PropertyName.Should().Be("IsActive");
        config.Properties[0].PropertyType.Should().Be(typeof(bool));

        config.Properties[1].PropertyName.Should().Be("Quantity");
        config.Properties[1].PropertyType.Should().Be(typeof(int));

        config.Properties[2].PropertyName.Should().Be("Status");
        config.Properties[2].PropertyType.Should().Be(typeof(ProductStatus));
    }

    [Fact]
    [Trait("Category", "Story4.3")]
    public void FilterConfiguration_DuplicateProperty_ThrowsInvalidOperationException()
    {
        // Arrange
        var config = new FilterConfiguration<FilterableEntity>();
        config.AddProperty(p => p.IsActive);

        // Act
        var act = () => config.AddProperty(p => p.IsActive);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*'IsActive'*already configured*filtering*");
    }
}

/// <summary>
/// Unit tests for FilterParser bracket-syntax operator parsing.
/// </summary>
[Trait("Type", "Unit")]
[Trait("Feature", "Filtering")]
public class FilterParserOperatorTests
{
    [Theory]
    [Trait("Category", "Story4.3.Operators")]
    [InlineData("price", "price", null)]
    [InlineData("price[eq]", "price", "eq")]
    [InlineData("price[gte]", "price", "gte")]
    [InlineData("price[lte]", "price", "lte")]
    [InlineData("price[gt]", "price", "gt")]
    [InlineData("price[lt]", "price", "lt")]
    [InlineData("name[contains]", "name", "contains")]
    [InlineData("name[starts_with]", "name", "starts_with")]
    [InlineData("status[in]", "status", "in")]
    [InlineData("status[neq]", "status", "neq")]
    public void ParseQueryParameterKey_ParsesBracketSyntax(string key, string expectedName, string? expectedOp)
    {
        // Act
        var (paramName, operatorStr) = FilterParser.ParseQueryParameterKey(key);

        // Assert
        paramName.Should().Be(expectedName);
        operatorStr.Should().Be(expectedOp);
    }

    [Theory]
    [Trait("Category", "Story4.3.Operators")]
    [InlineData("eq")]
    [InlineData("neq")]
    [InlineData("gt")]
    [InlineData("lt")]
    [InlineData("gte")]
    [InlineData("lte")]
    [InlineData("contains")]
    [InlineData("starts_with")]
    [InlineData("in")]
    public void GetOperatorName_ReturnsCorrectString(string expected)
    {
        // Arrange
        FilterOperator op;
        if (expected == "starts_with")
        {
            op = FilterOperator.StartsWith;
        }
        else
        {
            op = Enum.Parse<FilterOperator>(expected, ignoreCase: true);
        }

        // Act
        var name = FilterParser.GetOperatorName(op);

        // Assert
        name.Should().Be(expected);
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public void Parse_BracketEq_MatchesBareEquality()
    {
        // Arrange
        var config = new FilterConfiguration<FilterableEntity>();
        config.AddProperty(p => p.Quantity);

        var queryBare = new Microsoft.AspNetCore.Http.QueryCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
            { "quantity", "42" }
            });
        var queryBracket = new Microsoft.AspNetCore.Http.QueryCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
            { "quantity[eq]", "42" }
            });

        // Act
        var resultBare = FilterParser.Parse(query: queryBare, configuration: config);
        var resultBracket = FilterParser.Parse(query: queryBracket, configuration: config);

        // Assert
        resultBare.IsValid.Should().BeTrue();
        resultBracket.IsValid.Should().BeTrue();
        resultBare.Values[0].TypedValue.Should().Be(resultBracket.Values[0].TypedValue);
        resultBare.Values[0].Operator.Should().Be(FilterOperator.Eq);
        resultBracket.Values[0].Operator.Should().Be(FilterOperator.Eq);
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public void Parse_GteOperator_ParsesCorrectly()
    {
        // Arrange
        var config = new FilterConfiguration<FilterableEntity>();
        config.AddProperty(p => p.Quantity, FilterOperator.Gte, FilterOperator.Lte);

        var query = new Microsoft.AspNetCore.Http.QueryCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
            { "quantity[gte]", "10" }
            });

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Values.Should().HaveCount(1);
        result.Values[0].Operator.Should().Be(FilterOperator.Gte);
        result.Values[0].TypedValue.Should().Be(10);
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public void Parse_MultipleOperatorsOnSameProperty_Allowed()
    {
        // Arrange
        var config = new FilterConfiguration<FilterableEntity>();
        config.AddProperty(p => p.Quantity, FilterOperator.Gte, FilterOperator.Lte);

        var query = new Microsoft.AspNetCore.Http.QueryCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
            { "quantity[gte]", "10" },
            { "quantity[lte]", "100" }
            });

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Values.Should().HaveCount(2);
        result.Values.Should().Contain(v => v.Operator == FilterOperator.Gte);
        result.Values.Should().Contain(v => v.Operator == FilterOperator.Lte);
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public void Parse_UnknownOperator_ReturnsError()
    {
        // Arrange
        var config = new FilterConfiguration<FilterableEntity>();
        config.AddProperty(p => p.Quantity);

        var query = new Microsoft.AspNetCore.Http.QueryCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
            { "quantity[regex]", ".*" }
            });

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Message.Should().Contain("Unknown filter operator");
        result.Errors[0].Message.Should().Contain("regex");
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public void Parse_OperatorNotAllowed_ReturnsError()
    {
        // Arrange — Quantity only gets Eq by default (no operators specified)
        var config = new FilterConfiguration<FilterableEntity>();
        config.AddProperty(p => p.Quantity);

        var query = new Microsoft.AspNetCore.Http.QueryCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
            { "quantity[gte]", "10" }
            });

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Message.Should().Contain("not allowed");
        result.Errors[0].Message.Should().Contain("gte");
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public void Parse_ComparisonOnNonComparable_ReturnsTypeError()
    {
        // Arrange — CategoryId is a Guid? which does implement IComparable,
        // but we need a non-comparable type. Use the NullConvertedType which isn't IComparable.
        var config = new FilterConfiguration<NullConverterEntity>();
        config.AddProperty(p => p.CustomProp, FilterOperator.Gt);

        var query = new Microsoft.AspNetCore.Http.QueryCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
            { "custom_prop[gt]", "abc" }
            });

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors[0].Message.Should().Contain("comparable type");
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public void Parse_StringOperatorOnNonString_ReturnsTypeError()
    {
        // Arrange — Quantity is int, not string
        var config = new FilterConfiguration<FilterableEntity>();
        config.AddProperty(p => p.Quantity, FilterOperator.Contains);

        var query = new Microsoft.AspNetCore.Http.QueryCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
            { "quantity[contains]", "5" }
            });

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors[0].Message.Should().Contain("string properties");
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public void Parse_ContainsOperator_ParsesCorrectly()
    {
        // Arrange
        var config = new FilterConfiguration<FilterableEntity>();
        config.AddProperty(p => p.Name, FilterOperators.String);

        var query = new Microsoft.AspNetCore.Http.QueryCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
            { "name[contains]", "wid" }
            });

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Values.Should().HaveCount(1);
        result.Values[0].Operator.Should().Be(FilterOperator.Contains);
        result.Values[0].TypedValue.Should().Be("wid");
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public void Parse_StartsWithOperator_ParsesCorrectly()
    {
        // Arrange
        var config = new FilterConfiguration<FilterableEntity>();
        config.AddProperty(p => p.Name, FilterOperators.String);

        var query = new Microsoft.AspNetCore.Http.QueryCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
            { "name[starts_with]", "Wid" }
            });

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Values.Should().HaveCount(1);
        result.Values[0].Operator.Should().Be(FilterOperator.StartsWith);
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public void Parse_NeqOperator_ParsesCorrectly()
    {
        // Arrange
        var config = new FilterConfiguration<FilterableEntity>();
        config.AddProperty(p => p.Quantity, FilterOperators.Comparison);

        var query = new Microsoft.AspNetCore.Http.QueryCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
            { "quantity[neq]", "42" }
            });

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Values[0].Operator.Should().Be(FilterOperator.Neq);
        result.Values[0].TypedValue.Should().Be(42);
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public void Parse_InOperator_ParsesCommaList()
    {
        // Arrange
        var config = new FilterConfiguration<FilterableEntity>();
        config.AddProperty(p => p.Quantity, FilterOperator.In);

        var query = new Microsoft.AspNetCore.Http.QueryCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
            { "quantity[in]", "10,20,30" }
            });

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Values.Should().HaveCount(1);
        result.Values[0].Operator.Should().Be(FilterOperator.In);
        result.Values[0].TypedValues.Should().HaveCount(3);
        result.Values[0].TypedValues.Should().ContainInOrder(10, 20, 30);
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public void Parse_InOperator_EmptyList_ReturnsError()
    {
        // Arrange
        var config = new FilterConfiguration<FilterableEntity>();
        config.AddProperty(p => p.Quantity, FilterOperator.In);

        var query = new Microsoft.AspNetCore.Http.QueryCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
            { "quantity[in]", ",,," }
            });

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors[0].Message.Should().Contain("at least one value");
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public void Parse_InOperator_ExceedsMaxListSize_ReturnsError()
    {
        // Arrange
        var config = new FilterConfiguration<FilterableEntity>();
        config.AddProperty(p => p.Quantity, FilterOperator.In);

        var tooManyValues = string.Join(",", Enumerable.Range(1, FilterParser.DefaultMaxInListSize + 1));
        var query = new Microsoft.AspNetCore.Http.QueryCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
            { "quantity[in]", tooManyValues }
            });

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors[0].Message.Should().Contain("maximum");
        result.Errors[0].Message.Should().Contain(FilterParser.DefaultMaxInListSize.ToString());
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public void Parse_InOperator_InvalidValueInList_ReturnsError()
    {
        // Arrange
        var config = new FilterConfiguration<FilterableEntity>();
        config.AddProperty(p => p.Quantity, FilterOperator.In);

        var query = new Microsoft.AspNetCore.Http.QueryCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
            { "quantity[in]", "10,abc,30" }
            });

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors[0].ProvidedValue.Should().Be("abc");
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public void Parse_DuplicateOperatorOnSameProperty_ReturnsError()
    {
        // Arrange — bare "quantity=10" + "quantity[eq]=20" both resolve to Eq on same property
        var config = new FilterConfiguration<FilterableEntity>();
        config.AddProperty(p => p.Quantity);

        var query = new Microsoft.AspNetCore.Http.QueryCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
            { "quantity", "10" },
            { "quantity[eq]", "20" }
            });

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert — one should succeed, the second should trigger a duplicate error
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message.Contains("Duplicate"));
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public void Parse_EqAlwaysImplicitlyAllowed()
    {
        // Arrange — configure with only comparison operators, Eq should still be allowed
        var config = new FilterConfiguration<FilterableEntity>();
        config.AddProperty(p => p.Quantity, FilterOperator.Gt, FilterOperator.Lt);

        var query = new Microsoft.AspNetCore.Http.QueryCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
            { "quantity", "42" }
            });

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert — bare quantity=42 uses Eq which is always available
        result.IsValid.Should().BeTrue();
        result.Values[0].Operator.Should().Be(FilterOperator.Eq);
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public void TryConvertValue_Integer_ConvertsSuccessfully()
    {
        // Act
        var (success, value, error) = FilterParser.TryConvertValue("42", typeof(int));

        // Assert
        success.Should().BeTrue();
        value.Should().Be(42);
        error.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public void TryConvertValue_Decimal_ConvertsSuccessfully()
    {
        // Act
        var (success, value, error) = FilterParser.TryConvertValue("99.99", typeof(decimal));

        // Assert
        success.Should().BeTrue();
        value.Should().Be(99.99m);
        error.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public void TryConvertValue_InvalidInteger_ReturnsError()
    {
        // Act
        var (success, value, error) = FilterParser.TryConvertValue("abc", typeof(int));

        // Assert
        success.Should().BeFalse();
        value.Should().BeNull();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public void GetFriendlyTypeName_ReturnsHumanReadableNames()
    {
        // Act & Assert
        FilterParser.GetFriendlyTypeName(typeof(int)).Should().Be("integer");
        FilterParser.GetFriendlyTypeName(typeof(long)).Should().Be("long integer");
        FilterParser.GetFriendlyTypeName(typeof(decimal)).Should().Be("decimal number");
        FilterParser.GetFriendlyTypeName(typeof(double)).Should().Be("number");
        FilterParser.GetFriendlyTypeName(typeof(float)).Should().Be("number");
        FilterParser.GetFriendlyTypeName(typeof(bool)).Should().Be("boolean (true/false)");
        FilterParser.GetFriendlyTypeName(typeof(Guid)).Should().Be("GUID");
        FilterParser.GetFriendlyTypeName(typeof(DateTime)).Should().Be("date/time");
        FilterParser.GetFriendlyTypeName(typeof(DateTimeOffset)).Should().Be("date/time with timezone");
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public void GetFriendlyTypeName_NullableType_ReturnsUnderlyingName()
    {
        // Act & Assert
        FilterParser.GetFriendlyTypeName(typeof(int?)).Should().Be("integer");
        FilterParser.GetFriendlyTypeName(typeof(Guid?)).Should().Be("GUID");
    }
}

/// <summary>
/// Tests for FilterConfiguration with operator support.
/// </summary>
[Trait("Type", "Unit")]
[Trait("Feature", "Filtering")]
public class FilterConfigurationOperatorTests
{
    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public void AddProperty_WithoutOperators_DefaultsToEqOnly()
    {
        // Arrange
        var config = new FilterConfiguration<FilterableEntity>();

        // Act
        config.AddProperty(p => p.Quantity);

        // Assert
        config.Properties[0].AllowedOperators.Should().HaveCount(1);
        config.Properties[0].AllowedOperators.Should().Contain(FilterOperator.Eq);
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public void AddProperty_WithOperators_IncludesEqPlusSpecified()
    {
        // Arrange
        var config = new FilterConfiguration<FilterableEntity>();

        // Act
        config.AddProperty(p => p.Quantity, FilterOperator.Gt, FilterOperator.Lt);

        // Assert — should have Eq (implicit) + Gt + Lt = 3
        config.Properties[0].AllowedOperators.Should().HaveCount(3);
        config.Properties[0].AllowedOperators.Should().Contain(FilterOperator.Eq);
        config.Properties[0].AllowedOperators.Should().Contain(FilterOperator.Gt);
        config.Properties[0].AllowedOperators.Should().Contain(FilterOperator.Lt);
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public void AddProperty_WithComparisonPreset_HasAllComparisonOps()
    {
        // Arrange
        var config = new FilterConfiguration<FilterableEntity>();

        // Act
        config.AddProperty(p => p.Price, FilterOperators.Comparison);

        // Assert
        config.Properties[0].AllowedOperators.Should().Contain(FilterOperator.Eq);
        config.Properties[0].AllowedOperators.Should().Contain(FilterOperator.Neq);
        config.Properties[0].AllowedOperators.Should().Contain(FilterOperator.Gt);
        config.Properties[0].AllowedOperators.Should().Contain(FilterOperator.Lt);
        config.Properties[0].AllowedOperators.Should().Contain(FilterOperator.Gte);
        config.Properties[0].AllowedOperators.Should().Contain(FilterOperator.Lte);
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public void AddProperty_WithStringPreset_HasAllStringOps()
    {
        // Arrange
        var config = new FilterConfiguration<FilterableEntity>();

        // Act
        config.AddProperty(p => p.Name, FilterOperators.String);

        // Assert
        config.Properties[0].AllowedOperators.Should().Contain(FilterOperator.Eq);
        config.Properties[0].AllowedOperators.Should().Contain(FilterOperator.Neq);
        config.Properties[0].AllowedOperators.Should().Contain(FilterOperator.Contains);
        config.Properties[0].AllowedOperators.Should().Contain(FilterOperator.StartsWith);
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public void FilterOperatorsPreset_All_ContainsAllValues()
    {
        // Act & Assert
        FilterOperators.All.Should().HaveCount(Enum.GetValues<FilterOperator>().Length);
        foreach (var op in Enum.GetValues<FilterOperator>())
        {
            FilterOperators.All.Should().Contain(op);
        }
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public void FilterOperatorsPreset_Equality_ContainsEqAndNeq()
    {
        // Act & Assert
        FilterOperators.Equality.Should().HaveCount(2);
        FilterOperators.Equality.Should().Contain(FilterOperator.Eq);
        FilterOperators.Equality.Should().Contain(FilterOperator.Neq);
    }
}

/// <summary>
/// Integration tests for filter operators through the HTTP pipeline.
/// Uses InMemoryRepository which handles all operators.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "Filtering")]
public class FilterOperatorIntegrationTests : IDisposable
{
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly InMemoryRepository<FilterableEntity, Guid> _repository;

    public FilterOperatorIntegrationTests()
    {
        _repository = new InMemoryRepository<FilterableEntity, Guid>(e => e.Id, Guid.NewGuid);

        (_host, _client) = new TestHostBuilder<FilterableEntity, Guid>(_repository, "/api/items")
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.AllowFiltering(p => p.Quantity, FilterOperators.Comparison);
                config.AllowFiltering(p => p.Price, FilterOperators.Comparison);
                config.AllowFiltering(p => p.Name, FilterOperators.String);
                config.AllowFiltering(p => p.IsActive, FilterOperator.Neq);
                config.AllowFiltering(p => p.Status, FilterOperator.In, FilterOperator.Neq);
            })
            .Build();

        // Seed test data
        SeedTestData().GetAwaiter().GetResult();
    }

    private async Task SeedTestData()
    {
        var entities = new[]
        {
        new FilterableEntity { Id = Guid.NewGuid(), Name = "Widget Alpha", Quantity = 10, Price = 9.99m, IsActive = true, Status = ProductStatus.Active },
        new FilterableEntity { Id = Guid.NewGuid(), Name = "Widget Beta", Quantity = 20, Price = 19.99m, IsActive = true, Status = ProductStatus.Active },
        new FilterableEntity { Id = Guid.NewGuid(), Name = "Gadget Gamma", Quantity = 30, Price = 29.99m, IsActive = false, Status = ProductStatus.Draft },
        new FilterableEntity { Id = Guid.NewGuid(), Name = "Gadget Delta", Quantity = 40, Price = 49.99m, IsActive = true, Status = ProductStatus.Active },
        new FilterableEntity { Id = Guid.NewGuid(), Name = "Sprocket Epsilon", Quantity = 50, Price = 99.99m, IsActive = false, Status = ProductStatus.Discontinued },
    };

        foreach (var entity in entities)
        {
            await _repository.CreateAsync(entity);
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        _host.Dispose();
    }

    #region Greater Than / Less Than

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public async Task GetAll_GtOperator_FiltersCorrectly()
    {
        // Act — quantity > 30 should return entities with 40 and 50
        var response = await _client.GetAsync("/api/items?quantity[gt]=30");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(2);
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public async Task GetAll_LtOperator_FiltersCorrectly()
    {
        // Act — quantity < 30 should return entities with 10 and 20
        var response = await _client.GetAsync("/api/items?quantity[lt]=30");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(2);
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public async Task GetAll_GteOperator_FiltersCorrectly()
    {
        // Act — quantity >= 30 should return entities with 30, 40, and 50
        var response = await _client.GetAsync("/api/items?quantity[gte]=30");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(3);
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public async Task GetAll_LteOperator_FiltersCorrectly()
    {
        // Act — quantity <= 30 should return entities with 10, 20, and 30
        var response = await _client.GetAsync("/api/items?quantity[lte]=30");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(3);
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public async Task GetAll_RangeQuery_GteAndLte_FiltersCorrectly()
    {
        // Act — 20 <= quantity <= 40
        var response = await _client.GetAsync("/api/items?quantity[gte]=20&quantity[lte]=40");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(3); // 20, 30, 40
    }

    #endregion

    #region Not Equal

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public async Task GetAll_NeqOperator_FiltersCorrectly()
    {
        // Act — quantity != 10
        var response = await _client.GetAsync("/api/items?quantity[neq]=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(4); // All except quantity=10
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public async Task GetAll_NeqOnBool_FiltersCorrectly()
    {
        // Act — is_active != true (should return inactive items)
        var response = await _client.GetAsync("/api/items?is_active[neq]=true");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(2); // Gadget Gamma and Sprocket Epsilon
    }

    #endregion

    #region String Operators

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public async Task GetAll_ContainsOperator_FiltersCorrectly()
    {
        // Act — name contains "widget" (case-insensitive)
        var response = await _client.GetAsync("/api/items?name[contains]=widget");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(2); // Widget Alpha, Widget Beta
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public async Task GetAll_StartsWithOperator_FiltersCorrectly()
    {
        // Act — name starts_with "Gadget"
        var response = await _client.GetAsync("/api/items?name[starts_with]=Gadget");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(2); // Gadget Gamma, Gadget Delta
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public async Task GetAll_ContainsOperator_CaseInsensitive()
    {
        // Act — name contains "ALPHA" (uppercase, should still match "Widget Alpha")
        var response = await _client.GetAsync("/api/items?name[contains]=ALPHA");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public async Task GetAll_StartsWithOperator_CaseInsensitive()
    {
        // Act — name starts_with "sprocket" (lowercase)
        var response = await _client.GetAsync("/api/items?name[starts_with]=sprocket");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(1); // Sprocket Epsilon
    }

    #endregion

    #region In Operator

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public async Task GetAll_InOperator_FiltersCorrectly()
    {
        // Act — status in Active,Draft
        var response = await _client.GetAsync("/api/items?status[in]=Active,Draft");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(4); // 3 Active + 1 Draft
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public async Task GetAll_InOperator_SingleValue_FiltersCorrectly()
    {
        // Act — status in Discontinued
        var response = await _client.GetAsync("/api/items?status[in]=Discontinued");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(1);
    }

    #endregion

    #region Error Handling

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public async Task GetAll_DisallowedOperator_Returns400()
    {
        // Act — status does not allow Gte
        var response = await _client.GetAsync("/api/items?status[gte]=Active");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("type").GetString().Should().Be("/problems/invalid-filter");
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public async Task GetAll_UnknownOperator_Returns400()
    {
        // Act
        var response = await _client.GetAsync("/api/items?quantity[like]=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Unknown filter operator");
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public async Task GetAll_InvalidValueForOperator_Returns400()
    {
        // Act — "abc" is not a valid integer for gte
        var response = await _client.GetAsync("/api/items?quantity[gte]=abc");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public async Task GetAll_BareEquality_StillWorks()
    {
        // Act — bare price=9.99 should still work (backward compatible)
        var response = await _client.GetAsync("/api/items?price=9.99");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(1);
    }

    #endregion

    #region Decimal Comparison

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public async Task GetAll_DecimalGte_FiltersCorrectly()
    {
        // Act — price >= 29.99 should return 3 items (29.99, 49.99, 99.99)
        var response = await _client.GetAsync("/api/items?price[gte]=29.99");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(3);
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public async Task GetAll_DecimalLt_FiltersCorrectly()
    {
        // Act — price < 20 should return 2 items (9.99, 19.99)
        var response = await _client.GetAsync("/api/items?price[lt]=20");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(2);
    }

    #endregion

    #region Combined Operator + Other Query Params

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public async Task GetAll_OperatorWithPagination_WorksTogether()
    {
        // Act — quantity >= 10 with limit=2
        var response = await _client.GetAsync("/api/items?quantity[gte]=10&limit=2");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(2);

        // Should have a next link
        json.TryGetProperty("next", out var next).Should().BeTrue();
        next.GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public async Task GetAll_MultiplePropertiesWithDifferentOperators_WorksTogether()
    {
        // Act — price >= 10 AND name contains "Widget"
        var response = await _client.GetAsync("/api/items?price[gte]=10&name[contains]=Widget");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(1); // Only Widget Beta (price 19.99, name contains "Widget")
    }

    #endregion
}

/// <summary>
/// Tests that <see cref="RestLibOptions.MaxFilterInListSize"/> is configurable
/// and threaded through to the filter parser.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "Filtering")]
public class MaxFilterInListSizeTests : IDisposable
{
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly InMemoryRepository<FilterableEntity, Guid> _repository;

    public MaxFilterInListSizeTests()
    {
        _repository = new InMemoryRepository<FilterableEntity, Guid>(e => e.Id, Guid.NewGuid);

        (_host, _client) = new TestHostBuilder<FilterableEntity, Guid>(_repository, "/api/items")
            .WithOptions(options =>
            {
                options.MaxFilterInListSize = 3;
            })
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.AllowFiltering(p => p.Quantity, FilterOperator.In);
            })
            .Build();
    }

    public void Dispose()
    {
        _client.Dispose();
        _host.Dispose();
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public async Task GetAll_InListWithinCustomLimit_Succeeds()
    {
        // Arrange — custom limit is 3, send exactly 3 values
        await _repository.CreateAsync(new FilterableEntity { Id = Guid.NewGuid(), Name = "A", Quantity = 1 });

        // Act
        var response = await _client.GetAsync("/api/items?quantity[in]=1,2,3");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Category", "Story4.3.Operators")]
    public async Task GetAll_InListExceedingCustomLimit_ReturnsBadRequest()
    {
        // Arrange — custom limit is 3, send 4 values
        // Act
        var response = await _client.GetAsync("/api/items?quantity[in]=1,2,3,4");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var errors = json.GetProperty("errors").GetProperty("quantity[in]");
        var errorMessage = errors[0].GetString();
        errorMessage.Should().Contain("maximum");
        errorMessage.Should().Contain("3");
    }
}
