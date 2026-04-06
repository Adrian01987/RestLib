using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Tests for operation selection (IncludeOperations / ExcludeOperations).
/// Verifies that only the configured endpoints are registered.
/// </summary>
public class OperationSelectionTests
{
    private static (IHost host, HttpClient client, TestEntityRepository repository) CreateTestHost(
        Action<RestLib.Configuration.RestLibEndpointConfiguration<TestEntity, Guid>> configure)
    {
        var repository = new TestEntityRepository();

        var (host, client) = new TestHostBuilder<TestEntity, Guid>(repository, "/api/items")
            .WithEndpoint(configure)
            .Build();

        return (host, client, repository);
    }

    #region Default behavior (all endpoints enabled)

    [Fact]
    public async Task Default_AllEndpointsAreAccessible()
    {
        // Arrange
        var (host, client, repository) = CreateTestHost(config => config.AllowAnonymous());
        using var _ = host;
        using var __ = client;

        var id = Guid.NewGuid();
        repository.Seed(new TestEntity { Id = id, Name = "Test", Price = 10m });

        // Act
        var getAll = await client.GetAsync("/api/items");
        var getById = await client.GetAsync($"/api/items/{id}");
        var create = await client.PostAsJsonAsync("/api/items", new TestEntity { Name = "New", Price = 5m });
        var update = await client.PutAsJsonAsync($"/api/items/{id}", new TestEntity { Name = "Upd", Price = 1m });
        var patch = await client.PatchAsJsonAsync($"/api/items/{id}", new { name = "Patched" });
        var delete = await client.DeleteAsync($"/api/items/{id}");

        // Assert
        getAll.StatusCode.Should().Be(HttpStatusCode.OK);
        getById.StatusCode.Should().Be(HttpStatusCode.OK);
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        patch.StatusCode.Should().Be(HttpStatusCode.OK);
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    #endregion

    #region IncludeOperations

    [Fact]
    public async Task IncludeOperations_ReadOnly_OnlyGetAllAndGetByIdAreAccessible()
    {
        // Arrange
        var (host, client, repository) = CreateTestHost(config =>
        {
            config.AllowAnonymous();
            config.IncludeOperations(RestLibOperation.GetAll, RestLibOperation.GetById);
        });
        using var _ = host;
        using var __ = client;

        var id = Guid.NewGuid();
        repository.Seed(new TestEntity { Id = id, Name = "Test", Price = 10m });

        // Act — Included operations should work
        var getAll = await client.GetAsync("/api/items");
        var getById = await client.GetAsync($"/api/items/{id}");

        // Assert
        getAll.StatusCode.Should().Be(HttpStatusCode.OK);
        getById.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act — Excluded operations should return 405 Method Not Allowed (no matching route)
        var create = await client.PostAsJsonAsync("/api/items", new TestEntity { Name = "New", Price = 5m });
        var update = await client.PutAsJsonAsync($"/api/items/{id}", new TestEntity { Name = "Upd", Price = 1m });
        var patch = await client.PatchAsJsonAsync($"/api/items/{id}", new { name = "Patched" });
        var delete = await client.DeleteAsync($"/api/items/{id}");

        // Assert — These endpoints are not registered, so the server returns 404 or 405
        create.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        update.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        patch.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        delete.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task IncludeOperations_SingleEndpoint_OnlyThatEndpointIsAccessible()
    {
        // Arrange
        var (host, client, repository) = CreateTestHost(config =>
        {
            config.AllowAnonymous();
            config.IncludeOperations(RestLibOperation.Create);
        });
        using var _ = host;
        using var __ = client;

        // Act — Create should work
        var create = await client.PostAsJsonAsync("/api/items", new TestEntity { Name = "New", Price = 5m });

        // Assert
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        // Act — GetAll should not be registered
        var getAll = await client.GetAsync("/api/items");

        // Assert
        getAll.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task IncludeOperations_AllOperations_BehavesLikeDefault()
    {
        // Arrange
        var (host, client, repository) = CreateTestHost(config =>
        {
            config.AllowAnonymous();
            config.IncludeOperations(
            RestLibOperation.GetAll,
            RestLibOperation.GetById,
            RestLibOperation.Create,
            RestLibOperation.Update,
            RestLibOperation.Patch,
            RestLibOperation.Delete);
        });
        using var _ = host;
        using var __ = client;

        var id = Guid.NewGuid();
        repository.Seed(new TestEntity { Id = id, Name = "Test", Price = 10m });

        // Act
        var getAll = await client.GetAsync("/api/items");
        var getById = await client.GetAsync($"/api/items/{id}");
        var create = await client.PostAsJsonAsync("/api/items", new TestEntity { Name = "New", Price = 5m });
        var update = await client.PutAsJsonAsync($"/api/items/{id}", new TestEntity { Name = "Upd", Price = 1m });
        var patch = await client.PatchAsJsonAsync($"/api/items/{id}", new { name = "Patched" });
        var delete = await client.DeleteAsync($"/api/items/{id}");

        // Assert
        getAll.StatusCode.Should().Be(HttpStatusCode.OK);
        getById.StatusCode.Should().Be(HttpStatusCode.OK);
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        patch.StatusCode.Should().Be(HttpStatusCode.OK);
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    #endregion

    #region ExcludeOperations

    [Fact]
    public async Task ExcludeOperations_DeleteExcluded_AllOthersWork()
    {
        // Arrange
        var (host, client, repository) = CreateTestHost(config =>
        {
            config.AllowAnonymous();
            config.ExcludeOperations(RestLibOperation.Delete);
        });
        using var _ = host;
        using var __ = client;

        var id = Guid.NewGuid();
        repository.Seed(new TestEntity { Id = id, Name = "Test", Price = 10m });

        // Act
        var getAll = await client.GetAsync("/api/items");
        var getById = await client.GetAsync($"/api/items/{id}");
        var create = await client.PostAsJsonAsync("/api/items", new TestEntity { Name = "New", Price = 5m });
        var update = await client.PutAsJsonAsync($"/api/items/{id}", new TestEntity { Name = "Upd", Price = 1m });
        var patch = await client.PatchAsJsonAsync($"/api/items/{id}", new { name = "Patched" });

        // Assert
        getAll.StatusCode.Should().Be(HttpStatusCode.OK);
        getById.StatusCode.Should().Be(HttpStatusCode.OK);
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        patch.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act — Delete should not be registered
        var delete = await client.DeleteAsync($"/api/items/{id}");

        // Assert
        delete.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task ExcludeOperations_MultipleExcluded_OnlyNonExcludedWork()
    {
        // Arrange
        var (host, client, repository) = CreateTestHost(config =>
        {
            config.AllowAnonymous();
            config.ExcludeOperations(RestLibOperation.Delete, RestLibOperation.Patch, RestLibOperation.Update);
        });
        using var _ = host;
        using var __ = client;

        var id = Guid.NewGuid();
        repository.Seed(new TestEntity { Id = id, Name = "Test", Price = 10m });

        // Act — Read + Create should work
        var getAll = await client.GetAsync("/api/items");
        var getById = await client.GetAsync($"/api/items/{id}");
        var create = await client.PostAsJsonAsync("/api/items", new TestEntity { Name = "New", Price = 5m });

        // Assert
        getAll.StatusCode.Should().Be(HttpStatusCode.OK);
        getById.StatusCode.Should().Be(HttpStatusCode.OK);
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        // Act — Excluded operations should not be registered
        var update = await client.PutAsJsonAsync($"/api/items/{id}", new TestEntity { Name = "Upd", Price = 1m });
        var patch = await client.PatchAsJsonAsync($"/api/items/{id}", new { name = "Patched" });
        var delete = await client.DeleteAsync($"/api/items/{id}");

        // Assert
        update.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        patch.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        delete.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    }

    #endregion

    #region Mutual exclusion validation

    [Fact]
    public void IncludeOperations_ThenExclude_ThrowsInvalidOperationException()
    {
        // Arrange
        var config = new RestLib.Configuration.RestLibEndpointConfiguration<TestEntity, Guid>();
        config.IncludeOperations(RestLibOperation.GetAll);

        // Act
        var act = () => config.ExcludeOperations(RestLibOperation.Delete);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot use ExcludeOperations*IncludeOperations*");
    }

    [Fact]
    public void ExcludeOperations_ThenInclude_ThrowsInvalidOperationException()
    {
        // Arrange
        var config = new RestLib.Configuration.RestLibEndpointConfiguration<TestEntity, Guid>();
        config.ExcludeOperations(RestLibOperation.Delete);

        // Act
        var act = () => config.IncludeOperations(RestLibOperation.GetAll);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot use IncludeOperations*ExcludeOperations*");
    }

    #endregion

    #region IsOperationEnabled unit tests

    [Fact]
    public void IsOperationEnabled_NoConfiguration_AllEnabled()
    {
        // Arrange
        var config = new RestLib.Configuration.RestLibEndpointConfiguration<TestEntity, Guid>();

        // Act & Assert
        foreach (var op in Enum.GetValues<RestLibOperation>())
        {
            config.IsOperationEnabled(op).Should().BeTrue(
                because: $"{op} should be enabled by default");
        }
    }

    [Fact]
    public void IsOperationEnabled_IncludeOnly_ReturnsTrueOnlyForIncluded()
    {
        // Arrange
        var config = new RestLib.Configuration.RestLibEndpointConfiguration<TestEntity, Guid>();
        config.IncludeOperations(RestLibOperation.GetAll, RestLibOperation.Create);

        // Act & Assert
        config.IsOperationEnabled(RestLibOperation.GetAll).Should().BeTrue();
        config.IsOperationEnabled(RestLibOperation.Create).Should().BeTrue();
        config.IsOperationEnabled(RestLibOperation.GetById).Should().BeFalse();
        config.IsOperationEnabled(RestLibOperation.Update).Should().BeFalse();
        config.IsOperationEnabled(RestLibOperation.Patch).Should().BeFalse();
        config.IsOperationEnabled(RestLibOperation.Delete).Should().BeFalse();
    }

    [Fact]
    public void IsOperationEnabled_ExcludeOnly_ReturnsFalseOnlyForExcluded()
    {
        // Arrange
        var config = new RestLib.Configuration.RestLibEndpointConfiguration<TestEntity, Guid>();
        config.ExcludeOperations(RestLibOperation.Delete, RestLibOperation.Patch);

        // Act & Assert
        config.IsOperationEnabled(RestLibOperation.GetAll).Should().BeTrue();
        config.IsOperationEnabled(RestLibOperation.GetById).Should().BeTrue();
        config.IsOperationEnabled(RestLibOperation.Create).Should().BeTrue();
        config.IsOperationEnabled(RestLibOperation.Update).Should().BeTrue();
        config.IsOperationEnabled(RestLibOperation.Patch).Should().BeFalse();
        config.IsOperationEnabled(RestLibOperation.Delete).Should().BeFalse();
    }

    #endregion

    #region Chaining

    [Fact]
    public void IncludeOperations_ReturnsConfig_ForChaining()
    {
        // Arrange
        var config = new RestLib.Configuration.RestLibEndpointConfiguration<TestEntity, Guid>();

        // Act
        var result = config.IncludeOperations(RestLibOperation.GetAll);

        // Assert
        result.Should().BeSameAs(config);
    }

    [Fact]
    public void ExcludeOperations_ReturnsConfig_ForChaining()
    {
        // Arrange
        var config = new RestLib.Configuration.RestLibEndpointConfiguration<TestEntity, Guid>();

        // Act
        var result = config.ExcludeOperations(RestLibOperation.Delete);

        // Assert
        result.Should().BeSameAs(config);
    }

    #endregion

    #region Edge cases

    [Fact]
    public void IncludeOperations_Empty_DisablesAllOperations()
    {
        // Arrange
        var config = new RestLib.Configuration.RestLibEndpointConfiguration<TestEntity, Guid>();

        // Act
        config.IncludeOperations(); // empty params = nothing included

        // Assert
        foreach (var op in Enum.GetValues<RestLibOperation>())
        {
            config.IsOperationEnabled(op).Should().BeFalse(
                because: $"{op} should be disabled when IncludeOperations is called with no arguments");
        }
    }

    [Fact]
    public void ExcludeOperations_Empty_EnablesAllOperations()
    {
        // Arrange
        var config = new RestLib.Configuration.RestLibEndpointConfiguration<TestEntity, Guid>();

        // Act
        config.ExcludeOperations(); // empty params = nothing excluded

        // Assert
        foreach (var op in Enum.GetValues<RestLibOperation>())
        {
            config.IsOperationEnabled(op).Should().BeTrue(
                because: $"{op} should remain enabled when ExcludeOperations is called with no arguments");
        }
    }

    #endregion

    #region Multiple IncludeOperations calls (merge)

    [Fact]
    public void IncludeOperations_CalledMultipleTimes_MergesOperations()
    {
        // Arrange
        var config = new RestLib.Configuration.RestLibEndpointConfiguration<TestEntity, Guid>();

        // Act
        config.IncludeOperations(RestLibOperation.GetAll);
        config.IncludeOperations(RestLibOperation.GetById);
        config.IncludeOperations(RestLibOperation.Create);

        // Assert
        config.IsOperationEnabled(RestLibOperation.GetAll).Should().BeTrue();
        config.IsOperationEnabled(RestLibOperation.GetById).Should().BeTrue();
        config.IsOperationEnabled(RestLibOperation.Create).Should().BeTrue();
        config.IsOperationEnabled(RestLibOperation.Update).Should().BeFalse();
        config.IsOperationEnabled(RestLibOperation.Patch).Should().BeFalse();
        config.IsOperationEnabled(RestLibOperation.Delete).Should().BeFalse();
    }

    [Fact]
    public void IncludeOperations_DuplicateOperations_HandledGracefully()
    {
        // Arrange
        var config = new RestLib.Configuration.RestLibEndpointConfiguration<TestEntity, Guid>();

        // Act
        config.IncludeOperations(RestLibOperation.GetAll, RestLibOperation.GetById);
        config.IncludeOperations(RestLibOperation.GetAll, RestLibOperation.Create); // GetAll is a duplicate

        // Assert
        config.IsOperationEnabled(RestLibOperation.GetAll).Should().BeTrue();
        config.IsOperationEnabled(RestLibOperation.GetById).Should().BeTrue();
        config.IsOperationEnabled(RestLibOperation.Create).Should().BeTrue();
        config.IsOperationEnabled(RestLibOperation.Update).Should().BeFalse();
    }

    [Fact]
    public async Task IncludeOperations_MergedAcrossMultipleCalls_EndpointsWork()
    {
        // Arrange
        var (host, client, repository) = CreateTestHost(config =>
        {
            config.AllowAnonymous();
            config.IncludeOperations(RestLibOperation.GetAll);
            config.IncludeOperations(RestLibOperation.Create);
        });
        using var _ = host;
        using var __ = client;

        // Act — Both merged operations should work
        var getAll = await client.GetAsync("/api/items");
        var create = await client.PostAsJsonAsync("/api/items", new TestEntity { Name = "New", Price = 5m });

        // Assert
        getAll.StatusCode.Should().Be(HttpStatusCode.OK);
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        // Act — Non-included operations should not be registered
        var id = Guid.NewGuid();
        var getById = await client.GetAsync($"/api/items/{id}");
        var delete = await client.DeleteAsync($"/api/items/{id}");

        // Assert
        getById.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        delete.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    }

    #endregion

    #region Custom endpoint replacement

    [Fact]
    public async Task ExcludeCreate_WithCustomCreateEndpoint_CustomEndpointWorks()
    {
        // Arrange
        var repository = new TestEntityRepository();

        var (host, client) = new TestHostBuilder<TestEntity, Guid>(repository, "/api/items")
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.ExcludeOperations(RestLibOperation.Create);
            })
            .WithMiddleware(app =>
            {
                // Register a custom Create endpoint
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapPost("/api/items", (TestEntity entity) =>
                    {
                        entity.Id = Guid.NewGuid();
                        entity.Name = $"CUSTOM: {entity.Name}";
                        repository.CreateAsync(entity);
                        return Results.Created($"/api/items/{entity.Id}", entity);
                    }).AllowAnonymous();
                });
            })
            .Build();

        using var _ = host;
        using var __ = client;

        // Act — Custom Create endpoint should work with custom logic
        var create = await client.PostAsJsonAsync("/api/items", new TestEntity { Name = "Product", Price = 10m });

        // Assert
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await create.Content.ReadFromJsonAsync<TestEntity>();
        created!.Name.Should().StartWith("CUSTOM:");

        // Act — Standard GetAll should still work
        var getAll = await client.GetAsync("/api/items");

        // Assert
        getAll.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion
}
