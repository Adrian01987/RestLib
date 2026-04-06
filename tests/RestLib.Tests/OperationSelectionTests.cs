using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestLib.Abstractions;
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

        var host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRestLib();
                        services.AddSingleton<IRepository<TestEntity, Guid>>(repository);
                        services.AddRouting();
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapRestLib<TestEntity, Guid>("/api/items", configure);
                    });
                    });
            })
            .Build();

        host.Start();
        return (host, host.GetTestClient(), repository);
    }

    #region Default behavior (all endpoints enabled)

    [Fact]
    public async Task Default_AllEndpointsAreAccessible()
    {
        var (host, client, repository) = CreateTestHost(config => config.AllowAnonymous());
        using var _ = host;
        using var __ = client;

        var id = Guid.NewGuid();
        repository.Seed(new TestEntity { Id = id, Name = "Test", Price = 10m });

        var getAll = await client.GetAsync("/api/items");
        var getById = await client.GetAsync($"/api/items/{id}");
        var create = await client.PostAsJsonAsync("/api/items", new TestEntity { Name = "New", Price = 5m });
        var update = await client.PutAsJsonAsync($"/api/items/{id}", new TestEntity { Name = "Upd", Price = 1m });
        var patch = await client.PatchAsJsonAsync($"/api/items/{id}", new { name = "Patched" });
        var delete = await client.DeleteAsync($"/api/items/{id}");

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
        var (host, client, repository) = CreateTestHost(config =>
        {
            config.AllowAnonymous();
            config.IncludeOperations(RestLibOperation.GetAll, RestLibOperation.GetById);
        });
        using var _ = host;
        using var __ = client;

        var id = Guid.NewGuid();
        repository.Seed(new TestEntity { Id = id, Name = "Test", Price = 10m });

        // Included operations should work
        var getAll = await client.GetAsync("/api/items");
        var getById = await client.GetAsync($"/api/items/{id}");
        getAll.StatusCode.Should().Be(HttpStatusCode.OK);
        getById.StatusCode.Should().Be(HttpStatusCode.OK);

        // Excluded operations should return 405 Method Not Allowed (no matching route)
        var create = await client.PostAsJsonAsync("/api/items", new TestEntity { Name = "New", Price = 5m });
        var update = await client.PutAsJsonAsync($"/api/items/{id}", new TestEntity { Name = "Upd", Price = 1m });
        var patch = await client.PatchAsJsonAsync($"/api/items/{id}", new { name = "Patched" });
        var delete = await client.DeleteAsync($"/api/items/{id}");

        // These endpoints are not registered, so the server returns 404 or 405
        create.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        update.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        patch.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        delete.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task IncludeOperations_SingleEndpoint_OnlyThatEndpointIsAccessible()
    {
        var (host, client, repository) = CreateTestHost(config =>
        {
            config.AllowAnonymous();
            config.IncludeOperations(RestLibOperation.Create);
        });
        using var _ = host;
        using var __ = client;

        // Create should work
        var create = await client.PostAsJsonAsync("/api/items", new TestEntity { Name = "New", Price = 5m });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        // GetAll should not be registered
        var getAll = await client.GetAsync("/api/items");
        getAll.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task IncludeOperations_AllOperations_BehavesLikeDefault()
    {
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

        var getAll = await client.GetAsync("/api/items");
        var getById = await client.GetAsync($"/api/items/{id}");
        var create = await client.PostAsJsonAsync("/api/items", new TestEntity { Name = "New", Price = 5m });
        var update = await client.PutAsJsonAsync($"/api/items/{id}", new TestEntity { Name = "Upd", Price = 1m });
        var patch = await client.PatchAsJsonAsync($"/api/items/{id}", new { name = "Patched" });
        var delete = await client.DeleteAsync($"/api/items/{id}");

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
        var (host, client, repository) = CreateTestHost(config =>
        {
            config.AllowAnonymous();
            config.ExcludeOperations(RestLibOperation.Delete);
        });
        using var _ = host;
        using var __ = client;

        var id = Guid.NewGuid();
        repository.Seed(new TestEntity { Id = id, Name = "Test", Price = 10m });

        var getAll = await client.GetAsync("/api/items");
        var getById = await client.GetAsync($"/api/items/{id}");
        var create = await client.PostAsJsonAsync("/api/items", new TestEntity { Name = "New", Price = 5m });
        var update = await client.PutAsJsonAsync($"/api/items/{id}", new TestEntity { Name = "Upd", Price = 1m });
        var patch = await client.PatchAsJsonAsync($"/api/items/{id}", new { name = "Patched" });

        getAll.StatusCode.Should().Be(HttpStatusCode.OK);
        getById.StatusCode.Should().Be(HttpStatusCode.OK);
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        patch.StatusCode.Should().Be(HttpStatusCode.OK);

        // Delete should not be registered
        var delete = await client.DeleteAsync($"/api/items/{id}");
        delete.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task ExcludeOperations_MultipleExcluded_OnlyNonExcludedWork()
    {
        var (host, client, repository) = CreateTestHost(config =>
        {
            config.AllowAnonymous();
            config.ExcludeOperations(RestLibOperation.Delete, RestLibOperation.Patch, RestLibOperation.Update);
        });
        using var _ = host;
        using var __ = client;

        var id = Guid.NewGuid();
        repository.Seed(new TestEntity { Id = id, Name = "Test", Price = 10m });

        // Read + Create should work
        var getAll = await client.GetAsync("/api/items");
        var getById = await client.GetAsync($"/api/items/{id}");
        var create = await client.PostAsJsonAsync("/api/items", new TestEntity { Name = "New", Price = 5m });
        getAll.StatusCode.Should().Be(HttpStatusCode.OK);
        getById.StatusCode.Should().Be(HttpStatusCode.OK);
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        // Excluded operations should not be registered
        var update = await client.PutAsJsonAsync($"/api/items/{id}", new TestEntity { Name = "Upd", Price = 1m });
        var patch = await client.PatchAsJsonAsync($"/api/items/{id}", new { name = "Patched" });
        var delete = await client.DeleteAsync($"/api/items/{id}");

        update.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        patch.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        delete.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    }

    #endregion

    #region Mutual exclusion validation

    [Fact]
    public void IncludeOperations_ThenExclude_ThrowsInvalidOperationException()
    {
        var config = new RestLib.Configuration.RestLibEndpointConfiguration<TestEntity, Guid>();
        config.IncludeOperations(RestLibOperation.GetAll);

        var act = () => config.ExcludeOperations(RestLibOperation.Delete);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot use ExcludeOperations*IncludeOperations*");
    }

    [Fact]
    public void ExcludeOperations_ThenInclude_ThrowsInvalidOperationException()
    {
        var config = new RestLib.Configuration.RestLibEndpointConfiguration<TestEntity, Guid>();
        config.ExcludeOperations(RestLibOperation.Delete);

        var act = () => config.IncludeOperations(RestLibOperation.GetAll);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot use IncludeOperations*ExcludeOperations*");
    }

    #endregion

    #region IsOperationEnabled unit tests

    [Fact]
    public void IsOperationEnabled_NoConfiguration_AllEnabled()
    {
        var config = new RestLib.Configuration.RestLibEndpointConfiguration<TestEntity, Guid>();

        foreach (var op in Enum.GetValues<RestLibOperation>())
        {
            config.IsOperationEnabled(op).Should().BeTrue(
                because: $"{op} should be enabled by default");
        }
    }

    [Fact]
    public void IsOperationEnabled_IncludeOnly_ReturnsTrueOnlyForIncluded()
    {
        var config = new RestLib.Configuration.RestLibEndpointConfiguration<TestEntity, Guid>();
        config.IncludeOperations(RestLibOperation.GetAll, RestLibOperation.Create);

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
        var config = new RestLib.Configuration.RestLibEndpointConfiguration<TestEntity, Guid>();
        config.ExcludeOperations(RestLibOperation.Delete, RestLibOperation.Patch);

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
        var config = new RestLib.Configuration.RestLibEndpointConfiguration<TestEntity, Guid>();

        var result = config.IncludeOperations(RestLibOperation.GetAll);

        result.Should().BeSameAs(config);
    }

    [Fact]
    public void ExcludeOperations_ReturnsConfig_ForChaining()
    {
        var config = new RestLib.Configuration.RestLibEndpointConfiguration<TestEntity, Guid>();

        var result = config.ExcludeOperations(RestLibOperation.Delete);

        result.Should().BeSameAs(config);
    }

    #endregion

    #region Edge cases

    [Fact]
    public void IncludeOperations_Empty_DisablesAllOperations()
    {
        var config = new RestLib.Configuration.RestLibEndpointConfiguration<TestEntity, Guid>();
        config.IncludeOperations(); // empty params = nothing included

        foreach (var op in Enum.GetValues<RestLibOperation>())
        {
            config.IsOperationEnabled(op).Should().BeFalse(
                because: $"{op} should be disabled when IncludeOperations is called with no arguments");
        }
    }

    [Fact]
    public void ExcludeOperations_Empty_EnablesAllOperations()
    {
        var config = new RestLib.Configuration.RestLibEndpointConfiguration<TestEntity, Guid>();
        config.ExcludeOperations(); // empty params = nothing excluded

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
        var config = new RestLib.Configuration.RestLibEndpointConfiguration<TestEntity, Guid>();
        config.IncludeOperations(RestLibOperation.GetAll);
        config.IncludeOperations(RestLibOperation.GetById);
        config.IncludeOperations(RestLibOperation.Create);

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
        var config = new RestLib.Configuration.RestLibEndpointConfiguration<TestEntity, Guid>();
        config.IncludeOperations(RestLibOperation.GetAll, RestLibOperation.GetById);
        config.IncludeOperations(RestLibOperation.GetAll, RestLibOperation.Create); // GetAll is a duplicate

        config.IsOperationEnabled(RestLibOperation.GetAll).Should().BeTrue();
        config.IsOperationEnabled(RestLibOperation.GetById).Should().BeTrue();
        config.IsOperationEnabled(RestLibOperation.Create).Should().BeTrue();
        config.IsOperationEnabled(RestLibOperation.Update).Should().BeFalse();
    }

    [Fact]
    public async Task IncludeOperations_MergedAcrossMultipleCalls_EndpointsWork()
    {
        var (host, client, repository) = CreateTestHost(config =>
        {
            config.AllowAnonymous();
            config.IncludeOperations(RestLibOperation.GetAll);
            config.IncludeOperations(RestLibOperation.Create);
        });
        using var _ = host;
        using var __ = client;

        // Both merged operations should work
        var getAll = await client.GetAsync("/api/items");
        var create = await client.PostAsJsonAsync("/api/items", new TestEntity { Name = "New", Price = 5m });
        getAll.StatusCode.Should().Be(HttpStatusCode.OK);
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        // Non-included operations should not be registered
        var id = Guid.NewGuid();
        var getById = await client.GetAsync($"/api/items/{id}");
        var delete = await client.DeleteAsync($"/api/items/{id}");
        getById.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        delete.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    }

    #endregion

    #region Custom endpoint replacement

    [Fact]
    public async Task ExcludeCreate_WithCustomCreateEndpoint_CustomEndpointWorks()
    {
        var repository = new TestEntityRepository();

        var host = new HostBuilder()
          .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRestLib();
                        services.AddSingleton<IRepository<TestEntity, Guid>>(repository);
                        services.AddRouting();
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                    {
                        // Register RestLib without Create
                        endpoints.MapRestLib<TestEntity, Guid>("/api/items", config =>
                      {
                          config.AllowAnonymous();
                          config.ExcludeOperations(RestLibOperation.Create);
                      });

                        // Register a custom Create endpoint
                        endpoints.MapPost("/api/items", (TestEntity entity) =>
                      {
                          entity.Id = Guid.NewGuid();
                          entity.Name = $"CUSTOM: {entity.Name}";
                          repository.CreateAsync(entity);
                          return Results.Created($"/api/items/{entity.Id}", entity);
                      }).AllowAnonymous();
                    });
                    });
            })
            .Build();

        using var _ = host;
        host.Start();
        using var client = host.GetTestClient();

        // Custom Create endpoint should work with custom logic
        var create = await client.PostAsJsonAsync("/api/items", new TestEntity { Name = "Product", Price = 10m });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await create.Content.ReadFromJsonAsync<TestEntity>();
        created!.Name.Should().StartWith("CUSTOM:");

        // Standard GetAll should still work
        var getAll = await client.GetAsync("/api/items");
        getAll.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion
}
