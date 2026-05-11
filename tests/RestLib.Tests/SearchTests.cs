using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using RestLib.InMemory;
using RestLib.Responses;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Integration tests for collection search.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "Search")]
public class SearchTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private InMemoryRepository<SearchableEntity, Guid> _repository = null!;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _repository = new InMemoryRepository<SearchableEntity, Guid>(entity => entity.Id, Guid.NewGuid);

        (_host, _client) = await new TestHostBuilder<SearchableEntity, Guid>(_repository, "/api/items")
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.AllowFiltering(entity => entity.IsActive);
                config.AllowSorting(entity => entity.Name);
                config.AllowFieldSelection(entity => entity.Name, entity => entity.Description, entity => entity.Customer!.Email);
                config.AllowSearch(entity => entity.Name, entity => entity.Description!);
            })
            .BuildAsync();
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task AllowSearch_WithQuery_ReturnsItemsMatchingAnyConfiguredProperty()
    {
        // Arrange
        SeedEntities(
        [
            CreateEntity("Blue Widget", "Basic item"),
            CreateEntity("Gadget", "Widget-compatible accessory"),
            CreateEntity("Hammer", "Heavy tool")
        ]);

        // Act
        var response = await _client.GetAsync("/api/items?q=widget");
        var json = await ReadJsonAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.GetProperty("items").GetArrayLength().Should().Be(2);
        json.GetProperty("total_count").GetInt64().Should().Be(2);
    }

    [Fact]
    public async Task AllowSearch_WithNoQuery_ReturnsUnfilteredResults()
    {
        // Arrange
        SeedEntities(
        [
            CreateEntity("One", "Alpha"),
            CreateEntity("Two", "Beta")
        ]);

        // Act
        var response = await _client.GetAsync("/api/items");
        var json = await ReadJsonAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.GetProperty("items").GetArrayLength().Should().Be(2);
        json.GetProperty("total_count").GetInt64().Should().Be(2);
    }

    [Fact]
    public async Task AllowSearch_WithWhitespaceQuery_ReturnsUnfilteredResults()
    {
        // Arrange
        SeedEntities(
        [
            CreateEntity("One", "Alpha"),
            CreateEntity("Two", "Beta")
        ]);

        // Act
        var response = await _client.GetAsync("/api/items?q=%20%20%20");
        var json = await ReadJsonAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.GetProperty("items").GetArrayLength().Should().Be(2);
        json.GetProperty("total_count").GetInt64().Should().Be(2);
    }

    [Fact]
    public async Task AllowSearch_WithMultipleQueryValues_ReturnsBadRequest()
    {
        // Arrange
        SeedEntities([CreateEntity("Blue Widget", "Basic item")]);

        // Act
        var response = await _client.GetAsync("/api/items?q=alpha&q=beta");

        // Assert
        var problem = await response.ShouldBeProblemDetailsJson(
            HttpStatusCode.BadRequest,
            ProblemTypes.Resolve(ProblemTypes.InvalidSearch),
            expectedTitle: "Invalid Search Parameter");
        problem.GetProperty("errors").GetProperty("q")[0].GetString().Should().Contain("Multiple values");
    }

    [Fact]
    public async Task AllowSearch_WithCaseInsensitiveDefault_MatchesDifferentCase()
    {
        // Arrange
        SeedEntities([CreateEntity("Blue Widget", "Basic item")]);

        // Act
        var response = await _client.GetAsync("/api/items?q=WIDGET");
        var json = await ReadJsonAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.GetProperty("items").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task AllowSearch_WithCaseSensitiveOption_RequiresExactCase()
    {
        // Arrange
        var (caseSensitiveHost, client, repository) = await CreateCaseSensitiveHostAsync();
        using var _ = caseSensitiveHost;
        repository.Seed([CreateEntity("Blue Widget", "Basic item")]);

        // Act
        var exactCase = await client.GetAsync("/api/items?query=Widget");
        var wrongCase = await client.GetAsync("/api/items?query=widget");

        // Assert
        (await ReadJsonAsync(exactCase)).GetProperty("items").GetArrayLength().Should().Be(1);
        (await ReadJsonAsync(wrongCase)).GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task AllowSearch_WithNestedPath_SearchesNestedStringProperty()
    {
        // Arrange
        var (nestedHost, client, repository) = await CreateNestedHostAsync();
        using var _ = nestedHost;
        repository.Seed(
        [
            CreateEntity("Widget", "Basic item", "zoe@example.com"),
            CreateEntity("Gadget", "Other item", "adam@example.com")
        ]);

        // Act
        var response = await client.GetAsync("/api/items?q=zoe@example.com");
        var json = await ReadJsonAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.GetProperty("items").GetArrayLength().Should().Be(1);
        json.GetProperty("items")[0].GetProperty("name").GetString().Should().Be("Widget");
    }

    [Fact]
    public async Task AllowSearch_ComposesWithFilteringSortingAndFieldSelection()
    {
        // Arrange
        var (nestedHost, client, repository) = await CreateNestedHostAsync();
        using var _ = nestedHost;
        repository.Seed(
        [
            CreateEntity("Beta Widget", "First", "beta@example.com", isActive: true),
            CreateEntity("Alpha Widget", "Second", "alpha@example.com", isActive: true),
            CreateEntity("Dormant Widget", "Third", "dormant@example.com", isActive: false)
        ]);

        // Act
        var response = await client.GetAsync("/api/items?q=widget&is_active=true&sort=name:asc&fields=name,customer.email");
        var json = await ReadJsonAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(2);
        items[0].GetProperty("name").GetString().Should().Be("Alpha Widget");
        items[0].GetProperty("customer.email").GetString().Should().Be("alpha@example.com");
        json.GetProperty("total_count").GetInt64().Should().Be(2);
    }

    private static SearchableEntity CreateEntity(
        string name,
        string? description,
        string customerEmail = "user@example.com",
        bool isActive = true)
    {
        return new SearchableEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            IsActive = isActive,
            Customer = new SearchCustomer { Email = customerEmail }
        };
    }

    private static async Task<(IHost Host, HttpClient Client, InMemoryRepository<SearchableEntity, Guid> Repository)> CreateCaseSensitiveHostAsync()
    {
        var repository = new InMemoryRepository<SearchableEntity, Guid>(entity => entity.Id, Guid.NewGuid);
        var (host, client) = await new TestHostBuilder<SearchableEntity, Guid>(repository, "/api/items")
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.AllowSearch(options =>
                {
                    options.QueryParameterName = "query";
                    options.CaseSensitive = true;
                }, entity => entity.Name);
            })
            .BuildAsync();

        return (host, client, repository);
    }

    private static async Task<(IHost Host, HttpClient Client, InMemoryRepository<SearchableEntity, Guid> Repository)> CreateNestedHostAsync()
    {
        var repository = new InMemoryRepository<SearchableEntity, Guid>(entity => entity.Id, Guid.NewGuid);
        var (host, client) = await new TestHostBuilder<SearchableEntity, Guid>(repository, "/api/items")
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.AllowFiltering(entity => entity.IsActive);
                config.AllowSorting(entity => entity.Name);
                config.AllowFieldSelection(entity => entity.Name, entity => entity.Customer!.Email);
                config.AllowSearch(entity => entity.Name, entity => entity.Customer!.Email);
            })
            .BuildAsync();

        return (host, client, repository);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private void SeedEntities(IReadOnlyList<SearchableEntity> entities)
    {
        _repository.Clear();
        _repository.Seed(entities);
    }
}

/// <summary>
/// Test entity for search integration tests.
/// </summary>
public class SearchableEntity
{
    /// <summary>
    /// Gets or sets the identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the entity is active.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets or sets the nested customer.
    /// </summary>
    public SearchCustomer? Customer { get; set; }
}

/// <summary>
/// Nested customer entity used in search tests.
/// </summary>
public class SearchCustomer
{
    /// <summary>
    /// Gets or sets the email.
    /// </summary>
    public string Email { get; set; } = string.Empty;
}
