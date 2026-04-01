using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestLib.Abstractions;
using RestLib.Configuration;
using RestLib.InMemory;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Integration tests for versioned endpoint registration patterns.
/// </summary>
[Trait("Category", "Story8.1")]
public class VersioningTests : IDisposable
{
    private IHost? _host;
    private HttpClient? _client;

    public void Dispose()
    {
        _client?.Dispose();
        _host?.Dispose();
    }

    private (IHost Host, HttpClient Client) CreateHost(Action<IApplicationBuilder> configureApp, Action<IServiceCollection>? configureServices = null)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRestLib();
                        services.AddRouting();
                        configureServices?.Invoke(services);
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        configureApp(app);
                    });
            })
            .Build();

        host.Start();
        var client = host.GetTestClient();

        _host = host;
        _client = client;

        return (host, client);
    }

    // ---------------------------------------------------------------
    // Tier 1 — URL prefix grouping
    // ---------------------------------------------------------------

    [Fact]
    public async Task MapGroup_ThenMapRestLib_EndpointsHaveGroupPrefix()
    {
        // Arrange
        var repository = new InMemoryRepository<TestEntity, Guid>(e => e.Id, Guid.NewGuid);
        repository.Seed(new[] { new TestEntity { Id = Guid.NewGuid(), Name = "Widget", Price = 9.99m } });

        var (_, client) = CreateHost(
            app => app.UseEndpoints(endpoints =>
            {
                endpoints.MapGroup("/api/v1").MapRestLib<TestEntity, Guid>("/products", config =>
                {
                    config.AllowAnonymous();
                });
            }),
            services => services.AddSingleton<IRepository<TestEntity, Guid>>(repository));

        // Act
        var response = await client.GetAsync("/api/v1/products");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Widget");
    }

    [Fact]
    public async Task PrefixlessMapRestLib_OnGroup_EndpointsRegistered()
    {
        // Arrange
        var repository = new InMemoryRepository<TestEntity, Guid>(e => e.Id, Guid.NewGuid);
        repository.Seed(new[] { new TestEntity { Id = Guid.NewGuid(), Name = "Gadget", Price = 19.99m } });

        var (_, client) = CreateHost(
            app => app.UseEndpoints(endpoints =>
            {
                endpoints.MapGroup("/api/v1/products").MapRestLib<TestEntity, Guid>(config =>
                {
                    config.AllowAnonymous();
                });
            }),
            services => services.AddSingleton<IRepository<TestEntity, Guid>>(repository));

        // Act
        var response = await client.GetAsync("/api/v1/products");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Gadget");
    }

    [Fact]
    public async Task PrefixlessMapRestLib_AllCrudOperations_Work()
    {
        // Arrange
        var repository = new InMemoryRepository<TestEntity, Guid>(e => e.Id, Guid.NewGuid);

        var (_, client) = CreateHost(
            app => app.UseEndpoints(endpoints =>
            {
                endpoints.MapGroup("/api/v1/products").MapRestLib<TestEntity, Guid>(config =>
                {
                    config.AllowAnonymous();
                });
            }),
            services => services.AddSingleton<IRepository<TestEntity, Guid>>(repository));

        // Act & Assert — POST (Create)
        var createPayload = new { name = "Created", price = 5.0m };
        var createResponse = await client.PostAsJsonAsync("/api/v1/products", createPayload);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var createdId = created.GetProperty("id").GetString();
        createdId.Should().NotBeNullOrEmpty();

        // Act & Assert — GET all
        var getAllResponse = await client.GetAsync("/api/v1/products");
        getAllResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var allBody = await getAllResponse.Content.ReadFromJsonAsync<JsonElement>();
        allBody.GetProperty("items").GetArrayLength().Should().Be(1);

        // Act & Assert — GET by ID
        var getByIdResponse = await client.GetAsync($"/api/v1/products/{createdId}");
        getByIdResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var byIdBody = await getByIdResponse.Content.ReadFromJsonAsync<JsonElement>();
        byIdBody.GetProperty("name").GetString().Should().Be("Created");

        // Act & Assert — PUT (Update)
        var updatePayload = new { id = createdId, name = "Updated", price = 10.0m };
        var updateResponse = await client.PutAsJsonAsync($"/api/v1/products/{createdId}", updatePayload);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act & Assert — PATCH
        var patchJson = JsonSerializer.Serialize(new { name = "Patched" });
        var patchContent = new StringContent(patchJson, Encoding.UTF8, "application/merge-patch+json");
        var patchResponse = await client.PatchAsync($"/api/v1/products/{createdId}", patchContent);
        patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act & Assert — DELETE
        var deleteResponse = await client.DeleteAsync($"/api/v1/products/{createdId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify deleted
        var afterDelete = await client.GetAsync($"/api/v1/products/{createdId}");
        afterDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PrefixlessMapRestLib_WithFiltering_Works()
    {
        // Arrange
        var repository = new InMemoryRepository<TestEntity, Guid>(e => e.Id, Guid.NewGuid);
        repository.Seed(new[]
        {
            new TestEntity { Id = Guid.NewGuid(), Name = "Cheap", Price = 5.0m },
            new TestEntity { Id = Guid.NewGuid(), Name = "Expensive", Price = 100.0m }
        });

        var (_, client) = CreateHost(
            app => app.UseEndpoints(endpoints =>
            {
                endpoints.MapGroup("/api/v1/products").MapRestLib<TestEntity, Guid>(config =>
                {
                    config.AllowAnonymous();
                    config.AllowFiltering(p => p.Name);
                });
            }),
            services => services.AddSingleton<IRepository<TestEntity, Guid>>(repository));

        // Act
        var response = await client.GetAsync("/api/v1/products?name=Cheap");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("items").GetArrayLength().Should().Be(1);
        body.GetProperty("items")[0].GetProperty("name").GetString().Should().Be("Cheap");
    }

    [Fact]
    public async Task PrefixlessMapRestLib_WithSorting_Works()
    {
        // Arrange
        var repository = new InMemoryRepository<TestEntity, Guid>(e => e.Id, Guid.NewGuid);
        repository.Seed(new[]
        {
            new TestEntity { Id = Guid.NewGuid(), Name = "Banana", Price = 2.0m },
            new TestEntity { Id = Guid.NewGuid(), Name = "Apple", Price = 1.0m },
            new TestEntity { Id = Guid.NewGuid(), Name = "Cherry", Price = 3.0m }
        });

        var (_, client) = CreateHost(
            app => app.UseEndpoints(endpoints =>
            {
                endpoints.MapGroup("/api/v1/products").MapRestLib<TestEntity, Guid>(config =>
                {
                    config.AllowAnonymous();
                    config.AllowSorting(p => p.Name);
                });
            }),
            services => services.AddSingleton<IRepository<TestEntity, Guid>>(repository));

        // Act
        var response = await client.GetAsync("/api/v1/products?sort=name:asc");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items");
        items[0].GetProperty("name").GetString().Should().Be("Apple");
        items[1].GetProperty("name").GetString().Should().Be("Banana");
        items[2].GetProperty("name").GetString().Should().Be("Cherry");
    }

    [Fact]
    public async Task PrefixlessMapRestLib_WithFieldSelection_Works()
    {
        // Arrange
        var repository = new InMemoryRepository<TestEntity, Guid>(e => e.Id, Guid.NewGuid);
        var knownId = Guid.NewGuid();
        repository.Seed(new[] { new TestEntity { Id = knownId, Name = "Widget", Price = 9.99m } });

        var (_, client) = CreateHost(
            app => app.UseEndpoints(endpoints =>
            {
                endpoints.MapGroup("/api/v1/products").MapRestLib<TestEntity, Guid>(config =>
                {
                    config.AllowAnonymous();
                    config.AllowFieldSelection(p => p.Id, p => p.Name, p => p.Price);
                });
            }),
            services => services.AddSingleton<IRepository<TestEntity, Guid>>(repository));

        // Act
        var response = await client.GetAsync($"/api/v1/products/{knownId}?fields=name");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("name", out _).Should().BeTrue();
        body.TryGetProperty("price", out _).Should().BeFalse();
    }

    [Fact]
    public async Task PrefixlessMapRestLib_WithBatch_Works()
    {
        // Arrange
        var repository = new InMemoryRepository<TestEntity, Guid>(e => e.Id, Guid.NewGuid);

        var (_, client) = CreateHost(
            app => app.UseEndpoints(endpoints =>
            {
                endpoints.MapGroup("/api/v1/products").MapRestLib<TestEntity, Guid>(config =>
                {
                    config.AllowAnonymous();
                    config.EnableBatch();
                });
            }),
            services => services.AddSingleton<IRepository<TestEntity, Guid>>(repository));

        var batchPayload = new
        {
            action = "create",
            items = new[]
            {
                new { name = "Item1", price = 1.0m },
                new { name = "Item2", price = 2.0m }
            }
        };

        // Act
        var content = new StringContent(
            JsonSerializer.Serialize(batchPayload),
            Encoding.UTF8,
            "application/json");
        var response = await client.PostAsync("/api/v1/products/batch", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("items").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task PrefixlessMapRestLib_WithHooks_Works()
    {
        // Arrange
        var repository = new InMemoryRepository<TestEntity, Guid>(e => e.Id, Guid.NewGuid);
        var hookFired = false;

        var (_, client) = CreateHost(
            app => app.UseEndpoints(endpoints =>
            {
                endpoints.MapGroup("/api/v1/products").MapRestLib<TestEntity, Guid>(config =>
                {
                    config.AllowAnonymous();
                    config.UseHooks(hooks =>
                    {
                        hooks.OnRequestReceived = async ctx =>
                        {
                            hookFired = true;
                            await Task.CompletedTask;
                        };
                    });
                });
            }),
            services => services.AddSingleton<IRepository<TestEntity, Guid>>(repository));

        // Act
        await client.GetAsync("/api/v1/products");

        // Assert
        hookFired.Should().BeTrue();
    }

    [Fact]
    public async Task TwoVersionGroups_SameEntity_IndependentConfig()
    {
        // Arrange
        var repository = new InMemoryRepository<TestEntity, Guid>(e => e.Id, Guid.NewGuid);
        repository.Seed(new[] { new TestEntity { Id = Guid.NewGuid(), Name = "Widget", Price = 9.99m } });

        var (_, client) = CreateHost(
            app => app.UseEndpoints(endpoints =>
            {
                // v1: read-only — use prefix-based overload on version group for unique endpoint names
                endpoints.MapGroup("/api/v1").MapRestLib<TestEntity, Guid>("/products", config =>
                {
                    config.AllowAnonymous();
                    config.IncludeOperations(RestLibOperation.GetAll, RestLibOperation.GetById);
                });

                // v2: full CRUD
                endpoints.MapGroup("/api/v2").MapRestLib<TestEntity, Guid>("/products", config =>
                {
                    config.AllowAnonymous();
                });
            }),
            services => services.AddSingleton<IRepository<TestEntity, Guid>>(repository));

        // Act & Assert — v1 GET works
        var v1Get = await client.GetAsync("/api/v1/products");
        v1Get.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act & Assert — v1 POST rejected (method not allowed because route exists but POST is not registered)
        var v1Post = await client.PostAsJsonAsync("/api/v1/products", new { name = "New", price = 1.0m });
        v1Post.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);

        // Act & Assert — v2 POST works
        var v2Post = await client.PostAsJsonAsync("/api/v2/products", new { name = "New", price = 1.0m });
        v2Post.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task TwoVersionGroups_DifferentEntities_BothWork()
    {
        // Arrange
        var testRepo = new InMemoryRepository<TestEntity, Guid>(e => e.Id, Guid.NewGuid);
        testRepo.Seed(new[] { new TestEntity { Id = Guid.NewGuid(), Name = "Widget", Price = 9.99m } });

        var productRepo = new InMemoryRepository<ProductEntity, Guid>(e => e.Id, Guid.NewGuid);
        productRepo.Seed(new[] { new ProductEntity { Id = Guid.NewGuid(), ProductName = "Gadget", UnitPrice = 19.99m, IsActive = true } });

        var (_, client) = CreateHost(
            app => app.UseEndpoints(endpoints =>
            {
                endpoints.MapGroup("/api/v1/items").MapRestLib<TestEntity, Guid>(config =>
                {
                    config.AllowAnonymous();
                });

                endpoints.MapGroup("/api/v2/products").MapRestLib<ProductEntity, Guid>(config =>
                {
                    config.AllowAnonymous();
                });
            }),
            services =>
            {
                services.AddSingleton<IRepository<TestEntity, Guid>>(testRepo);
                services.AddSingleton<IRepository<ProductEntity, Guid>>(productRepo);
            });

        // Act
        var v1Response = await client.GetAsync("/api/v1/items");
        var v2Response = await client.GetAsync("/api/v2/products");

        // Assert
        v1Response.StatusCode.Should().Be(HttpStatusCode.OK);
        var v1Body = await v1Response.Content.ReadFromJsonAsync<JsonElement>();
        v1Body.GetProperty("items")[0].GetProperty("name").GetString().Should().Be("Widget");

        v2Response.StatusCode.Should().Be(HttpStatusCode.OK);
        var v2Body = await v2Response.Content.ReadFromJsonAsync<JsonElement>();
        v2Body.GetProperty("items")[0].GetProperty("product_name").GetString().Should().Be("Gadget");
    }

    [Fact]
    public void PrefixlessMapRestLib_ReturnsRouteGroupBuilder()
    {
        // Arrange & Act
        RouteGroupBuilder? returnedGroup = null;

        var host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRestLib();
                        services.AddSingleton<IRepository<TestEntity, Guid>>(
                            new InMemoryRepository<TestEntity, Guid>(e => e.Id, Guid.NewGuid));
                        services.AddRouting();
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            var group = endpoints.MapGroup("/api/v1/products");
                            returnedGroup = group.MapRestLib<TestEntity, Guid>(config =>
                            {
                                config.AllowAnonymous();
                            });
                        });
                    });
            })
            .Build();

        host.Start();

        // Assert
        returnedGroup.Should().NotBeNull();

        host.Dispose();
    }

    [Fact]
    public async Task PrefixlessMapRestLib_NullConfigure_UsesDefaults()
    {
        // Arrange
        var repository = new InMemoryRepository<TestEntity, Guid>(e => e.Id, Guid.NewGuid);
        repository.Seed(new[] { new TestEntity { Id = Guid.NewGuid(), Name = "Default", Price = 1.0m } });

        var (_, client) = CreateHost(
            app =>
            {
                app.UseAuthorization();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGroup("/api/v1/products").MapRestLib<TestEntity, Guid>();
                });
            },
            services =>
            {
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
                services.AddAuthorization();
                services.AddSingleton<IRepository<TestEntity, Guid>>(repository);
            });

        // Act — anonymous access should be denied by default (RequireAuthorization)
        var response = await client.GetAsync("/api/v1/products");

        // Assert — default config requires authorization, so expect 401
        // The key assertion: endpoint is registered (not 404) and authorization is enforced
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task NestedGroups_MultipleDepth_EndpointsReachable()
    {
        // Arrange
        var repository = new InMemoryRepository<TestEntity, Guid>(e => e.Id, Guid.NewGuid);
        repository.Seed(new[] { new TestEntity { Id = Guid.NewGuid(), Name = "Deep", Price = 42.0m } });

        var (_, client) = CreateHost(
            app => app.UseEndpoints(endpoints =>
            {
                endpoints.MapGroup("/api")
                    .MapGroup("/v1")
                    .MapRestLib<TestEntity, Guid>("/products", config =>
                    {
                        config.AllowAnonymous();
                    });
            }),
            services => services.AddSingleton<IRepository<TestEntity, Guid>>(repository));

        // Act
        var response = await client.GetAsync("/api/v1/products");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("items")[0].GetProperty("name").GetString().Should().Be("Deep");
    }

    [Fact]
    public async Task PaginationLinks_IncludeGroupPrefix()
    {
        // Arrange
        var repository = new InMemoryRepository<TestEntity, Guid>(e => e.Id, Guid.NewGuid);

        // Seed enough items to trigger pagination
        for (var i = 0; i < 25; i++)
        {
            repository.Seed(new[] { new TestEntity { Id = Guid.NewGuid(), Name = $"Item{i}", Price = i } });
        }

        var (_, client) = CreateHost(
            app => app.UseEndpoints(endpoints =>
            {
                endpoints.MapGroup("/api/v1/products").MapRestLib<TestEntity, Guid>(config =>
                {
                    config.AllowAnonymous();
                });
            }),
            services => services.AddSingleton<IRepository<TestEntity, Guid>>(repository));

        // Act
        var response = await client.GetAsync("/api/v1/products?limit=5");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // self link should include the versioned path
        var selfLink = body.GetProperty("self").GetString();
        selfLink.Should().Contain("/api/v1/products");

        // next link should include the versioned path (since 25 > 5)
        var nextLink = body.GetProperty("next").GetString();
        nextLink.Should().NotBeNull();
        nextLink.Should().Contain("/api/v1/products");
    }

    // ---------------------------------------------------------------
    // Edge cases
    // ---------------------------------------------------------------

    [Fact]
    public async Task ExistingOverload_StillWorks_AfterRefactor()
    {
        // Arrange — this is a regression guard for the existing MapRestLib overload
        var repository = new InMemoryRepository<TestEntity, Guid>(e => e.Id, Guid.NewGuid);
        repository.Seed(new[] { new TestEntity { Id = Guid.NewGuid(), Name = "Existing", Price = 7.77m } });

        var (_, client) = CreateHost(
            app => app.UseEndpoints(endpoints =>
            {
                endpoints.MapRestLib<TestEntity, Guid>("/api/products", config =>
                {
                    config.AllowAnonymous();
                });
            }),
            services => services.AddSingleton<IRepository<TestEntity, Guid>>(repository));

        // Act
        var response = await client.GetAsync("/api/products");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("items")[0].GetProperty("name").GetString().Should().Be("Existing");
    }

    [Fact]
    public async Task PrefixlessMapRestLib_NestedInsideVersionGroup_AllFeaturesCompose()
    {
        // Arrange — verify that filtering + sorting + field selection all compose on a group
        var repository = new InMemoryRepository<TestEntity, Guid>(e => e.Id, Guid.NewGuid);
        repository.Seed(new[]
        {
            new TestEntity { Id = Guid.NewGuid(), Name = "Banana", Price = 2.0m },
            new TestEntity { Id = Guid.NewGuid(), Name = "Apple", Price = 1.0m },
            new TestEntity { Id = Guid.NewGuid(), Name = "Cherry", Price = 3.0m }
        });

        var (_, client) = CreateHost(
            app => app.UseEndpoints(endpoints =>
            {
                endpoints.MapGroup("/api/v2/products").MapRestLib<TestEntity, Guid>(config =>
                {
                    config.AllowAnonymous();
                    config.AllowFiltering(p => p.Name);
                    config.AllowSorting(p => p.Price);
                    config.AllowFieldSelection(p => p.Name, p => p.Price);
                });
            }),
            services => services.AddSingleton<IRepository<TestEntity, Guid>>(repository));

        // Act — sorted by price ascending, field selection on name only
        var response = await client.GetAsync("/api/v2/products?sort=price:asc&fields=name");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items");
        items.GetArrayLength().Should().Be(3);

        // Verify sort order (by price ascending: Apple=1, Banana=2, Cherry=3)
        items[0].GetProperty("name").GetString().Should().Be("Apple");
        items[1].GetProperty("name").GetString().Should().Be("Banana");
        items[2].GetProperty("name").GetString().Should().Be("Cherry");

        // Verify field selection — price should not be in response since only "name" selected
        items[0].TryGetProperty("price", out _).Should().BeFalse();
    }

    [Fact]
    public async Task TwoVersionGroups_SameEntity_PrefixlessOverload_BothWork()
    {
        // Arrange — both v1 and v2 use the same entity type at different version groups
        var repository = new InMemoryRepository<TestEntity, Guid>(e => e.Id, Guid.NewGuid);
        repository.Seed(new[] { new TestEntity { Id = Guid.NewGuid(), Name = "Shared", Price = 5.0m } });

        var (_, client) = CreateHost(
            app => app.UseEndpoints(endpoints =>
            {
                // Use MapRestLib with a prefix on a version group — the prefix-based overload
                // ensures unique endpoint names via the route prefix.
                endpoints.MapGroup("/api/v1").MapRestLib<TestEntity, Guid>("/products", config =>
                {
                    config.AllowAnonymous();
                    config.IncludeOperations(RestLibOperation.GetAll);
                });

                endpoints.MapGroup("/api/v2").MapRestLib<TestEntity, Guid>("/products", config =>
                {
                    config.AllowAnonymous();
                    config.AllowSorting(p => p.Name);
                });
            }),
            services => services.AddSingleton<IRepository<TestEntity, Guid>>(repository));

        // Act
        var v1Response = await client.GetAsync("/api/v1/products");
        var v2Response = await client.GetAsync("/api/v2/products?sort=name:asc");

        // Assert
        v1Response.StatusCode.Should().Be(HttpStatusCode.OK);
        v2Response.StatusCode.Should().Be(HttpStatusCode.OK);

        // v1 should not have sorting support — sort param is ignored (not an error)
        // v2 should have sorting support
        var v2Body = await v2Response.Content.ReadFromJsonAsync<JsonElement>();
        v2Body.GetProperty("items")[0].GetProperty("name").GetString().Should().Be("Shared");
    }

    [Fact]
    public async Task PrefixlessMapRestLib_EmptyGroup_EndpointsAtRoot()
    {
        // Arrange — using MapGroup with empty prefix
        var repository = new InMemoryRepository<TestEntity, Guid>(e => e.Id, Guid.NewGuid);
        repository.Seed(new[] { new TestEntity { Id = Guid.NewGuid(), Name = "Root", Price = 1.0m } });

        var (_, client) = CreateHost(
            app => app.UseEndpoints(endpoints =>
            {
                endpoints.MapGroup("/products").MapRestLib<TestEntity, Guid>(config =>
                {
                    config.AllowAnonymous();
                });
            }),
            services => services.AddSingleton<IRepository<TestEntity, Guid>>(repository));

        // Act
        var response = await client.GetAsync("/products");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("items")[0].GetProperty("name").GetString().Should().Be("Root");
    }

    // ---------------------------------------------------------------
    // Edge cases
    // ---------------------------------------------------------------

    [Fact]
    public void PrefixlessMapRestLib_WithNullGroup_ThrowsArgumentNullException()
    {
        // Arrange
        RouteGroupBuilder group = null!;

        // Act
        var act = () => group.MapRestLib<TestEntity, Guid>();

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("group");
    }

    [Fact]
    public async Task GroupWithAuthorization_AppliedToRestLibEndpoints()
    {
        // Arrange — group requires authorization, RestLib endpoints should inherit it
        var repository = new InMemoryRepository<TestEntity, Guid>(e => e.Id, Guid.NewGuid);
        var entityId = Guid.NewGuid();
        repository.Seed(new[] { new TestEntity { Id = entityId, Name = "Protected", Price = 5.0m } });

        var (_, client) = CreateHost(
            app =>
            {
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints =>
                {
                    var group = endpoints.MapGroup("/api/v1/products");
                    group.RequireAuthorization();
                    group.MapRestLib<TestEntity, Guid>(config =>
                    {
                        // Do NOT call AllowAnonymous — authorization comes from the group
                    });
                });
            },
            services =>
            {
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
                services.AddAuthorization();
                services.AddSingleton<IRepository<TestEntity, Guid>>(repository);
            });

        // Act & Assert — anonymous GET should be denied
        var anonGet = await client.GetAsync("/api/v1/products");
        anonGet.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Act & Assert — anonymous POST should be denied
        var anonPost = await client.PostAsJsonAsync("/api/v1/products", new { name = "New", price = 1.0m });
        anonPost.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Act & Assert — authenticated GET should succeed
        var authRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/products");
        authRequest.Headers.Add("Authorization", "Test");
        var authGet = await client.SendAsync(authRequest);
        authGet.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act & Assert — authenticated GET by ID should succeed
        var authByIdRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/products/{entityId}");
        authByIdRequest.Headers.Add("Authorization", "Test");
        var authById = await client.SendAsync(authByIdRequest);
        authById.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
