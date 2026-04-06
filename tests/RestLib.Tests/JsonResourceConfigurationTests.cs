using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi;
using RestLib.Abstractions;
using RestLib.Configuration;
using RestLib.Hooks;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

public class JsonResourceConfigurationTests
{
    [Fact]
    public async Task AddJsonResource_WithInlineConfiguration_MapsConfiguredOperationsAndFiltering()
    {
        // Arrange
        using var host = await CreateHost(services =>
        {
            services.AddJsonResource<TestEntity, Guid>(new RestLibJsonResourceConfiguration
            {
                Name = "items",
                Route = "/api/items",
                AllowAnonymousAll = true,
                Operations = new RestLibJsonOperationSelection
                {
                    Include = [RestLibOperation.GetAll, RestLibOperation.GetById, RestLibOperation.Create]
                },
                Filtering = [nameof(TestEntity.Name)],
                OpenApi = new RestLibJsonOpenApiConfiguration
                {
                    Tag = "Items",
                    Summaries = new Dictionary<string, string>
                    {
                        [nameof(RestLibOperation.GetAll)] = "List configured items"
                    }
                }
            });
        });

        var client = host.GetTestClient();
        var repository = host.Services.GetRequiredService<TestEntityRepository>();
        var id = Guid.NewGuid();
        repository.Seed(new TestEntity { Id = id, Name = "alpha", Price = 10m });

        // Act
        var getAll = await client.GetAsync("/api/items?name=alpha");
        var getById = await client.GetAsync($"/api/items/{id}");
        var create = await client.PostAsJsonAsync("/api/items", new TestEntity { Name = "beta", Price = 5m });
        var delete = await client.DeleteAsync($"/api/items/{id}");

        // Assert
        getAll.StatusCode.Should().Be(HttpStatusCode.OK);
        getById.StatusCode.Should().Be(HttpStatusCode.OK);
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        delete.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);

        var openApi = await client.GetStringAsync("/openapi/v1.json");
        var result = OpenApiDocument.Parse(openApi, "json");
        var document = result.Document!;
        var pathItem = document.Paths!["/api/items"]!;
        var getOp = pathItem.Operations![HttpMethod.Get]!;
        getOp.Summary.Should().Be("List configured items");
        getOp.Tags.Should().Contain(tag => tag.Name == "Items");
    }

    [Fact]
    public async Task AddJsonResource_FromConfigurationSection_LoadsResourceDefinition()
    {
        // Arrange
        var configurationData = new Dictionary<string, string?>
        {
            ["RestLib:Resources:0:Name"] = "items",
            ["RestLib:Resources:0:Route"] = "/api/items",
            ["RestLib:Resources:0:AllowAnonymousAll"] = "true",
            ["RestLib:Resources:0:Operations:Exclude:0"] = nameof(RestLibOperation.Delete)
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationData)
            .Build();

        using var host = await CreateHost(services =>
        {
            services.AddJsonResource<TestEntity, Guid>(configuration.GetSection("RestLib:Resources:0"));
        });

        var client = host.GetTestClient();
        var repository = host.Services.GetRequiredService<TestEntityRepository>();
        var id = Guid.NewGuid();
        repository.Seed(new TestEntity { Id = id, Name = "configured", Price = 5m });

        // Act
        var getAll = await client.GetAsync("/api/items");
        var delete = await client.DeleteAsync($"/api/items/{id}");

        // Assert
        getAll.StatusCode.Should().Be(HttpStatusCode.OK);
        delete.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task AddJsonResource_WithNamedHooks_RunsConfiguredHooksPerOperation()
    {
        // Arrange
        using var host = await CreateHost(services =>
        {
            services.AddNamedHook<TestEntity, Guid>(HookNames.StampCreateName, context =>
        {
            if (context.Entity is not null)
            {
                context.Entity.Name = $"created:{context.Entity.Name}";
            }

            return Task.CompletedTask;
        });

            services.AddNamedHook<TestEntity, Guid>(HookNames.StampUpdateName, context =>
        {
            if (context.Entity is not null)
            {
                context.Entity.Name = $"updated:{context.Entity.Name}";
            }

            return Task.CompletedTask;
        });

            services.AddJsonResource<TestEntity, Guid>(new RestLibJsonResourceConfiguration
            {
                Name = "items",
                Route = "/api/items",
                AllowAnonymousAll = true,
                Hooks = new RestLibJsonHookConfiguration
                {
                    BeforePersist = new RestLibJsonHookStage
                    {
                        ByOperation = new Dictionary<string, List<string>>
                        {
                            [nameof(RestLibOperation.Create)] = [HookNames.StampCreateName],
                            [nameof(RestLibOperation.Update)] = [HookNames.StampUpdateName]
                        }
                    }
                }
            });
        });

        var client = host.GetTestClient();
        var repository = host.Services.GetRequiredService<TestEntityRepository>();

        // Act
        var createResponse = await client.PostAsJsonAsync("/api/items", new TestEntity { Name = "first", Price = 1m });
        var created = await createResponse.Content.ReadFromJsonAsync<TestEntity>();

        // Assert
        created.Should().NotBeNull();
        created!.Name.Should().Be("created:first");

        // Act
        var updateResponse = await client.PutAsJsonAsync($"/api/items/{created.Id}", new TestEntity { Name = "second", Price = 2m });
        var updated = await updateResponse.Content.ReadFromJsonAsync<TestEntity>();

        // Assert
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("updated:second");

        var persisted = await repository.GetByIdAsync(created.Id);
        persisted.Should().NotBeNull();
        persisted!.Name.Should().Be("updated:second");
    }

    [Fact]
    public async Task AddJsonResource_WithKeyProperty_UsesConfiguredKeySelector()
    {
        // Arrange
        using var host = await CreateHost(
          services =>
          {
              services.AddSingleton<CustomKeyEntityRepository>();
              services.AddSingleton<IRepository<CustomKeyEntity, Guid>>(sp =>
              sp.GetRequiredService<CustomKeyEntityRepository>());

              services.AddJsonResource<CustomKeyEntity, Guid>(new RestLibJsonResourceConfiguration
              {
                  Name = "custom-key-items",
                  Route = "/api/custom-key-items",
                  AllowAnonymousAll = true,
                  KeyProperty = nameof(CustomKeyEntity.Code),
                  Operations = new RestLibJsonOperationSelection
                  {
                      Include = [RestLibOperation.GetAll, RestLibOperation.GetById, RestLibOperation.Create]
                  }
              });
          });

        var client = host.GetTestClient();
        var repository = host.Services.GetRequiredService<CustomKeyEntityRepository>();

        // Act — Create an entity via POST
        var createResponse = await client.PostAsJsonAsync("/api/custom-key-items",
            new CustomKeyEntity { Label = "test-item" });

        // Assert
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await createResponse.Content.ReadFromJsonAsync<CustomKeyEntity>();
        created.Should().NotBeNull();
        created!.Code.Should().NotBe(Guid.Empty);

        // Verify the Location header points to the correct key
        createResponse.Headers.Location.Should().NotBeNull();
        createResponse.Headers.Location!.ToString().Should().Contain(created.Code.ToString());

        // Act — Fetch by the custom key property
        var getByIdResponse = await client.GetAsync($"/api/custom-key-items/{created.Code}");

        // Assert
        getByIdResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var fetched = await getByIdResponse.Content.ReadFromJsonAsync<CustomKeyEntity>();
        fetched.Should().NotBeNull();
        fetched!.Label.Should().Be("test-item");
    }

    [Fact]
    public async Task AddJsonResource_WithDefaultHooks_RunsHooksForAllOperations()
    {
        // Arrange
        using var host = await CreateHost(services =>
        {
            services.AddNamedHook<TestEntity, Guid>(HookNames.DefaultStamp, context =>
        {
            if (context.Entity is not null)
            {
                context.Entity.Name = $"stamped:{context.Entity.Name}";
            }

            return Task.CompletedTask;
        });

            services.AddJsonResource<TestEntity, Guid>(new RestLibJsonResourceConfiguration
            {
                Name = "items",
                Route = "/api/items",
                AllowAnonymousAll = true,
                Hooks = new RestLibJsonHookConfiguration
                {
                    BeforePersist = new RestLibJsonHookStage
                    {
                        Default = [HookNames.DefaultStamp]
                    }
                }
            });
        });

        var client = host.GetTestClient();

        // Act — Create should trigger the default hook
        var createResponse = await client.PostAsJsonAsync("/api/items", new TestEntity { Name = "alpha", Price = 1m });
        var created = await createResponse.Content.ReadFromJsonAsync<TestEntity>();

        // Assert
        created.Should().NotBeNull();
        created!.Name.Should().Be("stamped:alpha");

        // Act — Update should also trigger the default hook
        var updateResponse = await client.PutAsJsonAsync($"/api/items/{created.Id}",
            new TestEntity { Name = "beta", Price = 2m });
        var updated = await updateResponse.Content.ReadFromJsonAsync<TestEntity>();

        // Assert
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("stamped:beta");
    }

    [Fact]
    public async Task AddJsonResource_WithMultipleHooksInStage_ExecutesAllSequentially()
    {
        // Arrange
        using var host = await CreateHost(services =>
        {
            services.AddNamedHook<TestEntity, Guid>(HookNames.PrefixName, context =>
        {
            if (context.Entity is not null)
            {
                context.Entity.Name = $"prefix:{context.Entity.Name}";
            }

            return Task.CompletedTask;
        });

            services.AddNamedHook<TestEntity, Guid>(HookNames.SuffixName, context =>
        {
            if (context.Entity is not null)
            {
                context.Entity.Name = $"{context.Entity.Name}:suffix";
            }

            return Task.CompletedTask;
        });

            services.AddJsonResource<TestEntity, Guid>(new RestLibJsonResourceConfiguration
            {
                Name = "items",
                Route = "/api/items",
                AllowAnonymousAll = true,
                Hooks = new RestLibJsonHookConfiguration
                {
                    BeforePersist = new RestLibJsonHookStage
                    {
                        Default = [HookNames.PrefixName, HookNames.SuffixName]
                    }
                }
            });
        });

        var client = host.GetTestClient();

        // Act
        var createResponse = await client.PostAsJsonAsync("/api/items", new TestEntity { Name = "test", Price = 1m });
        var created = await createResponse.Content.ReadFromJsonAsync<TestEntity>();

        // Assert
        created.Should().NotBeNull();
        // PrefixName runs first: "test" -> "prefix:test"
        // SuffixName runs second: "prefix:test" -> "prefix:test:suffix"
        created!.Name.Should().Be("prefix:test:suffix");
    }

    [Fact]
    public async Task AddJsonResource_WithNamedErrorHooks_HandlesErrorsPerConfiguration()
    {
        // Arrange
        using var host = await CreateHost(services =>
        {
            services.AddNamedHook<TestEntity, Guid>(HookNames.ThrowOnPersist, _ =>
        {
            throw new InvalidOperationException("Simulated persistence error");
        });

            services.AddNamedErrorHook<TestEntity, Guid>(HookNames.HandleTestError, context =>
        {
            context.Handled = true;
            context.ErrorResult = Results.Json(
            new { error = "handled", message = context.Exception.Message },
            statusCode: 422);
            return Task.CompletedTask;
        });

            services.AddJsonResource<TestEntity, Guid>(new RestLibJsonResourceConfiguration
            {
                Name = "items",
                Route = "/api/items",
                AllowAnonymousAll = true,
                Hooks = new RestLibJsonHookConfiguration
                {
                    BeforePersist = new RestLibJsonHookStage
                    {
                        Default = [HookNames.ThrowOnPersist]
                    },
                    OnError = new RestLibJsonErrorHookStage
                    {
                        Default = [HookNames.HandleTestError]
                    }
                }
            });
        });

        var client = host.GetTestClient();

        // Act
        var createResponse = await client.PostAsJsonAsync("/api/items", new TestEntity { Name = "will-fail", Price = 1m });

        // Assert
        createResponse.StatusCode.Should().Be((HttpStatusCode)422);

        var body = await createResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        body.Should().NotBeNull();
        body!["error"].Should().Be("handled");
        body["message"].Should().Be("Simulated persistence error");
    }

    [Fact]
    public async Task AddJsonResource_WithOpenApiDeprecationAndDescriptions_PropagatesToOpenApiDocument()
    {
        // Arrange
        using var host = await CreateHost(services =>
        {
            services.AddJsonResource<TestEntity, Guid>(new RestLibJsonResourceConfiguration
            {
                Name = "items",
                Route = "/api/items",
                AllowAnonymousAll = true,
                Operations = new RestLibJsonOperationSelection
                {
                    Include = [RestLibOperation.GetAll, RestLibOperation.GetById]
                },
                OpenApi = new RestLibJsonOpenApiConfiguration
                {
                    Tag = "LegacyItems",
                    Deprecated = true,
                    DeprecationMessage = "Use /api/v2/items instead.",
                    Summaries = new Dictionary<string, string>
                    {
                        [nameof(RestLibOperation.GetAll)] = "List all legacy items"
                    },
                    Descriptions = new Dictionary<string, string>
                    {
                        [nameof(RestLibOperation.GetAll)] = "Returns a paginated list of legacy items."
                    }
                }
            });
        });

        var client = host.GetTestClient();

        // Act
        var openApi = await client.GetStringAsync("/openapi/v1.json");
        var document = OpenApiDocument.Parse(openApi, "json").Document;

        var getAllOperation = document!.Paths!["/api/items"]!.Operations![HttpMethod.Get]!;

        // Assert
        getAllOperation.Summary.Should().Be("List all legacy items");
        getAllOperation.Deprecated.Should().BeTrue();
        getAllOperation.Description.Should().Contain("Use /api/v2/items instead.");
        getAllOperation.Description.Should().Contain("Returns a paginated list of legacy items.");
        getAllOperation.Tags.Should().Contain(tag => tag.Name == "LegacyItems");
    }

    [Fact]
    public async Task MapJsonResource_WithName_MapsSingleResource()
    {
        // Arrange
        using var host = await CreateHost(
          services =>
          {
              services.AddJsonResource<TestEntity, Guid>(new RestLibJsonResourceConfiguration
              {
                  Name = "first-resource",
                  Route = "/api/first",
                  AllowAnonymousAll = true
              });

              services.AddJsonResource<TestEntity, Guid>(new RestLibJsonResourceConfiguration
              {
                  Name = "second-resource",
                  Route = "/api/second",
                  AllowAnonymousAll = true
              });
          },
          mapResourceName: "first-resource");

        var client = host.GetTestClient();
        var repository = host.Services.GetRequiredService<TestEntityRepository>();
        var id = Guid.NewGuid();
        repository.Seed(new TestEntity { Id = id, Name = "test", Price = 1m });

        // Act — First resource should be mapped
        var firstResponse = await client.GetAsync($"/api/first/{id}");

        // Assert
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act — Second resource should NOT be mapped
        var secondResponse = await client.GetAsync($"/api/second/{id}");

        // Assert
        secondResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public void AddJsonResource_WithUnregisteredHookName_ThrowsOnMapping()
    {
        // Act
        var act = async () =>
        {
            using var host = await CreateHost(services =>
        {
            services.AddJsonResource<TestEntity, Guid>(new RestLibJsonResourceConfiguration
            {
                Name = "items",
                Route = "/api/items",
                AllowAnonymousAll = true,
                Hooks = new RestLibJsonHookConfiguration
                {
                    BeforePersist = new RestLibJsonHookStage
                    {
                        Default = ["NonExistentHook"]
                    }
                }
            });
        });
        };

        // Assert
        act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No RestLib hook named 'NonExistentHook'*");
    }

    [Fact]
    public void AddJsonResource_WithDuplicateName_ThrowsOnRegistration()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddRestLib();
        services.AddSingleton<TestEntityRepository>();
        services.AddSingleton<IRepository<TestEntity, Guid>>(sp => sp.GetRequiredService<TestEntityRepository>());

        services.AddJsonResource<TestEntity, Guid>(new RestLibJsonResourceConfiguration
        {
            Name = "items",
            Route = "/api/items",
            AllowAnonymousAll = true
        });

        // Act
        var act = () => services.AddJsonResource<TestEntity, Guid>(new RestLibJsonResourceConfiguration
        {
            Name = "items",
            Route = "/api/other-items",
            AllowAnonymousAll = true
        });

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*'items' is already registered*");
    }

    [Fact]
    public void AddJsonResource_WithInvalidOperationNameInSummaries_ThrowsOnMapping()
    {
        // Act
        var act = async () =>
        {
            using var host = await CreateHost(services =>
        {
            services.AddJsonResource<TestEntity, Guid>(new RestLibJsonResourceConfiguration
            {
                Name = "items",
                Route = "/api/items",
                AllowAnonymousAll = true,
                OpenApi = new RestLibJsonOpenApiConfiguration
                {
                    Summaries = new Dictionary<string, string>
                    {
                        ["InvalidOperation"] = "This should fail"
                    }
                }
            });
        });
        };

        // Assert
        act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*'InvalidOperation' is not a valid RestLib operation name*");
    }

    [Fact]
    public void AddJsonResource_WithBothIncludeAndExclude_ThrowsOnMapping()
    {
        // Act
        var act = async () =>
        {
            using var host = await CreateHost(services =>
        {
            services.AddJsonResource<TestEntity, Guid>(new RestLibJsonResourceConfiguration
            {
                Name = "items",
                Route = "/api/items",
                AllowAnonymousAll = true,
                Operations = new RestLibJsonOperationSelection
                {
                    Include = [RestLibOperation.GetAll],
                    Exclude = [RestLibOperation.Delete]
                }
            });
        });
        };

        // Assert
        act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*cannot configure both include and exclude*");
    }

    [Fact]
    public void AddJsonResource_WithInvalidKeyProperty_ThrowsOnMapping()
    {
        // Act
        var act = async () =>
        {
            using var host = await CreateHost(services =>
        {
            services.AddJsonResource<TestEntity, Guid>(new RestLibJsonResourceConfiguration
            {
                Name = "items",
                Route = "/api/items",
                AllowAnonymousAll = true,
                KeyProperty = "NonExistentProperty"
            });
        });
        };

        // Assert
        act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Key property 'NonExistentProperty' was not found*");
    }

    [Fact]
    public async Task AddJsonResource_WithFilteringOperatorPreset_ExpandsPresetToIndividualOperators()
    {
        // Arrange
        using var host = await CreateHost(services =>
        {
            services.AddJsonResource<TestEntity, Guid>(new RestLibJsonResourceConfiguration
            {
                Name = "items",
                Route = "/api/items",
                AllowAnonymousAll = true,
                FilteringOperators = new Dictionary<string, List<string>>
                {
                    [nameof(TestEntity.Price)] = ["comparison"]
                }
            });
        });

        var client = host.GetTestClient();
        var repository = host.Services.GetRequiredService<TestEntityRepository>();
        repository.Seed(new TestEntity { Id = Guid.NewGuid(), Name = "item", Price = 10m });

        // Act — "comparison" preset includes gt, so price[gt] should be accepted
        var gtResponse = await client.GetAsync("/api/items?price[gt]=5");

        // Act — "comparison" preset does NOT include contains, so it should be rejected
        var containsResponse = await client.GetAsync("/api/items?price[contains]=5");

        // Assert
        gtResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        containsResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AddJsonResource_WithMixedPresetsAndOperators_ExpandsAndDeduplicates()
    {
        // Arrange — "equality" preset includes eq and neq; "contains" added individually
        using var host = await CreateHost(services =>
        {
            services.AddJsonResource<TestEntity, Guid>(new RestLibJsonResourceConfiguration
            {
                Name = "items",
                Route = "/api/items",
                AllowAnonymousAll = true,
                FilteringOperators = new Dictionary<string, List<string>>
                {
                    [nameof(TestEntity.Name)] = ["equality", "contains"]
                }
            });
        });

        var client = host.GetTestClient();
        var repository = host.Services.GetRequiredService<TestEntityRepository>();
        repository.Seed(new TestEntity { Id = Guid.NewGuid(), Name = "alpha", Price = 1m });

        // Act — "equality" includes eq; "contains" was added individually; both should be accepted
        var eqResponse = await client.GetAsync("/api/items?name[eq]=alpha");
        var containsResponse = await client.GetAsync("/api/items?name[contains]=lph");

        // Act — "gt" is not in equality or contains, so it should be rejected
        var gtResponse = await client.GetAsync("/api/items?name[gt]=a");

        // Assert
        eqResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        containsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        gtResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public void AddJsonResource_WithInvalidFilterOperatorName_ThrowsOnMapping()
    {
        // Arrange & Act
        var act = async () =>
        {
            using var host = await CreateHost(services =>
        {
            services.AddJsonResource<TestEntity, Guid>(new RestLibJsonResourceConfiguration
            {
                Name = "items",
                Route = "/api/items",
                AllowAnonymousAll = true,
                FilteringOperators = new Dictionary<string, List<string>>
                {
                    [nameof(TestEntity.Price)] = ["invalid_operator"]
                }
            });
        });
        };

        // Assert
        act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*'invalid_operator' is not a valid filter operator or preset*");
    }

    [Fact]
    public async Task AddJsonResource_WithAllPreset_EnablesEveryFilterOperator()
    {
        // Arrange
        using var host = await CreateHost(services =>
        {
            services.AddJsonResource<TestEntity, Guid>(new RestLibJsonResourceConfiguration
            {
                Name = "items",
                Route = "/api/items",
                AllowAnonymousAll = true,
                FilteringOperators = new Dictionary<string, List<string>>
                {
                    [nameof(TestEntity.Name)] = ["all"]
                }
            });
        });

        var client = host.GetTestClient();
        var repository = host.Services.GetRequiredService<TestEntityRepository>();
        repository.Seed(new TestEntity { Id = Guid.NewGuid(), Name = "hello", Price = 1m });

        // Act — "all" preset includes every operator; starts_with should be accepted
        var startsWithResponse = await client.GetAsync("/api/items?name[starts_with]=hel");
        var eqResponse = await client.GetAsync("/api/items?name[eq]=hello");
        var containsResponse = await client.GetAsync("/api/items?name[contains]=ell");

        // Assert — all operators should be accepted (200), not rejected (400)
        startsWithResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        eqResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        containsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<IHost> CreateHost(
        Action<IServiceCollection> configureServices,
        string? mapResourceName = null)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRestLib();
                    services.AddSingleton<TestEntityRepository>();
                    services.AddSingleton<IRepository<TestEntity, Guid>>(sp => sp.GetRequiredService<TestEntityRepository>());
                    services.AddRouting();
                    services.AddOpenApi();
                    configureServices(services);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                {
                    endpoints.MapOpenApi();
                    if (mapResourceName is not null)
                    {
                        endpoints.MapJsonResource(mapResourceName);
                    }
                    else
                    {
                        endpoints.MapJsonResources();
                    }
                });
                });
            })
            .Build();

        await host.StartAsync();
        return host;
    }
}
