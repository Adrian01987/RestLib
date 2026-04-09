using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using RestLib.InMemory;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Entity for sorting integration tests.
/// </summary>
public class SortableEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Category { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Integration tests for sorting / ordering support (Story 5.1).
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "Sorting")]
public class SortingTests : IDisposable
{
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly InMemoryRepository<SortableEntity, Guid> _repository;

    public SortingTests()
    {
        _repository = new InMemoryRepository<SortableEntity, Guid>(
            e => e.Id,
            Guid.NewGuid);

        (_host, _client) = new TestHostBuilder<SortableEntity, Guid>(_repository, "/api/sortable")
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.AllowSorting(
                    p => p.Price,
                    p => p.Name,
                    p => p.Category,
                    p => p.Quantity);
                config.AllowFiltering(p => p.Category);
            })
            .Build();

        SeedData();
    }

    private void SeedData()
    {
        _repository.Seed([
            new SortableEntity { Id = Guid.NewGuid(), Name = "Banana", Price = 1.50m, Category = "Fruit", Quantity = 100, CreatedAt = new DateTime(2025, 1, 1) },
        new SortableEntity { Id = Guid.NewGuid(), Name = "Apple", Price = 2.00m, Category = "Fruit", Quantity = 50, CreatedAt = new DateTime(2025, 2, 1) },
        new SortableEntity { Id = Guid.NewGuid(), Name = "Carrot", Price = 0.75m, Category = "Vegetable", Quantity = 200, CreatedAt = new DateTime(2025, 3, 1) },
        new SortableEntity { Id = Guid.NewGuid(), Name = "Date", Price = 5.00m, Category = "Fruit", Quantity = 30, CreatedAt = new DateTime(2025, 4, 1) },
        new SortableEntity { Id = Guid.NewGuid(), Name = "Eggplant", Price = 3.00m, Category = "Vegetable", Quantity = 80, CreatedAt = new DateTime(2025, 5, 1) },
    ]);
    }

    public void Dispose()
    {
        _client.Dispose();
        _host.Dispose();
    }

    [Fact]
    [Trait("Category", "Story5.1")]
    public async Task GetAll_WithSortAsc_ReturnsOrderedResults()
    {
        // Act
        var response = await _client.GetAsync("/api/sortable?sort=price:asc");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        var prices = Enumerable.Range(0, items.GetArrayLength())
            .Select(i => items[i].GetProperty("price").GetDecimal())
            .ToList();
        prices.Should().BeInAscendingOrder();
    }

    [Fact]
    [Trait("Category", "Story5.1")]
    public async Task GetAll_WithSortDesc_ReturnsOrderedResults()
    {
        // Act
        var response = await _client.GetAsync("/api/sortable?sort=price:desc");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        var prices = Enumerable.Range(0, items.GetArrayLength())
            .Select(i => items[i].GetProperty("price").GetDecimal())
            .ToList();
        prices.Should().BeInDescendingOrder();
    }

    [Fact]
    [Trait("Category", "Story5.1")]
    public async Task GetAll_WithMultiFieldSort_ReturnsCorrectOrder()
    {
        // Act — sort by category asc, then price asc
        var response = await _client.GetAsync("/api/sortable?sort=category:asc,price:asc");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        var results = Enumerable.Range(0, items.GetArrayLength())
            .Select(i => (
                Category: items[i].GetProperty("category").GetString()!,
                Price: items[i].GetProperty("price").GetDecimal()))
            .ToList();

        // All Fruit items should come before Vegetable items
        var fruitItems = results.Where(r => r.Category == "Fruit").ToList();
        var vegItems = results.Where(r => r.Category == "Vegetable").ToList();
        fruitItems.Select(f => f.Price).Should().BeInAscendingOrder();
        vegItems.Select(v => v.Price).Should().BeInAscendingOrder();
        results.IndexOf(fruitItems.Last()).Should().BeLessThan(results.IndexOf(vegItems.First()));
    }

    [Fact]
    [Trait("Category", "Story5.1")]
    public async Task GetAll_WithInvalidSortField_Returns400()
    {
        // Act
        var response = await _client.GetAsync("/api/sortable?sort=unknown_field:asc");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("type").GetString().Should().Be("/problems/invalid-sort");
        json.GetProperty("status").GetInt32().Should().Be(400);
        json.TryGetProperty("errors", out var errors).Should().BeTrue();
        errors.TryGetProperty("unknown_field", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Story5.1")]
    public async Task GetAll_WithInvalidDirection_Returns400()
    {
        // Act
        var response = await _client.GetAsync("/api/sortable?sort=price:sideways");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("type").GetString().Should().Be("/problems/invalid-sort");
    }

    [Fact]
    [Trait("Category", "Story5.1")]
    public async Task GetAll_SortPreservedInPaginationLinks()
    {
        // Act — request with sort + small limit to force pagination
        var response = await _client.GetAsync("/api/sortable?sort=price:asc&limit=2");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        var self = json.GetProperty("self").GetString()!;
        var first = json.GetProperty("first").GetString()!;
        var next = json.GetProperty("next").GetString()!;

        foreach (var link in new[] { self, first, next })
        {
            link.Should().Contain("sort=price%3Aasc");
            link.Should().Contain("limit=2");
        }
    }

    [Fact]
    [Trait("Category", "Story5.1")]
    public async Task GetAll_SortCombinesWithFilters()
    {
        // Act — filter to Fruit only, sort by price desc
        var response = await _client.GetAsync("/api/sortable?category=Fruit&sort=price:desc");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(3); // Banana, Apple, Date

        var prices = Enumerable.Range(0, items.GetArrayLength())
            .Select(i => items[i].GetProperty("price").GetDecimal())
            .ToList();
        prices.Should().BeInDescendingOrder();
    }

    [Fact]
    [Trait("Category", "Story5.1")]
    public async Task GetAll_SortCombinesWithPagination()
    {
        // Act — sort by name asc, limit to 3
        var response1 = await _client.GetAsync("/api/sortable?sort=name:asc&limit=3");

        // Assert
        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        var json1 = await response1.Content.ReadFromJsonAsync<JsonElement>();
        var items1 = json1.GetProperty("items");
        items1.GetArrayLength().Should().Be(3);

        var names1 = Enumerable.Range(0, items1.GetArrayLength())
            .Select(i => items1[i].GetProperty("name").GetString()!)
            .ToList();
        names1.Should().BeInAscendingOrder();
        names1[0].Should().Be("Apple");
        names1[1].Should().Be("Banana");
        names1[2].Should().Be("Carrot");

        // Act — Get next page
        var nextUrl = json1.GetProperty("next").GetString()!;
        // The URL is absolute, extract path+query for test client
        var uri = new Uri(nextUrl);
        var response2 = await _client.GetAsync(uri.PathAndQuery);

        // Assert
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
        var json2 = await response2.Content.ReadFromJsonAsync<JsonElement>();
        var items2 = json2.GetProperty("items");
        items2.GetArrayLength().Should().Be(2);

        var names2 = Enumerable.Range(0, items2.GetArrayLength())
            .Select(i => items2[i].GetProperty("name").GetString()!)
            .ToList();
        names2.Should().BeInAscendingOrder();
        names2[0].Should().Be("Date");
        names2[1].Should().Be("Eggplant");
    }

    [Fact]
    [Trait("Category", "Story5.1")]
    public async Task GetAll_SortProblemDetails_HasCorrectFormat()
    {
        // Act
        var response = await _client.GetAsync("/api/sortable?sort=bad_field:asc");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("type").GetString().Should().Be("/problems/invalid-sort");
        json.GetProperty("title").GetString().Should().Be("Invalid Sort Parameter");
        json.GetProperty("status").GetInt32().Should().Be(400);
        json.TryGetProperty("detail", out _).Should().BeTrue();
        json.TryGetProperty("errors", out var errors).Should().BeTrue();
        errors.EnumerateObject().Should().HaveCountGreaterThan(0);
    }
}

/// <summary>
/// Tests for default sort and no-sort configuration scenarios.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "Sorting")]
public class SortingDefaultSortTests : IDisposable
{
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly InMemoryRepository<SortableEntity, Guid> _repository;

    public SortingDefaultSortTests()
    {
        _repository = new InMemoryRepository<SortableEntity, Guid>(
            e => e.Id,
            Guid.NewGuid);

        (_host, _client) = new TestHostBuilder<SortableEntity, Guid>(_repository, "/api/sorted")
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.AllowSorting(p => p.Price, p => p.Name);
                config.DefaultSort("name:asc");
            })
            .Build();

        _repository.Seed([
            new SortableEntity { Id = Guid.NewGuid(), Name = "Cherry", Price = 4.00m, Category = "Fruit", Quantity = 60 },
        new SortableEntity { Id = Guid.NewGuid(), Name = "Apple", Price = 2.00m, Category = "Fruit", Quantity = 50 },
        new SortableEntity { Id = Guid.NewGuid(), Name = "Banana", Price = 1.50m, Category = "Fruit", Quantity = 100 },
    ]);
    }

    public void Dispose()
    {
        _client.Dispose();
        _host.Dispose();
    }

    [Fact]
    [Trait("Category", "Story5.1")]
    public async Task GetAll_WithDefaultSort_AppliesWhenNoSortParam()
    {
        // Act — no sort parameter, default is "name:asc"
        var response = await _client.GetAsync("/api/sorted");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        var names = Enumerable.Range(0, items.GetArrayLength())
            .Select(i => items[i].GetProperty("name").GetString()!)
            .ToList();
        names.Should().BeInAscendingOrder();
        names.Should().Equal("Apple", "Banana", "Cherry");
    }

    [Fact]
    [Trait("Category", "Story5.1")]
    public async Task GetAll_WithExplicitSort_OverridesDefaultSort()
    {
        // Act — explicit sort by price:desc should override default name:asc
        var response = await _client.GetAsync("/api/sorted?sort=price:desc");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        var prices = Enumerable.Range(0, items.GetArrayLength())
            .Select(i => items[i].GetProperty("price").GetDecimal())
            .ToList();
        prices.Should().BeInDescendingOrder();
        prices.Should().Equal(4.00m, 2.00m, 1.50m);
    }
}

/// <summary>
/// Tests for sorting when no sort configuration is applied.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "Sorting")]
public class SortingNoConfigTests : IDisposable
{
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly InMemoryRepository<SortableEntity, Guid> _repository;

    public SortingNoConfigTests()
    {
        _repository = new InMemoryRepository<SortableEntity, Guid>(
            e => e.Id,
            Guid.NewGuid);

        (_host, _client) = new TestHostBuilder<SortableEntity, Guid>(_repository, "/api/unsorted")
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();

                // No AllowSorting call
            })
            .Build();

        _repository.Seed([
            new SortableEntity { Id = Guid.NewGuid(), Name = "Banana", Price = 1.50m, Category = "Fruit", Quantity = 100 },
        new SortableEntity { Id = Guid.NewGuid(), Name = "Apple", Price = 2.00m, Category = "Fruit", Quantity = 50 },
    ]);
    }

    public void Dispose()
    {
        _client.Dispose();
        _host.Dispose();
    }

    [Fact]
    [Trait("Category", "Story5.1")]
    public async Task GetAll_WithoutSortConfig_SortParamIgnored()
    {
        // Act — sort param present but sorting not configured
        var response = await _client.GetAsync("/api/unsorted?sort=name:asc");

        // Assert — should succeed without error, param silently ignored
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("items").GetArrayLength().Should().Be(2);
    }
}
