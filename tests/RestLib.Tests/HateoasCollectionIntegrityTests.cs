using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestLib.Abstractions;
using RestLib.Configuration;
using RestLib.InMemory;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Verifies that per-item HATEOAS metadata never changes collection data.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "HATEOAS")]
public class HateoasCollectionIntegrityTests
{
    [Fact]
    public async Task GetAllMapped_HateoasWithoutApiKeySelector_ThrowsInvalidOperationException()
    {
        // Arrange
        var repository = new InMemoryRepository<MappedDbEntity, string>(
            entity => entity.StorageKey,
            () => Guid.NewGuid().ToString("N"));

        // Act
        var act = async () => await new TestTwoModelHostBuilder<MappedApiEntity, MappedDbEntity, string>(
                repository,
                "/api/mapped-items")
            .WithOptions(options => options.EnableHateoas = true)
            .WithServices(services =>
                services.AddRestLibMapper<MappedApiEntity, MappedDbEntity>(_ => new CollectionMapper()))
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.IncludeOperations(RestLibOperation.GetAll);
            })
            .BuildAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*MappedApiEntity*")
            .WithMessage("*Id*")
            .WithMessage("*KeySelector*");
    }

    [Fact]
    public async Task GetAll_SelectorReturnsNull_PreservesFullAndProjectedCollectionMetadata()
    {
        // Arrange
        var repository = new InMemoryRepository<CollectionEntity, string>(
            entity => entity.StorageKey,
            () => Guid.NewGuid().ToString("N"));
        repository.Seed(
        [
            new CollectionEntity { StorageKey = "1", ResourceKey = "linked", Name = "Linked" },
            new CollectionEntity { StorageKey = "2", ResourceKey = null, Name = "Unlinked" }
        ]);

        var (host, client) = await new TestHostBuilder<CollectionEntity, string>(repository, "/api/items")
            .WithOptions(options => options.EnableHateoas = true)
            .WithServices(services =>
                services.AddSingleton<IQueryCountableRepository<CollectionEntity, string>>(repository))
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.IncludeOperations(RestLibOperation.GetAll);
                config.KeySelector = entity => entity.ResourceKey!;
                config.AllowFieldSelection(entity => entity.Name);
            })
            .BuildAsync();
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var fullResponse = await client.GetAsync("/api/items");
        var projectedResponse = await client.GetAsync("/api/items?fields=name");

        // Assert
        fullResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        projectedResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var fullJson = await fullResponse.Content.ReadFromJsonAsync<JsonElement>();
        var projectedJson = await projectedResponse.Content.ReadFromJsonAsync<JsonElement>();
        AssertPreservedCollection(fullJson, isProjected: false);
        AssertPreservedCollection(projectedJson, isProjected: true);
    }

    [Fact]
    public async Task GetAllMapped_SelectorReturnsNull_PreservesAllApiModelsWithoutDbFields()
    {
        // Arrange
        var repository = new InMemoryRepository<MappedDbEntity, string>(
            entity => entity.StorageKey,
            () => Guid.NewGuid().ToString("N"));
        repository.Seed(
        [
            new MappedDbEntity
            {
                StorageKey = "1",
                ResourceKey = "mapped-linked",
                Name = "Mapped Linked",
                InternalSecret = "secret-1"
            },
            new MappedDbEntity
            {
                StorageKey = "2",
                ResourceKey = null,
                Name = "Mapped Unlinked",
                InternalSecret = "secret-2"
            }
        ]);

        var (host, client) = await new TestTwoModelHostBuilder<MappedApiEntity, MappedDbEntity, string>(
                repository,
                "/api/mapped-items")
            .WithOptions(options => options.EnableHateoas = true)
            .WithServices(services =>
            {
                services.AddRestLibMapper<MappedApiEntity, MappedDbEntity>(_ => new CollectionMapper());
                services.AddSingleton<IQueryCountableRepository<MappedDbEntity, string>>(repository);
            })
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.IncludeOperations(RestLibOperation.GetAll);
                config.KeySelector = entity => entity.ResourceKey!;
            })
            .BuildAsync();
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.GetAsync("/api/mapped-items");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("total_count").GetInt64().Should().Be(2);
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(2);
        items[0].GetProperty("name").GetString().Should().Be("Mapped Linked");
        items[0].GetProperty("_links").GetProperty("self").GetProperty("href").GetString()
            .Should().EndWith("/api/mapped-items/mapped-linked");
        items[1].GetProperty("name").GetString().Should().Be("Mapped Unlinked");
        items[1].TryGetProperty("_links", out _).Should().BeFalse();
        items[0].TryGetProperty("internal_secret", out _).Should().BeFalse();
        items[1].TryGetProperty("internal_secret", out _).Should().BeFalse();
    }

    private static void AssertPreservedCollection(JsonElement json, bool isProjected)
    {
        json.GetProperty("total_count").GetInt64().Should().Be(2);
        json.TryGetProperty("self", out _).Should().BeTrue();
        json.TryGetProperty("first", out _).Should().BeTrue();

        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(2);
        items[0].GetProperty("name").GetString().Should().Be("Linked");
        items[0].GetProperty("_links").GetProperty("self").GetProperty("href").GetString()
            .Should().EndWith("/api/items/linked");
        items[1].GetProperty("name").GetString().Should().Be("Unlinked");
        items[1].TryGetProperty("_links", out _).Should().BeFalse();

        items[0].TryGetProperty("storage_key", out _).Should().Be(!isProjected);
        items[1].TryGetProperty("storage_key", out _).Should().Be(!isProjected);
    }

    private sealed class CollectionEntity
    {
        public required string StorageKey { get; init; }

        public string? ResourceKey { get; init; }

        public required string Name { get; init; }
    }

    private sealed class MappedApiEntity
    {
        public string? ResourceKey { get; init; }

        public required string Name { get; init; }
    }

    private sealed class MappedDbEntity
    {
        public required string StorageKey { get; init; }

        public string? ResourceKey { get; init; }

        public required string Name { get; init; }

        public required string InternalSecret { get; init; }
    }

    private sealed class CollectionMapper : IRestLibMapper<MappedApiEntity, MappedDbEntity>
    {
        public MappedApiEntity ToApi(MappedDbEntity dbModel)
        {
            return new MappedApiEntity
            {
                ResourceKey = dbModel.ResourceKey,
                Name = dbModel.Name
            };
        }

        public MappedDbEntity ToDb(MappedApiEntity apiModel)
        {
            return new MappedDbEntity
            {
                StorageKey = apiModel.ResourceKey ?? string.Empty,
                ResourceKey = apiModel.ResourceKey,
                Name = apiModel.Name,
                InternalSecret = "mapped"
            };
        }
    }
}
