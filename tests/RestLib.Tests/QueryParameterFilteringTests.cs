using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestLib.Abstractions;
using RestLib.Configuration;
using RestLib.Filtering;
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
/// Repository that supports filtering for testing.
/// </summary>
public class FilterableRepository : IRepository<FilterableEntity, Guid>
{
  private readonly Dictionary<Guid, FilterableEntity> _store = new();

  public void Seed(FilterableEntity entity) => _store[entity.Id] = entity;
  public void SeedMany(IEnumerable<FilterableEntity> entities)
  {
    foreach (var entity in entities)
      _store[entity.Id] = entity;
  }
  public void Clear() => _store.Clear();

  public Task<FilterableEntity?> GetByIdAsync(Guid id, CancellationToken ct = default)
  {
    _store.TryGetValue(id, out var entity);
    return Task.FromResult(entity);
  }

  public Task<PagedResult<FilterableEntity>> GetAllAsync(PaginationRequest pagination, CancellationToken ct = default)
  {
    IEnumerable<FilterableEntity> query = _store.Values;

    // Apply filters
    foreach (var filter in pagination.Filters)
    {
      query = filter.PropertyName switch
      {
        "IsActive" => query.Where(e => e.IsActive == (bool)filter.TypedValue!),
        "CategoryId" => query.Where(e => e.CategoryId == (Guid?)filter.TypedValue),
        "Quantity" => query.Where(e => e.Quantity == (int)filter.TypedValue!),
        "Status" => query.Where(e => e.Status == (ProductStatus)filter.TypedValue!),
        "Name" => query.Where(e => e.Name == (string)filter.TypedValue!),
        "Price" => query.Where(e => e.Price == (decimal)filter.TypedValue!),
        _ => query
      };
    }

    var items = query.Take(pagination.Limit).ToList();
    var hasMore = query.Count() > pagination.Limit;

    return Task.FromResult(new PagedResult<FilterableEntity>
    {
      Items = items,
      NextCursor = hasMore ? CursorEncoder.Encode(items.Last().Id) : null
    });
  }

  public Task<FilterableEntity> CreateAsync(FilterableEntity entity, CancellationToken ct = default)
  {
    _store[entity.Id] = entity;
    return Task.FromResult(entity);
  }

  public Task<FilterableEntity?> UpdateAsync(Guid id, FilterableEntity entity, CancellationToken ct = default)
  {
    if (!_store.ContainsKey(id)) return Task.FromResult<FilterableEntity?>(null);
    _store[id] = entity;
    return Task.FromResult<FilterableEntity?>(entity);
  }

  public Task<FilterableEntity?> PatchAsync(Guid id, JsonElement patchDocument, CancellationToken ct = default)
  {
    if (!_store.TryGetValue(id, out var entity)) return Task.FromResult<FilterableEntity?>(null);
    return Task.FromResult<FilterableEntity?>(entity);
  }

  public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
  {
    return Task.FromResult(_store.Remove(id));
  }
}

/// <summary>
/// Tests for Story 4.3: Query Parameter Filtering.
/// Verifies filter configuration, snake_case param names, validation, and pagination integration.
/// </summary>
public class QueryParameterFilteringTests : IDisposable
{
  private readonly IHost _host;
  private readonly HttpClient _client;
  private readonly FilterableRepository _repository;

  public QueryParameterFilteringTests()
  {
    _repository = new FilterableRepository();

    _host = new HostBuilder()
        .ConfigureWebHost(webBuilder =>
        {
          webBuilder
                  .UseTestServer()
                  .ConfigureServices(services =>
                  {
                    services.AddRestLib();
                    services.AddSingleton<IRepository<FilterableEntity, Guid>>(_repository);
                    services.AddRouting();
                  })
                  .Configure(app =>
                  {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                      endpoints.MapRestLib<FilterableEntity, Guid>("/api/items", config =>
                      {
                        config.AllowAnonymous();
                        config.AllowFiltering(
                            p => p.IsActive,
                            p => p.CategoryId,
                            p => p.Quantity,
                            p => p.Status,
                            p => p.Name,
                            p => p.Price
                        );
                      });
                    });
                  });
        })
        .Build();

    _host.Start();
    _client = _host.GetTestClient();
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
    _repository.SeedMany([
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
    _repository.SeedMany([
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
    _repository.SeedMany([
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
    _repository.SeedMany([
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
    _repository.SeedMany([
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
    _repository.SeedMany([
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
    _repository.Seed(new FilterableEntity { Id = Guid.NewGuid(), Name = "Test", IsActive = true });

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
    _repository.SeedMany([
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
    _repository.Seed(new FilterableEntity { Id = Guid.NewGuid(), Name = "Test" });

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
    _repository.Seed(new FilterableEntity { Id = Guid.NewGuid(), Name = "Test" });

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
    _repository.Seed(new FilterableEntity { Id = Guid.NewGuid(), Name = "Test" });

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
    _repository.Seed(new FilterableEntity { Id = Guid.NewGuid(), Name = "Test" });

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
    _repository.Seed(new FilterableEntity { Id = Guid.NewGuid(), Name = "Test" });

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
      _repository.Seed(new FilterableEntity
      {
        Id = Guid.NewGuid(),
        Name = $"Active {i}",
        IsActive = true
      });
    }
    _repository.Seed(new FilterableEntity { Id = Guid.NewGuid(), Name = "Inactive", IsActive = false });

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
      _repository.Seed(new FilterableEntity
      {
        Id = Guid.NewGuid(),
        Name = $"Active {i}",
        IsActive = true,
        Quantity = 10
      });
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
    _repository.SeedMany([
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
    _repository.SeedMany([
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
    _repository.SeedMany([
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
    _repository.SeedMany([
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
    _repository.SeedMany([
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
    _repository.SeedMany([
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
public class NoFilterConfigurationTests : IDisposable
{
  private readonly IHost _host;
  private readonly HttpClient _client;
  private readonly FilterableRepository _repository;

  public NoFilterConfigurationTests()
  {
    _repository = new FilterableRepository();

    _host = new HostBuilder()
        .ConfigureWebHost(webBuilder =>
        {
          webBuilder
                  .UseTestServer()
                  .ConfigureServices(services =>
                  {
                    services.AddRestLib();
                    services.AddSingleton<IRepository<FilterableEntity, Guid>>(_repository);
                    services.AddRouting();
                  })
                  .Configure(app =>
                  {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                      // No filter configuration
                      endpoints.MapRestLib<FilterableEntity, Guid>("/api/items", config =>
                      {
                        config.AllowAnonymous();
                        // No AllowFiltering call
                      });
                    });
                  });
        })
        .Build();

    _host.Start();
    _client = _host.GetTestClient();
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
    _repository.SeedMany([
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
    _repository.Seed(new FilterableEntity { Id = Guid.NewGuid(), Name = "Test" });

    // Act - invalid value, but filters not configured so it's just ignored
    var response = await _client.GetAsync("/api/items?is_active=invalid");

    // Assert - still succeeds, param ignored
    response.StatusCode.Should().Be(HttpStatusCode.OK);
  }
}

/// <summary>
/// Tests for FilterParser unit behavior.
/// </summary>
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
public class FilterOpenApiTests : IDisposable
{
  private readonly IHost _host;
  private readonly HttpClient _client;

  public FilterOpenApiTests()
  {
    _host = new HostBuilder()
        .ConfigureWebHost(webBuilder =>
        {
          webBuilder
                  .UseTestServer()
                  .ConfigureServices(services =>
                  {
                    services.AddRestLib();
                    services.AddSingleton<IRepository<FilterableEntity, Guid>>(new FilterableRepository());
                    services.AddRouting();
                    services.AddEndpointsApiExplorer();
                  })
                  .Configure(app =>
                  {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                      endpoints.MapRestLib<FilterableEntity, Guid>("/api/items", config =>
                      {
                        config.AllowAnonymous();
                        config.AllowFiltering(
                            p => p.IsActive,
                            p => p.Quantity,
                            p => p.Status
                        );
                      });
                    });
                  });
        })
        .Build();

    _host.Start();
    _client = _host.GetTestClient();
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
