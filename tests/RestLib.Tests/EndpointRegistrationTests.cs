using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Tests for Story 1.1: Minimal Endpoint Registration
/// Verifies that MapRestLib generates all 6 CRUD endpoints correctly.
/// </summary>
public class EndpointRegistrationTests : IDisposable
{
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly TestEntityRepository _repository;

    public EndpointRegistrationTests()
    {
        _repository = new TestEntityRepository();

        (_host, _client) = new TestHostBuilder<TestEntity, Guid>(_repository, "/api/test-entities")
            .WithEndpoint(config => config.AllowAnonymous())
            .Build();
    }

    #region GET /api/test-entities (GetAll)

    [Fact]
    public async Task GetAll_ReturnsOk_WithItemsWrapper()
    {
        // Arrange
        _repository.Seed(
            new TestEntity { Id = Guid.NewGuid(), Name = "Item1", Price = 10.00m },
            new TestEntity { Id = Guid.NewGuid(), Name = "Item2", Price = 20.00m }
        );

        // Act
        var response = await _client.GetAsync("/api/test-entities");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<GetAllResponse>();
        content.Should().NotBeNull();
        content!.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAll_ReturnsEmptyItems_WhenNoEntities()
    {
        // Act
        var response = await _client.GetAsync("/api/test-entities");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<GetAllResponse>();
        content!.Items.Should().BeEmpty();
    }

    #endregion

    #region GET /api/test-entities/{id} (GetById)

    [Fact]
    public async Task GetById_ReturnsOk_WhenEntityExists()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(new TestEntity { Id = id, Name = "Test", Price = 99.99m });

        // Act
        var response = await _client.GetAsync($"/api/test-entities/{id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var entity = await response.Content.ReadFromJsonAsync<TestEntity>();
        entity.Should().NotBeNull();
        entity!.Id.Should().Be(id);
        entity.Name.Should().Be("Test");
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenEntityDoesNotExist()
    {
        // Act
        var response = await _client.GetAsync($"/api/test-entities/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region POST /api/test-entities (Create)

    [Fact]
    public async Task Create_ReturnsCreated_WithEntity()
    {
        // Arrange
        var entity = new TestEntity { Name = "NewItem", Price = 50.00m };

        // Act
        var response = await _client.PostAsJsonAsync("/api/test-entities", entity);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();

        var created = await response.Content.ReadFromJsonAsync<TestEntity>();
        created.Should().NotBeNull();
        created!.Id.Should().NotBeEmpty();
        created.Name.Should().Be("NewItem");
    }

    [Fact]
    public async Task Create_AddsEntityToRepository()
    {
        // Arrange
        var entity = new TestEntity { Name = "NewItem", Price = 50.00m };
        var initialCount = _repository.Count;

        // Act
        await _client.PostAsJsonAsync("/api/test-entities", entity);

        // Assert
        _repository.Count.Should().Be(initialCount + 1);
    }

    #endregion

    #region PUT /api/test-entities/{id} (Update)

    [Fact]
    public async Task Update_ReturnsOk_WhenEntityExists()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(new TestEntity { Id = id, Name = "Original", Price = 10.00m });
        var updated = new TestEntity { Name = "Updated", Price = 99.99m };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/test-entities/{id}", updated);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<TestEntity>();
        result!.Name.Should().Be("Updated");
        result.Price.Should().Be(99.99m);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_WhenEntityDoesNotExist()
    {
        // Arrange
        var updated = new TestEntity { Name = "Updated", Price = 99.99m };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/test-entities/{Guid.NewGuid()}", updated);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region PATCH /api/test-entities/{id} (Patch)

    [Fact]
    public async Task Patch_ReturnsOk_WhenEntityExists()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(new TestEntity { Id = id, Name = "Original", Price = 10.00m });
        var patch = new { name = "Patched" };

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/test-entities/{id}", patch);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<TestEntity>();
        result!.Name.Should().Be("Patched");
        result.Price.Should().Be(10.00m); // Unchanged
    }

    [Fact]
    public async Task Patch_ReturnsNotFound_WhenEntityDoesNotExist()
    {
        // Arrange
        var patch = new { name = "Patched" };

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/test-entities/{Guid.NewGuid()}", patch);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region DELETE /api/test-entities/{id} (Delete)

    [Fact]
    public async Task Delete_ReturnsNoContent_WhenEntityExists()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(new TestEntity { Id = id, Name = "ToDelete", Price = 10.00m });

        // Act
        var response = await _client.DeleteAsync($"/api/test-entities/{id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_RemovesEntityFromRepository()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.Seed(new TestEntity { Id = id, Name = "ToDelete", Price = 10.00m });

        // Act
        await _client.DeleteAsync($"/api/test-entities/{id}");

        // Assert
        var entity = await _repository.GetByIdAsync(id);
        entity.Should().BeNull();
    }

    [Fact]
    public async Task Delete_ReturnsNotFound_WhenEntityDoesNotExist()
    {
        // Act
        var response = await _client.DeleteAsync($"/api/test-entities/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    public void Dispose()
    {
        _client.Dispose();
        _host.Dispose();
    }

    private record GetAllResponse(List<TestEntity> Items);
}
