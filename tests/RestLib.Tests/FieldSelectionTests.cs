using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestLib.Abstractions;
using RestLib.Configuration;
using RestLib.FieldSelection;
using RestLib.InMemory;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Entity for field selection integration tests.
/// </summary>
public class FieldSelectableEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Category { get; set; } = string.Empty;
    public string InternalNotes { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Integration tests for field selection / sparse fieldsets (Story 7.1).
/// </summary>
public class FieldSelectionTests : IDisposable
{
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly InMemoryRepository<FieldSelectableEntity, Guid> _repository;

    private readonly Guid _knownId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    public FieldSelectionTests()
    {
        _repository = new InMemoryRepository<FieldSelectableEntity, Guid>(
            e => e.Id,
            Guid.NewGuid);

        (_host, _client) = new TestHostBuilder<FieldSelectableEntity, Guid>(_repository, "/api/items")
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.AllowFieldSelection(
                    p => p.Id,
                    p => p.Name,
                    p => p.Price,
                    p => p.Category,
                    p => p.CreatedAt);
                config.AllowFiltering(p => p.Category);
                config.AllowSorting(p => p.Price, p => p.Name);
            })
            .Build();

        SeedData();
    }

    private void SeedData()
    {
        _repository.Seed([
            new FieldSelectableEntity
        {
          Id = _knownId,
          Name = "Keyboard",
          Price = 49.99m,
          Category = "Electronics",
          InternalNotes = "Supplier: Acme Corp",
          CreatedAt = new DateTime(2025, 1, 1)
        },
        new FieldSelectableEntity
        {
          Id = Guid.NewGuid(),
          Name = "Mouse",
          Price = 29.99m,
          Category = "Electronics",
          InternalNotes = "Clearance item",
          CreatedAt = new DateTime(2025, 2, 1)
        },
        new FieldSelectableEntity
        {
          Id = Guid.NewGuid(),
          Name = "Desk",
          Price = 199.99m,
          Category = "Furniture",
          InternalNotes = "Heavy item",
          CreatedAt = new DateTime(2025, 3, 1)
        },
    ]);
    }

    public void Dispose()
    {
        _client.Dispose();
        _host.Dispose();
    }

    // ──────────────────────── GetById Tests ────────────────────────

    [Fact]
    [Trait("Category", "Story7.1")]
    public async Task GetById_WithFields_ReturnsOnlySelectedFields()
    {
        // Act
        var response = await _client.GetAsync($"/api/items/{_knownId}?fields=id,name");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("id", out _).Should().BeTrue();
        json.TryGetProperty("name", out _).Should().BeTrue();
        json.GetProperty("name").GetString().Should().Be("Keyboard");
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public async Task GetById_WithFields_OmitsUnselectedFields()
    {
        // Act
        var response = await _client.GetAsync($"/api/items/{_knownId}?fields=id");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("id", out _).Should().BeTrue();
        json.TryGetProperty("name", out _).Should().BeFalse();
        json.TryGetProperty("price", out _).Should().BeFalse();
        json.TryGetProperty("category", out _).Should().BeFalse();
        json.TryGetProperty("internal_notes", out _).Should().BeFalse();
        json.TryGetProperty("created_at", out _).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public async Task GetById_WithoutFields_ReturnsFullEntity()
    {
        // Act
        var response = await _client.GetAsync($"/api/items/{_knownId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("id", out _).Should().BeTrue();
        json.TryGetProperty("name", out _).Should().BeTrue();
        json.TryGetProperty("price", out _).Should().BeTrue();
        json.TryGetProperty("category", out _).Should().BeTrue();
        json.TryGetProperty("internal_notes", out _).Should().BeTrue();
        json.TryGetProperty("created_at", out _).Should().BeTrue();
    }

    // ──────────────────────── GetAll Tests ────────────────────────

    [Fact]
    [Trait("Category", "Story7.1")]
    public async Task GetAll_WithFields_ReturnsOnlySelectedFields()
    {
        // Act
        var response = await _client.GetAsync("/api/items?fields=id,name");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().BeGreaterThan(0);

        for (int i = 0; i < items.GetArrayLength(); i++)
        {
            var item = items[i];
            item.TryGetProperty("id", out _).Should().BeTrue();
            item.TryGetProperty("name", out _).Should().BeTrue();
            item.TryGetProperty("price", out _).Should().BeFalse();
            item.TryGetProperty("category", out _).Should().BeFalse();
            item.TryGetProperty("internal_notes", out _).Should().BeFalse();
        }
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public async Task GetAll_WithFields_PreservesCollectionWrapper()
    {
        // Act
        var response = await _client.GetAsync("/api/items?fields=id");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("items", out _).Should().BeTrue();
        json.TryGetProperty("self", out _).Should().BeTrue();
        json.TryGetProperty("first", out _).Should().BeTrue();
    }

    // ──────────────────────── Validation / Error Tests ────────────────────────

    [Fact]
    [Trait("Category", "Story7.1")]
    public async Task InvalidField_Returns400ProblemDetails()
    {
        // Act
        var response = await _client.GetAsync($"/api/items/{_knownId}?fields=id,secret_field");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("type").GetString().Should().Be("/problems/invalid-fields");
        json.GetProperty("title").GetString().Should().Be("Invalid Field Selection");
        json.GetProperty("status").GetInt32().Should().Be(400);

        var errors = json.GetProperty("errors");
        errors.TryGetProperty("secret_field", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public async Task MultipleInvalidFields_Returns400WithAllErrors()
    {
        // Act
        var response = await _client.GetAsync("/api/items?fields=bad1,bad2");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("type").GetString().Should().Be("/problems/invalid-fields");

        var errors = json.GetProperty("errors");
        errors.TryGetProperty("bad1", out _).Should().BeTrue();
        errors.TryGetProperty("bad2", out _).Should().BeTrue();
    }

    // ──────────────────────── Snake_case Tests ────────────────────────

    [Fact]
    [Trait("Category", "Story7.1")]
    public async Task FieldsUseSnakeCase()
    {
        // Act — snake_case should work
        var response = await _client.GetAsync($"/api/items/{_knownId}?fields=created_at");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("created_at", out _).Should().BeTrue();

        // Act — PascalCase should be rejected (not in the allow-list)
        var badResponse = await _client.GetAsync($"/api/items/{_knownId}?fields=CreatedAt");

        // Assert
        badResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ──────────────────────── Edge Case Tests ────────────────────────

    [Fact]
    [Trait("Category", "Story7.1")]
    public async Task EmptyFieldsParam_ReturnsFullEntity()
    {
        // Act
        var response = await _client.GetAsync($"/api/items/{_knownId}?fields=");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("id", out _).Should().BeTrue();
        json.TryGetProperty("name", out _).Should().BeTrue();
        json.TryGetProperty("price", out _).Should().BeTrue();
        json.TryGetProperty("category", out _).Should().BeTrue();
        json.TryGetProperty("internal_notes", out _).Should().BeTrue();
        json.TryGetProperty("created_at", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public async Task DuplicateFields_ReturnsBadRequest()
    {
        // Act
        var response = await _client.GetAsync($"/api/items/{_knownId}?fields=id,id,name");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("type").GetString().Should().Be("/problems/invalid-fields");
        json.GetProperty("errors").GetProperty("id").GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
    }

    // ──────────────────────── Combination Tests ────────────────────────

    [Fact]
    [Trait("Category", "Story7.1")]
    public async Task FieldsCombineWithFilters()
    {
        // Act
        var response = await _client.GetAsync("/api/items?category=Electronics&fields=id,name");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(2); // Keyboard + Mouse

        for (int i = 0; i < items.GetArrayLength(); i++)
        {
            var item = items[i];
            item.TryGetProperty("id", out _).Should().BeTrue();
            item.TryGetProperty("name", out _).Should().BeTrue();
            item.TryGetProperty("price", out _).Should().BeFalse();
            item.TryGetProperty("category", out _).Should().BeFalse();
        }
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public async Task FieldsCombineWithSort()
    {
        // Act
        var response = await _client.GetAsync("/api/items?sort=price:asc&fields=id,price");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().BeGreaterThan(0);

        var prices = Enumerable.Range(0, items.GetArrayLength())
            .Select(i => items[i].GetProperty("price").GetDecimal())
            .ToList();
        prices.Should().BeInAscendingOrder();

        // Verify projection
        for (int i = 0; i < items.GetArrayLength(); i++)
        {
            items[i].TryGetProperty("name", out _).Should().BeFalse();
        }
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public async Task FieldsCombineWithPagination()
    {
        // Act — first page
        var response = await _client.GetAsync("/api/items?fields=id,name&limit=2");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(2);

        for (int i = 0; i < items.GetArrayLength(); i++)
        {
            items[i].TryGetProperty("id", out _).Should().BeTrue();
            items[i].TryGetProperty("name", out _).Should().BeTrue();
            items[i].TryGetProperty("price", out _).Should().BeFalse();
        }

        // Next link should exist (we have 3 items, limit=2)
        json.TryGetProperty("next", out var nextEl).Should().BeTrue();
        nextEl.ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public async Task FieldsPreservedInPaginationLinks()
    {
        // Act
        var response = await _client.GetAsync("/api/items?fields=id,name&limit=2");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        // self link should contain fields param
        var selfLink = json.GetProperty("self").GetString();
        selfLink.Should().NotBeNull();
        selfLink.Should().Contain("fields=");

        // first link should contain fields param
        var firstLink = json.GetProperty("first").GetString();
        firstLink.Should().NotBeNull();
        firstLink.Should().Contain("fields=");

        // next link should contain fields param
        if (json.TryGetProperty("next", out var nextEl) && nextEl.ValueKind != JsonValueKind.Null)
        {
            var nextLink = nextEl.GetString();
            nextLink.Should().Contain("fields=");
        }
    }
}

/// <summary>
/// Integration tests for field selection when no AllowFieldSelection is configured.
/// </summary>
public class FieldSelectionNoConfigTests : IDisposable
{
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly InMemoryRepository<FieldSelectableEntity, Guid> _repository;

    private readonly Guid _knownId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    public FieldSelectionNoConfigTests()
    {
        _repository = new InMemoryRepository<FieldSelectableEntity, Guid>(
            e => e.Id,
            Guid.NewGuid);

        (_host, _client) = new TestHostBuilder<FieldSelectableEntity, Guid>(_repository, "/api/items")
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                // No AllowFieldSelection configured
            })
            .Build();

        _repository.Seed([
            new FieldSelectableEntity
        {
          Id = _knownId,
          Name = "Keyboard",
          Price = 49.99m,
          Category = "Electronics",
          InternalNotes = "Supplier: Acme Corp",
          CreatedAt = new DateTime(2025, 1, 1)
        },
    ]);
    }

    public void Dispose()
    {
        _client.Dispose();
        _host.Dispose();
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public async Task NoFieldSelectionConfig_FieldsParamIgnored()
    {
        // Act — fields param should be silently ignored
        var response = await _client.GetAsync($"/api/items/{_knownId}?fields=id");

        // Assert — full entity returned
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("id", out _).Should().BeTrue();
        json.TryGetProperty("name", out _).Should().BeTrue();
        json.TryGetProperty("price", out _).Should().BeTrue();
        json.TryGetProperty("category", out _).Should().BeTrue();
        json.TryGetProperty("internal_notes", out _).Should().BeTrue();
        json.TryGetProperty("created_at", out _).Should().BeTrue();
    }
}

/// <summary>
/// Unit tests for <see cref="FieldSelectionParser"/>.
/// </summary>
public class FieldSelectionParserTests
{
    private static FieldSelectionConfiguration<FieldSelectableEntity> CreateConfiguration()
    {
        var config = new FieldSelectionConfiguration<FieldSelectableEntity>();
        config.AddProperty(p => p.Id);
        config.AddProperty(p => p.Name);
        config.AddProperty(p => p.Price);
        config.AddProperty(p => p.Category);
        config.AddProperty(p => p.CreatedAt);
        return config;
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public void Parse_ValidFields_ReturnsSelectedFields()
    {
        // Arrange
        var config = CreateConfiguration();

        // Act
        var result = FieldSelectionParser.Parse("id,name,price", config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Fields.Should().HaveCount(3);
        result.Fields[0].PropertyName.Should().Be("Id");
        result.Fields[0].QueryFieldName.Should().Be("id");
        result.Fields[1].PropertyName.Should().Be("Name");
        result.Fields[2].PropertyName.Should().Be("Price");
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public void Parse_InvalidField_ReturnsError()
    {
        // Arrange
        var config = CreateConfiguration();

        // Act
        var result = FieldSelectionParser.Parse("id,unknown_field", config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Field.Should().Be("unknown_field");
        result.Errors[0].Message.Should().Contain("unknown_field");
        result.Errors[0].Message.Should().Contain("not a selectable field");
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public void Parse_EmptyString_ReturnsEmptyResult()
    {
        // Arrange
        var config = CreateConfiguration();

        // Act
        var result = FieldSelectionParser.Parse("", config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Fields.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public void Parse_Null_ReturnsEmptyResult()
    {
        // Arrange
        var config = CreateConfiguration();

        // Act
        var result = FieldSelectionParser.Parse(null, config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Fields.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public void Parse_DuplicateFields_ReturnsError()
    {
        // Arrange
        var config = CreateConfiguration();

        // Act
        var result = FieldSelectionParser.Parse("id,id,name", config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Field == "id" && e.Message == "Duplicate field.");
        result.Fields.Should().HaveCount(2);
        result.Fields[0].PropertyName.Should().Be("Id");
        result.Fields[1].PropertyName.Should().Be("Name");
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public void Parse_CaseInsensitiveMatching()
    {
        // Arrange
        var config = CreateConfiguration();

        // Act
        var result = FieldSelectionParser.Parse("ID,Name", config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Fields.Should().HaveCount(2);
        result.Fields[0].QueryFieldName.Should().Be("id");
        result.Fields[1].QueryFieldName.Should().Be("name");
    }
}

/// <summary>
/// Unit tests for <see cref="FieldSelectionConfiguration{TEntity}"/>.
/// </summary>
public class FieldSelectionConfigurationTests
{
    [Fact]
    [Trait("Category", "Story7.1")]
    public void Configuration_StoresCorrectSnakeCaseNames()
    {
        // Arrange & Act
        var config = new FieldSelectionConfiguration<FieldSelectableEntity>();
        config.AddProperty(p => p.CreatedAt);

        // Assert
        config.Properties.Should().HaveCount(1);
        config.Properties[0].PropertyName.Should().Be("CreatedAt");
        config.Properties[0].QueryFieldName.Should().Be("created_at");
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public void Configuration_DuplicateProperty_ThrowsInvalidOperationException()
    {
        // Arrange
        var config = new FieldSelectionConfiguration<FieldSelectableEntity>();
        config.AddProperty(p => p.CreatedAt);

        // Act
        var act = () => config.AddProperty(p => p.CreatedAt);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*'CreatedAt'*already configured*field selection*");
    }
}

/// <summary>
/// Integration tests for field selection configured via JSON resource configuration.
/// </summary>
public class FieldSelectionJsonConfigTests : IDisposable
{
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly InMemoryRepository<FieldSelectableEntity, Guid> _repository;

    private readonly Guid _knownId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    public FieldSelectionJsonConfigTests()
    {
        _repository = new InMemoryRepository<FieldSelectableEntity, Guid>(
            e => e.Id,
            Guid.NewGuid);

        _host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRestLib();
                        services.AddSingleton<IRepository<FieldSelectableEntity, Guid>>(_repository);
                        services.AddRouting();

                        services.AddJsonResource<FieldSelectableEntity, Guid>(
                        new RestLibJsonResourceConfiguration
                        {
                            Name = "field-select-items",
                            Route = "/api/fs-items",
                            AllowAnonymousAll = true,
                            FieldSelection = ["Id", "Name", "Price"],
                        });
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapJsonResources();
                    });
                    });
            })
            .Build();

        _host.Start();
        _client = _host.GetTestClient();

        _repository.Seed([
            new FieldSelectableEntity
        {
          Id = _knownId,
          Name = "Widget",
          Price = 9.99m,
          Category = "Gadgets",
          InternalNotes = "Secret note",
          CreatedAt = new DateTime(2025, 6, 1)
        },
    ]);
    }

    public void Dispose()
    {
        _client.Dispose();
        _host.Dispose();
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public async Task JsonConfig_AppliesFieldSelection()
    {
        // Act — request only id and name (price is allowed but not requested)
        var response = await _client.GetAsync($"/api/fs-items/{_knownId}?fields=id,name");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("id", out _).Should().BeTrue();
        json.TryGetProperty("name", out _).Should().BeTrue();
        json.GetProperty("name").GetString().Should().Be("Widget");

        // Fields not requested should be absent
        json.TryGetProperty("price", out _).Should().BeFalse();
        json.TryGetProperty("category", out _).Should().BeFalse();
        json.TryGetProperty("internal_notes", out _).Should().BeFalse();
        json.TryGetProperty("created_at", out _).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public async Task JsonConfig_RejectsFieldNotInAllowList()
    {
        // Act — category is not in the FieldSelection allow-list
        var response = await _client.GetAsync($"/api/fs-items/{_knownId}?fields=id,category");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("type").GetString().Should().Be("/problems/invalid-fields");

        var errors = json.GetProperty("errors");
        errors.TryGetProperty("category", out _).Should().BeTrue();
    }
}
