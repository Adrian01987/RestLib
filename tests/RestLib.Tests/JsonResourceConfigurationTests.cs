using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi;
using RestLib.Abstractions;
using RestLib.Configuration;
using RestLib.Hooks;
using RestLib.InMemory;
using RestLib.Serialization;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

[Trait("Type", "Integration")]
[Trait("Feature", "Configuration")]
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
    public async Task AddJsonResourceFromFile_WithValidFile_MapsConfiguredResource()
    {
        // Arrange
        var filePath = CreateResourceFile(
            """
            {
              "$schema": "../../schemas/restlib-resource.schema.json",
              "Name": "items",
              "Route": "/api/items",
              "AllowAnonymousAll": true,
              "Operations": {
                "Include": ["GetAll", "GetById"]
              }
            }
            """);

        await using var cleanup = new TempPath(filePath);

        using var host = await CreateHost(services =>
        {
            services.AddJsonResourceFromFile<TestEntity, Guid>(filePath);
        });

        var client = host.GetTestClient();
        var repository = host.Services.GetRequiredService<TestEntityRepository>();
        repository.Seed(new TestEntity { Id = Guid.NewGuid(), Name = "alpha", Price = 10m });

        // Act
        var response = await client.GetAsync("/api/items");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void AddJsonResourceFromFile_WithMissingFile_ThrowsFileNotFoundWithPath()
    {
        // Arrange
        var missingPath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json");
        var services = new ServiceCollection();
        services.AddRestLib();

        // Act
        var act = () => services.AddJsonResourceFromFile<TestEntity, Guid>(missingPath);

        // Assert
        act.Should().Throw<FileNotFoundException>()
            .WithMessage($"*{missingPath}*");
    }

    [Fact]
    public void AddJsonResourceFromFile_WithMalformedJson_ThrowsStartupExceptionWithPathAndLocation()
    {
        // Arrange
        var filePath = CreateResourceFile(
            """
            {
              "Name": "items",
              "Route": "/api/items",
            """
        );

        using var cleanup = new TempPath(filePath);
        var services = new ServiceCollection();
        services.AddRestLib();

        // Act
        var act = () => services.AddJsonResourceFromFile<TestEntity, Guid>(filePath);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .Where(ex => ex.Message.Contains(filePath, StringComparison.Ordinal)
                && (ex.Message.Contains("line", StringComparison.OrdinalIgnoreCase)
                    || ex.Message.Contains("byte position", StringComparison.OrdinalIgnoreCase)
                    || ex.Message.Contains("column", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task AddRestLibFromFolder_WithEmptyFolder_RegistersNoResources()
    {
        // Arrange
        var folder = CreateTempDirectory();
        await using var cleanup = new TempPath(folder);

        using var host = await CreateHost(services =>
        {
            services.AddRestLibFromFolder(folder);
        });

        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/api/items");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public void AddRestLibFromFolder_WithMixedValidAndInvalidFiles_ThrowsWithInvalidFilePath()
    {
        // Arrange
        var folder = CreateTempDirectory();
        using var cleanup = new TempPath(folder);

        _ = CreateResourceFileInFolder(folder, "Valid.json",
            """
            {
              "Name": "valid-items",
              "Route": "/api/valid-items",
              "EntityType": "RestLib.Tests.Fakes.TestEntity, RestLib.Tests"
            }
            """);
        var invalidPath = CreateResourceFileInFolder(folder, "Invalid.json", "{ \"Name\": \"broken\",");

        var services = new ServiceCollection();
        services.AddRestLib();

        // Act
        var act = () => services.AddRestLibFromFolder(folder);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{invalidPath}*");
    }

    [Fact]
    public void AddRestLibFromFolder_WhenTypeCannotBeResolved_ThrowsClearStartupException()
    {
        // Arrange
        var folder = CreateTempDirectory();
        using var cleanup = new TempPath(folder);
        var filePath = CreateResourceFileInFolder(folder, "Unknown.json",
            """
            {
              "Name": "unknown-items",
              "Route": "/api/unknown-items"
            }
            """);

        var services = new ServiceCollection();
        services.AddRestLib();

        // Act
        var act = () => services.AddRestLibFromFolder(folder);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .Where(ex => ex.Message.Contains(filePath, StringComparison.Ordinal)
                && ex.Message.Contains("Could not resolve a CLR entity type", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AddRestLibFromFolder_WithEntityTypeField_MapsAllResources()
    {
        // Arrange
        var folder = CreateTempDirectory();
        await using var cleanup = new TempPath(folder);

        _ = CreateResourceFileInFolder(folder, "Items.json",
            """
            {
              "Name": "items",
              "Route": "/api/items",
              "EntityType": "RestLib.Tests.Fakes.TestEntity, RestLib.Tests",
              "AllowAnonymousAll": true
            }
            """);
        _ = CreateResourceFileInFolder(folder, "alt-items.json",
            """
            {
              "Name": "alt-items",
              "Route": "/api/alt-items",
              "EntityType": "RestLib.Tests.Fakes.TestEntity, RestLib.Tests",
              "AllowAnonymousAll": true
            }
            """);

        using var host = await CreateHost(services =>
        {
            services.AddRestLibFromFolder(folder);
        });

        var client = host.GetTestClient();
        var repository = host.Services.GetRequiredService<TestEntityRepository>();
        var id = Guid.NewGuid();
        repository.Seed(new TestEntity { Id = id, Name = "alpha", Price = 10m });

        // Act
        var first = await client.GetAsync($"/api/items/{id}");
        var second = await client.GetAsync($"/api/alt-items/{id}");

        // Assert
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AddRestLibFromFolder_WithRegisteredAssemblyAndMatchingFileName_MapsResource()
    {
        // Arrange
        var folder = CreateTempDirectory();
        await using var cleanup = new TempPath(folder);

        _ = CreateResourceFileInFolder(folder, "TestEntity.json",
            """
            {
              "Name": "items",
              "Route": "/api/items",
              "AllowAnonymousAll": true
            }
            """);

        using var host = await CreateHost(services =>
        {
            services.AddRestLibFromFolder(folder, options =>
            {
                options.Assemblies.Add(typeof(TestEntity).Assembly);
            });
        });

        var client = host.GetTestClient();
        var repository = host.Services.GetRequiredService<TestEntityRepository>();
        var id = Guid.NewGuid();
        repository.Seed(new TestEntity { Id = id, Name = "alpha", Price = 10m });

        // Act
        var response = await client.GetAsync($"/api/items/{id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Feature", "FolderUnifiedResolver")]
    public async Task AddRestLibFromFolder_WithUnifiedResolverReturningNull_FallsBackToLegacyResolver()
    {
        // Arrange
        var folder = CreateTempDirectory();
        await using var cleanup = new TempPath(folder);

        _ = CreateResourceFileInFolder(folder, "items.json",
            """
            {
              "Name": "items",
              "Route": "/api/items",
              "AllowAnonymousAll": true
            }
            """);

        using var host = await CreateHost(services =>
        {
            services.AddRestLibFromFolder(folder, options =>
            {
                options.UnifiedTypeResolver = static (_, _) => null;
                options.TypeResolver = static (_, _) => (typeof(TestEntity), typeof(Guid));
            });
        });

        var client = host.GetTestClient();
        var repository = host.Services.GetRequiredService<TestEntityRepository>();
        var id = Guid.NewGuid();
        repository.Seed(new TestEntity { Id = id, Name = "alpha", Price = 10m });

        // Act
        var response = await client.GetAsync($"/api/items/{id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Feature", "FolderUnifiedResolver")]
    public async Task AddRestLibFromFolder_WithUnifiedResolverAndLegacyResolverSet_PrefersUnifiedResolver()
    {
        // Arrange
        var folder = CreateTempDirectory();
        await using var cleanup = new TempPath(folder);

        _ = CreateResourceFileInFolder(folder, "items.json",
            """
            {
              "Name": "items",
              "Route": "/api/items",
              "KeyProperty": "Code",
              "AllowAnonymousAll": true
            }
            """);

        using var host = await CreateHost(services =>
        {
            services.AddSingleton<CustomKeyEntityRepository>();
            services.AddSingleton<IRepository<CustomKeyEntity, Guid>>(sp => sp.GetRequiredService<CustomKeyEntityRepository>());
            services.AddRestLibFromFolder(folder, options =>
            {
                options.UnifiedTypeResolver = static (_, _) => new RestLibResolvedResourceTypes
                {
                    ApiType = typeof(CustomKeyEntity),
                    KeyType = typeof(Guid),
                };
                options.TypeResolver = static (_, _) => (typeof(TestEntity), typeof(Guid));
            });
        });

        var client = host.GetTestClient();
        var repository = host.Services.GetRequiredService<CustomKeyEntityRepository>();
        var id = Guid.NewGuid();
        repository.Seed(new CustomKeyEntity { Code = id, Label = "preferred" });

        // Act
        var response = await client.GetAsync($"/api/items/{id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("code").GetGuid().Should().Be(id);
        document.RootElement.GetProperty("label").GetString().Should().Be("preferred");
    }

    [Fact]
    [Trait("Feature", "FolderUnifiedResolver")]
    public void AddRestLibFromFolder_WithUnifiedResolverReturningInvalidApiType_ThrowsClearException()
    {
        // Arrange
        var folder = CreateTempDirectory();
        using var cleanup = new TempPath(folder);
        var filePath = CreateResourceFileInFolder(folder, "items.json",
            """
            {
              "Name": "items",
              "Route": "/api/items",
              "KeyProperty": "Code",
              "AllowAnonymousAll": true
            }
            """);

        var services = new ServiceCollection();
        services.AddRestLib();

        // Act
        var act = () => services.AddRestLibFromFolder(folder, options =>
        {
            options.UnifiedTypeResolver = static (_, _) => new RestLibResolvedResourceTypes
            {
                ApiType = null!,
                KeyType = typeof(Guid),
            };
        });

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .Where(ex => ex.Message.Contains(filePath, StringComparison.Ordinal)
                && ex.Message.Contains("null API type", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Feature", "FolderUnifiedResolver")]
    public void AddRestLibFromFolder_WithUnifiedResolverReturningInvalidDbKeyType_ThrowsClearException()
    {
        // Arrange
        var folder = CreateTempDirectory();
        using var cleanup = new TempPath(folder);
        var filePath = CreateResourceFileInFolder(folder, "items.json",
            """
            {
              "Name": "items",
              "Route": "/api/items",
              "KeyProperty": "Code",
              "AllowAnonymousAll": true
            }
            """);

        var services = new ServiceCollection();
        services.AddRestLib();

        // Act
        var act = () => services.AddRestLibFromFolder(folder, options =>
        {
            options.UnifiedTypeResolver = static (_, _) => new RestLibResolvedResourceTypes
            {
                ApiType = typeof(CustomKeyEntity),
                DbType = typeof(CustomKeyEntity),
                KeyType = typeof(string),
            };
        });

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .Where(ex => ex.Message.Contains(filePath, StringComparison.Ordinal)
                && ex.Message.Contains("DB model type", StringComparison.Ordinal)
                && ex.Message.Contains("Code", StringComparison.Ordinal)
                && ex.Message.Contains("String", StringComparison.Ordinal));
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
    public async Task AddJsonResource_WithCompositeKey_UsesConfiguredRouteAndKeySelector()
    {
        // Arrange
        using var host = await CreateCompositeHost(services =>
        {
            services.AddJsonResource<JsonCompositeEntity, RestLibCompositeKey<Guid, string>>(new RestLibJsonResourceConfiguration
            {
                Name = "catalog-items",
                Route = "/api/catalog-items",
                AllowAnonymousAll = true,
                Operations = new RestLibJsonOperationSelection
                {
                    Include = [RestLibOperation.GetById, RestLibOperation.Create]
                },
                Key = new RestLibJsonKeyConfiguration
                {
                    Properties = [nameof(JsonCompositeEntity.TenantId), nameof(JsonCompositeEntity.Sku)],
                    RouteParameters = ["tenantId", "sku"]
                }
            });
        });

        var client = host.GetTestClient();
        var tenantId = Guid.NewGuid();
        var payload = new
        {
            tenant_id = tenantId,
            sku = "sku-created",
            product_name = "Created",
            price = 12.5m
        };

        // Act
        var createResponse = await client.PostAsJsonAsync("/api/catalog-items", payload);
        var getResponse = await client.GetAsync($"/api/catalog-items/{tenantId}/sku-created");

        // Assert
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        createResponse.Headers.Location.Should().NotBeNull();
        createResponse.Headers.Location!.ToString().Should().EndWith($"/api/catalog-items/{tenantId}/sku-created");

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("tenant_id").GetGuid().Should().Be(tenantId);
        document.RootElement.GetProperty("sku").GetString().Should().Be("sku-created");
        document.RootElement.GetProperty("product_name").GetString().Should().Be("Created");
    }

    [Fact]
    public void AddJsonResource_WithBothKeyPropertyAndCompositeKey_ThrowsOnMapping()
    {
        // Act
        var act = async () =>
        {
            using var host = await CreateCompositeHost(services =>
            {
                services.AddJsonResource<JsonCompositeEntity, RestLibCompositeKey<Guid, string>>(new RestLibJsonResourceConfiguration
                {
                    Name = "catalog-items",
                    Route = "/api/catalog-items",
                    AllowAnonymousAll = true,
                    KeyProperty = nameof(JsonCompositeEntity.TenantId),
                    Key = new RestLibJsonKeyConfiguration
                    {
                        Properties = [nameof(JsonCompositeEntity.TenantId), nameof(JsonCompositeEntity.Sku)],
                        RouteParameters = ["tenantId", "sku"]
                    }
                });
            });
        };

        // Assert
        act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*cannot configure both KeyProperty and Key*");
    }

    [Fact]
    public void AddJsonResource_WithDuplicateCompositeRouteParameterNames_ThrowsOnMapping()
    {
        // Act
        var act = async () =>
        {
            using var host = await CreateCompositeHost(services =>
            {
                services.AddJsonResource<JsonCompositeEntity, RestLibCompositeKey<Guid, string>>(new RestLibJsonResourceConfiguration
                {
                    Name = "catalog-items",
                    Route = "/api/catalog-items",
                    AllowAnonymousAll = true,
                    Key = new RestLibJsonKeyConfiguration
                    {
                        Properties = [nameof(JsonCompositeEntity.TenantId), nameof(JsonCompositeEntity.Sku)],
                        RouteParameters = ["id", "Id"]
                    }
                });
            });
        };

        // Assert
        act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*unique composite key route parameter names*");
    }

    [Fact]
    public async Task AddRestLibFromFolder_WithCompositeKeyConfiguration_ResolvesCompositeKeyResource()
    {
        // Arrange
        var folder = CreateTempDirectory();
        await using var cleanup = new TempPath(folder);
        var entityTypeName = $"{typeof(JsonCompositeEntity).FullName}, {typeof(JsonCompositeEntity).Assembly.GetName().Name}";
        _ = CreateResourceFileInFolder(folder, "catalog-items.json",
            $$"""
            {
              "$schema": "../../schemas/restlib-resource.schema.json",
              "EntityType": "{{entityTypeName}}",
              "Name": "catalog-items",
              "Route": "/api/catalog-items",
              "AllowAnonymousAll": true,
              "Operations": {
                "Include": ["GetById"]
              },
              "Key": {
                "Properties": ["TenantId", "Sku"],
                "RouteParameters": ["tenantId", "sku"]
              }
            }
            """);

        using var host = await CreateCompositeHost(services =>
        {
            services.AddRestLibFromFolder(folder);
        });

        var client = host.GetTestClient();
        var repository = host.Services.GetRequiredService<InMemoryRepository<JsonCompositeEntity, RestLibCompositeKey<Guid, string>>>();
        var entity = CreateCompositeEntity(productName: "Folder-loaded");
        repository.Clear();
        repository.Seed([entity]);

        // Act
        var response = await client.GetAsync(GetCompositeItemPath(entity));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("tenant_id").GetGuid().Should().Be(entity.TenantId);
        document.RootElement.GetProperty("sku").GetString().Should().Be(entity.Sku);
        document.RootElement.GetProperty("product_name").GetString().Should().Be("Folder-loaded");
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

    [Fact]
    public async Task AddJsonResource_WithNestedQueryPaths_MapsFilteringSortingAndFieldSelection()
    {
        // Arrange
        using var host = await CreateNestedHost(services =>
        {
            services.AddJsonResource<JsonNestedOrder, Guid>(new RestLibJsonResourceConfiguration
            {
                Name = "orders",
                Route = "/api/orders",
                AllowAnonymousAll = true,
                Filtering = [$"{nameof(JsonNestedOrder.Customer)}.{nameof(JsonNestedCustomer.Name)}"],
                FilteringOperators = new Dictionary<string, List<string>>
                {
                    [$"{nameof(JsonNestedOrder.Customer)}.{nameof(JsonNestedCustomer.Email)}"] = ["contains"]
                },
                Sorting =
                [
                    $"{nameof(JsonNestedOrder.Customer)}.{nameof(JsonNestedCustomer.Name)}",
                    nameof(JsonNestedOrder.OrderNumber)
                ],
                DefaultSort = "customer.name:asc",
                FieldSelection =
                [
                    nameof(JsonNestedOrder.OrderNumber),
                    $"{nameof(JsonNestedOrder.Customer)}.{nameof(JsonNestedCustomer.Email)}"
                ]
            });
        });

        var client = host.GetTestClient();
        var repository = host.Services.GetRequiredService<InMemoryRepository<JsonNestedOrder, Guid>>();
        repository.Clear();
        repository.Seed(
        [
            CreateNestedOrder("B-200", "Zoe", "zoe@example.com"),
            CreateNestedOrder("A-100", "Adam", "adam@example.com")
        ]);

        // Act
        var equalityFilterResponse = await client.GetAsync("/api/orders?customer.name=Adam");
        var operatorFilterResponse = await client.GetAsync("/api/orders?customer.email[contains]=zoe");
        var defaultSortResponse = await client.GetAsync("/api/orders");
        var fieldSelectionResponse = await client.GetAsync("/api/orders?fields=order_number,customer.email&sort=order_number:asc");

        // Assert
        equalityFilterResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var equalityDocument = JsonDocument.Parse(await equalityFilterResponse.Content.ReadAsStringAsync());
        equalityDocument.RootElement.GetProperty("items").GetArrayLength().Should().Be(1);
        equalityDocument.RootElement.GetProperty("items")[0].GetProperty("order_number").GetString().Should().Be("A-100");

        operatorFilterResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var operatorDocument = JsonDocument.Parse(await operatorFilterResponse.Content.ReadAsStringAsync());
        operatorDocument.RootElement.GetProperty("items").GetArrayLength().Should().Be(1);
        operatorDocument.RootElement.GetProperty("items")[0].GetProperty("order_number").GetString().Should().Be("B-200");

        defaultSortResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var sortDocument = JsonDocument.Parse(await defaultSortResponse.Content.ReadAsStringAsync());
        var sortedItems = sortDocument.RootElement.GetProperty("items");
        sortedItems[0].GetProperty("order_number").GetString().Should().Be("A-100");
        sortedItems[1].GetProperty("order_number").GetString().Should().Be("B-200");

        fieldSelectionResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var fieldSelectionDocument = JsonDocument.Parse(await fieldSelectionResponse.Content.ReadAsStringAsync());
        var selectedItems = fieldSelectionDocument.RootElement.GetProperty("items");
        selectedItems[0].TryGetProperty("order_number", out _).Should().BeTrue();
        selectedItems[0].TryGetProperty("customer.email", out _).Should().BeTrue();
        selectedItems[0].GetProperty("customer.email").GetString().Should().Be("adam@example.com");
        selectedItems[0].TryGetProperty("customer", out _).Should().BeFalse();
    }

    [Fact]
    public async Task AddJsonResource_FromConfigurationSection_WithNestedQueryPaths_LoadsDottedConfiguration()
    {
        // Arrange
        var configurationData = new Dictionary<string, string?>
        {
            ["RestLib:Resources:0:Name"] = "orders",
            ["RestLib:Resources:0:Route"] = "/api/orders",
            ["RestLib:Resources:0:AllowAnonymousAll"] = "true",
            ["RestLib:Resources:0:FilteringOperators:Customer.Email:0"] = "contains",
            ["RestLib:Resources:0:Sorting:0"] = "Customer.Name",
            ["RestLib:Resources:0:DefaultSort"] = "customer.name:asc",
            ["RestLib:Resources:0:FieldSelection:0"] = "OrderNumber",
            ["RestLib:Resources:0:FieldSelection:1"] = "Customer.Email"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationData)
            .Build();

        using var host = await CreateNestedHost(services =>
        {
            services.AddJsonResource<JsonNestedOrder, Guid>(configuration.GetSection("RestLib:Resources:0"));
        });

        var client = host.GetTestClient();
        var repository = host.Services.GetRequiredService<InMemoryRepository<JsonNestedOrder, Guid>>();
        repository.Clear();
        repository.Seed(
        [
            CreateNestedOrder("B-200", "Zoe", "zoe@example.com"),
            CreateNestedOrder("A-100", "Adam", "adam@example.com")
        ]);

        // Act
        var response = await client.GetAsync("/api/orders?customer.email[contains]=example.com&fields=order_number,customer.email");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = document.RootElement.GetProperty("items");
        items[0].GetProperty("order_number").GetString().Should().Be("A-100");
        items[0].GetProperty("customer.email").GetString().Should().Be("adam@example.com");
        items[1].GetProperty("order_number").GetString().Should().Be("B-200");
        items[1].GetProperty("customer.email").GetString().Should().Be("zoe@example.com");
    }

    [Fact]
    public void AddJsonResource_WithCollectionValuedNestedPath_ThrowsOnMapping()
    {
        // Act
        var act = async () =>
        {
            using var host = await CreateNestedHost(services =>
            {
                services.AddJsonResource<JsonNestedOrder, Guid>(new RestLibJsonResourceConfiguration
                {
                    Name = "orders",
                    Route = "/api/orders",
                    AllowAnonymousAll = true,
                    Filtering = ["Items.Name"]
                });
            });
        };

        // Assert
        act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*collection-valued segment 'Items'*");
    }

    [Fact]
    public async Task AddJsonResource_WithSearchArray_ConfiguresSearch()
    {
        // Arrange
        using var host = await CreateNestedHost(services =>
        {
            services.AddJsonResource<JsonNestedOrder, Guid>(new RestLibJsonResourceConfiguration
            {
                Name = "orders",
                Route = "/api/orders",
                AllowAnonymousAll = true,
                Search =
                [
                    nameof(JsonNestedOrder.OrderNumber),
                    $"{nameof(JsonNestedOrder.Customer)}.{nameof(JsonNestedCustomer.Email)}"
                ]
            });
        });

        var client = host.GetTestClient();
        var repository = host.Services.GetRequiredService<InMemoryRepository<JsonNestedOrder, Guid>>();
        repository.Clear();
        repository.Seed(
        [
            CreateNestedOrder("A-100", "Adam", "adam@example.com"),
            CreateNestedOrder("B-200", "Zoe", "zoe@example.com")
        ]);

        // Act
        var response = await client.GetAsync("/api/orders?q=zoe@example.com");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("items").GetArrayLength().Should().Be(1);
        document.RootElement.GetProperty("items")[0].GetProperty("order_number").GetString().Should().Be("B-200");
    }

    [Fact]
    public async Task AddJsonResource_WithSearchOptions_ConfiguresQueryParameterAndCaseSensitivity()
    {
        // Arrange
        using var host = await CreateNestedHost(services =>
        {
            services.AddJsonResource<JsonNestedOrder, Guid>(new RestLibJsonResourceConfiguration
            {
                Name = "orders",
                Route = "/api/orders",
                AllowAnonymousAll = true,
                Search = [nameof(JsonNestedOrder.OrderNumber)],
                SearchOptions = new RestLibJsonSearchOptionsConfiguration
                {
                    QueryParameter = "query",
                    CaseSensitive = true
                }
            });
        });

        var client = host.GetTestClient();
        var repository = host.Services.GetRequiredService<InMemoryRepository<JsonNestedOrder, Guid>>();
        repository.Clear();
        repository.Seed([CreateNestedOrder("AbC-100", "Adam", "adam@example.com")]);

        // Act
        var matchingResponse = await client.GetAsync("/api/orders?query=AbC");
        var nonMatchingResponse = await client.GetAsync("/api/orders?query=abc");

        // Assert
        using var matchingDocument = JsonDocument.Parse(await matchingResponse.Content.ReadAsStringAsync());
        using var nonMatchingDocument = JsonDocument.Parse(await nonMatchingResponse.Content.ReadAsStringAsync());
        matchingDocument.RootElement.GetProperty("items").GetArrayLength().Should().Be(1);
        nonMatchingDocument.RootElement.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void AddJsonResource_WithNonStringSearchPath_ThrowsClearException()
    {
        // Act
        var act = async () =>
        {
            using var host = await CreateNestedHost(services =>
            {
                services.AddJsonResource<JsonNestedOrder, Guid>(new RestLibJsonResourceConfiguration
                {
                    Name = "orders",
                    Route = "/api/orders",
                    AllowAnonymousAll = true,
                    Search = [nameof(JsonNestedOrder.Id)]
                });
            });
        };

        // Assert
        act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*must resolve to a string property*");
    }

    [Fact]
    public async Task AddJsonResource_WithFieldSelectionResponseNested_LoadsConfiguration()
    {
        // Arrange
        var filePath = CreateResourceFile(
            """
            {
              "Name": "orders",
              "Route": "/api/orders",
              "AllowAnonymousAll": true,
              "FieldSelection": {
                "Properties": ["OrderNumber", "Customer.Email"],
                "Response": "Nested"
              }
            }
            """);

        await using var cleanup = new TempPath(filePath);

        using var host = await CreateNestedHost(services =>
        {
            services.AddJsonResourceFromFile<JsonNestedOrder, Guid>(filePath);
        });

        var client = host.GetTestClient();
        var repository = host.Services.GetRequiredService<InMemoryRepository<JsonNestedOrder, Guid>>();
        repository.Clear();
        repository.Seed([CreateNestedOrder("A-100", "Adam", "adam@example.com")]);

        // Act
        var response = await client.GetAsync("/api/orders?fields=order_number,customer.email");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = document.RootElement.GetProperty("items")[0];
        item.GetProperty("order_number").GetString().Should().Be("A-100");
        item.GetProperty("customer").GetProperty("email").GetString().Should().Be("adam@example.com");
        item.TryGetProperty("customer.email", out _).Should().BeFalse();
    }

    [Fact]
    public void AddJsonResource_WithFieldSelectionResponseInvalid_ThrowsClearConfigurationError()
    {
        // Arrange
        var filePath = CreateResourceFile(
            """
            {
              "Name": "orders",
              "Route": "/api/orders",
              "FieldSelection": {
                "Properties": ["OrderNumber"],
                "Response": "Diagonal"
              }
            }
            """);

        // Act
        var act = async () =>
        {
            using var cleanup = new TempPath(filePath);
            using var host = await CreateNestedHost(services =>
            {
                services.AddJsonResourceFromFile<JsonNestedOrder, Guid>(filePath);
            });
        };

        // Assert
        act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*FieldSelection.Response*Diagonal*Flat*Nested*");
    }

    [Fact]
    public async Task AddJsonResource_WithLegacyFieldSelectionArray_DefaultsToFlatShape()
    {
        // Arrange
        using var host = await CreateNestedHost(services =>
        {
            services.AddJsonResource<JsonNestedOrder, Guid>(new RestLibJsonResourceConfiguration
            {
                Name = "orders",
                Route = "/api/orders",
                AllowAnonymousAll = true,
                FieldSelection =
                [
                    nameof(JsonNestedOrder.OrderNumber),
                    $"{nameof(JsonNestedOrder.Customer)}.{nameof(JsonNestedCustomer.Email)}"
                ]
            });
        });

        var client = host.GetTestClient();
        var repository = host.Services.GetRequiredService<InMemoryRepository<JsonNestedOrder, Guid>>();
        repository.Clear();
        repository.Seed([CreateNestedOrder("A-100", "Adam", "adam@example.com")]);

        // Act
        var response = await client.GetAsync("/api/orders?fields=order_number,customer.email");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = document.RootElement.GetProperty("items")[0];
        item.GetProperty("customer.email").GetString().Should().Be("adam@example.com");
        item.TryGetProperty("customer", out _).Should().BeFalse();
    }

    private static async Task<IHost> CreateHost(
        Action<IServiceCollection> configureServices,
        string? mapResourceName = null)
    {
        var builder = new TestJsonHostBuilder()
            .WithServices(services =>
            {
                services.AddSingleton<TestEntityRepository>();
                services.AddSingleton<IRepository<TestEntity, Guid>>(sp => sp.GetRequiredService<TestEntityRepository>());
                services.AddOpenApi();
                configureServices(services);
            })
            .WithAdditionalEndpoints(endpoints => endpoints.MapOpenApi());

        if (mapResourceName is not null)
        {
            builder.MapOnly(mapResourceName);
        }

        var (host, _) = await builder.BuildAsync();
        return host;
    }

    private static async Task<IHost> CreateCompositeHost(Action<IServiceCollection> configureServices)
    {
        var repository = new InMemoryRepository<JsonCompositeEntity, RestLibCompositeKey<Guid, string>>(
            static entity => new RestLibCompositeKey<Guid, string>(entity.TenantId, entity.Sku),
            static () => new RestLibCompositeKey<Guid, string>(Guid.NewGuid(), $"generated-{Guid.NewGuid():N}"),
            RestLibJsonOptions.CreateDefault());

        var builder = new TestJsonHostBuilder()
            .WithServices(services =>
            {
                services.AddSingleton(repository);
                services.AddSingleton<IRepository<JsonCompositeEntity, RestLibCompositeKey<Guid, string>>>(repository);
                services.AddOpenApi();
                configureServices(services);
            })
            .WithAdditionalEndpoints(endpoints => endpoints.MapOpenApi());

        var (host, _) = await builder.BuildAsync();
        return host;
    }

    private static async Task<IHost> CreateNestedHost(Action<IServiceCollection> configureServices)
    {
        var repository = new InMemoryRepository<JsonNestedOrder, Guid>(
            static entity => entity.Id,
            Guid.NewGuid,
            RestLibJsonOptions.CreateDefault());

        var builder = new TestJsonHostBuilder()
            .WithServices(services =>
            {
                services.AddSingleton(repository);
                services.AddSingleton<IRepository<JsonNestedOrder, Guid>>(repository);
                services.AddOpenApi();
                configureServices(services);
            })
            .WithAdditionalEndpoints(endpoints => endpoints.MapOpenApi());

        var (host, _) = await builder.BuildAsync();
        return host;
    }

    private static JsonCompositeEntity CreateCompositeEntity(
        Guid? tenantId = null,
        string sku = "sku-1",
        string productName = "Widget",
        decimal price = 10m)
    {
        return new JsonCompositeEntity
        {
            TenantId = tenantId ?? Guid.NewGuid(),
            Sku = sku,
            ProductName = productName,
            Price = price
        };
    }

    private static string GetCompositeItemPath(JsonCompositeEntity entity)
    {
        return $"/api/catalog-items/{entity.TenantId}/{entity.Sku}";
    }

    private static JsonNestedOrder CreateNestedOrder(
        string orderNumber,
        string customerName,
        string customerEmail)
    {
        return new JsonNestedOrder
        {
            Id = Guid.NewGuid(),
            OrderNumber = orderNumber,
            Customer = new JsonNestedCustomer
            {
                Name = customerName,
                Email = customerEmail
            },
            Items =
            [
                new JsonNestedLineItem
                {
                    Name = $"line-{orderNumber}"
                }
            ]
        };
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"restlib-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string CreateResourceFile(string json)
    {
        var directory = CreateTempDirectory();
        return CreateResourceFileInFolder(directory, "resource.json", json);
    }

    private static string CreateResourceFileInFolder(string folder, string fileName, string json)
    {
        var path = Path.Combine(folder, fileName);
        File.WriteAllText(path, json.Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private sealed class TempPath : IAsyncDisposable, IDisposable
    {
        private readonly string _path;

        public TempPath(string path)
        {
            _path = path;
        }

        public void Dispose()
        {
            DeletePath();
        }

        public ValueTask DisposeAsync()
        {
            DeletePath();
            return ValueTask.CompletedTask;
        }

        private void DeletePath()
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }

            var directory = Directory.Exists(_path) ? _path : Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}

internal sealed class JsonCompositeEntity
{
    public Guid TenantId { get; set; }

    public string Sku { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public decimal Price { get; set; }
}

internal sealed class JsonNestedOrder
{
    public Guid Id { get; set; }

    public string OrderNumber { get; set; } = string.Empty;

    public JsonNestedCustomer Customer { get; set; } = new();

    public List<JsonNestedLineItem> Items { get; set; } = [];
}

internal sealed class JsonNestedCustomer
{
    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
}

internal sealed class JsonNestedLineItem
{
    public string Name { get; set; } = string.Empty;
}
