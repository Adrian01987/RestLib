using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestLib.Abstractions;
using RestLib.Batch;
using RestLib.Configuration;
using RestLib.InMemory;
using RestLib.Responses;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

[Trait("Type", "Integration")]
[Trait("Feature", "Mapping")]
public class JsonTwoModelResourceTests
{
    [Fact]
    public async Task AddRestLibFromFolder_WithNamedMapper_MapsTwoModelResource()
    {
        // Arrange
        var folder = CreateTempDirectory();
        await using var cleanup = new TempPath(folder);
        _ = CreateResourceFileInFolder(folder, "items.json", BuildNamedMapperResourceJson("items", "/api/items"));

        var repository = CreateRepository<JsonMappedDbItem>(item => item.Id, Guid.NewGuid);
        var id = Guid.NewGuid();
        repository.Seed(
        [
            new JsonMappedDbItem
            {
                Id = id,
                Name = "Widget",
                Price = 12.5m,
                Category = "hardware",
                IsActive = true,
                InternalToken = "db-only"
            }
        ]);

        var (host, client) = await CreateHostAsync(services =>
        {
            services.AddSingleton(repository);
            services.AddSingleton<IRepository<JsonMappedDbItem, Guid>>(repository);
            services.AddRestLibMapper<JsonMappedApiItem, JsonMappedDbItem, JsonMappedNamedMapper>();
            services.AddRestLibFromFolder(folder);
        });
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.GetAsync($"/api/items/{id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("name").GetString().Should().Be("Widget");
        json.GetProperty("category").GetString().Should().Be("hardware");
        json.TryGetProperty("internal_token", out _).Should().BeFalse();
    }

    [Fact]
    public async Task AddRestLibFromFolder_WithAutoMapper_MapsTwoModelResource()
    {
        // Arrange
        var folder = CreateTempDirectory();
        await using var cleanup = new TempPath(folder);
        _ = CreateResourceFileInFolder(folder, "auto-items.json", BuildAutoMapperResourceJson("auto-items", "/api/auto-items"));

        var repository = CreateRepository<AutoMappedDbItem>(item => item.Id, Guid.NewGuid);

        var (host, client) = await CreateHostAsync(services =>
        {
            services.AddSingleton(repository);
            services.AddSingleton<IRepository<AutoMappedDbItem, Guid>>(repository);
            services.AddRestLibFromFolder(folder);
        });
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.PostAsJsonAsync("/api/auto-items", new { name = "Auto", quantity = 3 });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        created.GetProperty("name").GetString().Should().Be("Auto");
        created.GetProperty("quantity").GetInt32().Should().Be(3);

        var createdId = created.GetProperty("id").GetGuid();
        var stored = await repository.GetByIdAsync(createdId);
        stored.Should().NotBeNull();
        stored!.Name.Should().Be("Auto");
        stored.Quantity.Should().Be(3);
    }

    [Fact]
    public async Task AddRestLibFromFolder_WithMissingMapper_ThrowsClearStartupException()
    {
        // Arrange
        var folder = CreateTempDirectory();
        await using var cleanup = new TempPath(folder);
        _ = CreateResourceFileInFolder(folder, "items.json", BuildNamedMapperResourceJson("items", "/api/items"));

        var repository = CreateRepository<JsonMappedDbItem>(item => item.Id, Guid.NewGuid);
        var id = Guid.NewGuid();
        repository.Seed(
        [
            new JsonMappedDbItem
            {
                Id = id,
                Name = "Missing Mapper",
                Price = 1m,
                Category = "hardware",
                IsActive = true,
                InternalToken = "db-only"
            }
        ]);

        var (host, client) = await CreateHostAsync(services =>
        {
            services.AddSingleton(repository);
            services.AddSingleton<IRepository<JsonMappedDbItem, Guid>>(repository);
            services.AddRestLibFromFolder(folder);
        });
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var act = async () => await client.GetAsync($"/api/items/{id}");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*items*")
            .WithMessage("*JsonMappedApiItem*")
            .WithMessage("*JsonMappedDbItem*")
            .WithMessage("*JsonMappedNamedMapper*");
    }

    [Fact]
    public async Task AddJsonResourceFromFile_WithTwoModelResource_MapsConfiguredResource()
    {
        // Arrange
        var filePath = CreateResourceFile(BuildNamedMapperResourceJson("items", "/api/items"));
        await using var cleanup = new TempPath(filePath);

        var repository = CreateRepository<JsonMappedDbItem>(item => item.Id, Guid.NewGuid);
        var id = Guid.NewGuid();
        repository.Seed(
        [
            new JsonMappedDbItem
            {
                Id = id,
                Name = "From File",
                Price = 7.5m,
                Category = "hardware",
                IsActive = true,
                InternalToken = "db-only"
            }
        ]);

        var (host, client) = await CreateHostAsync(services =>
        {
            services.AddSingleton(repository);
            services.AddSingleton<IRepository<JsonMappedDbItem, Guid>>(repository);
            services.AddRestLibMapper<JsonMappedApiItem, JsonMappedDbItem, JsonMappedNamedMapper>();
            services.AddJsonResourceFromFile<JsonMappedApiItem, JsonMappedDbItem, Guid>(filePath);
        });
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.GetAsync($"/api/items/{id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("name").GetString().Should().Be("From File");
        json.TryGetProperty("internal_token", out _).Should().BeFalse();
    }

    [Fact]
    public async Task AddJsonResource_WithDbHookModel_RunsDbNamedHooks()
    {
        // Arrange
        var repository = CreateRepository<JsonMappedDbItem>(item => item.Id, Guid.NewGuid);
        object? capturedEntity = null;

        var (host, client) = await CreateHostAsync(services =>
        {
            services.AddSingleton(repository);
            services.AddSingleton<IRepository<JsonMappedDbItem, Guid>>(repository);
            services.AddRestLibMapper<JsonMappedApiItem, JsonMappedDbItem, JsonMappedNamedMapper>();
            services.AddNamedHook<JsonMappedDbItem, Guid>(HookNames.MarkDbModel, context =>
            {
                capturedEntity = context.Entity;
                if (context.Entity is not null)
                {
                    context.Entity.Name = "DB Hooked";
                    context.Entity.InternalToken = "db-hooked";
                }

                return Task.CompletedTask;
            });

            services.AddJsonResource<JsonMappedApiItem, JsonMappedDbItem, Guid>(new RestLibJsonResourceConfiguration
            {
                Name = "items",
                Route = "/api/items",
                AllowAnonymousAll = true,
                Mapping = new RestLibJsonMappingConfiguration
                {
                    DbType = typeof(JsonMappedDbItem).AssemblyQualifiedName,
                    Mapper = nameof(JsonMappedNamedMapper),
                    HookModel = "Db"
                },
                Hooks = new RestLibJsonHookConfiguration
                {
                    BeforePersist = new RestLibJsonHookStage
                    {
                        Default = [HookNames.MarkDbModel]
                    }
                }
            });
        });
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/items",
            new { name = "Original", price = 5m, category = "hardware", is_active = true });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        capturedEntity.Should().BeOfType<JsonMappedDbItem>();

        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        created.GetProperty("name").GetString().Should().Be("DB Hooked");

        var createdId = created.GetProperty("id").GetGuid();
        var stored = await repository.GetByIdAsync(createdId);
        stored.Should().NotBeNull();
        stored!.InternalToken.Should().Be("db-hooked");
    }

    [Fact]
    public async Task AddRestLibFromFolder_WithTwoModelBatch_MapsBatchUpdateUsingApiModels()
    {
        // Arrange
        var folder = CreateTempDirectory();
        await using var cleanup = new TempPath(folder);
        _ = CreateResourceFileInFolder(folder, "items.json", BuildNamedMapperBatchResourceJson("items", "/api/items"));

        var repository = CreateRepository<JsonMappedDbItem>(item => item.Id, Guid.NewGuid);
        var id = Guid.NewGuid();
        repository.Seed(
        [
            new JsonMappedDbItem
            {
                Id = id,
                Name = "Original",
                Price = 12.5m,
                Category = "hardware",
                IsActive = true,
                InternalToken = "db-only"
            }
        ]);

        var (host, client) = await CreateHostAsync(services =>
        {
            services.AddSingleton(repository);
            services.AddSingleton<IRepository<JsonMappedDbItem, Guid>>(repository);
            services.AddSingleton<IBatchRepository<JsonMappedDbItem, Guid>>(repository);
            services.AddRestLibMapper<JsonMappedApiItem, JsonMappedDbItem, JsonMappedNamedMapper>();
            services.AddRestLibFromFolder(folder);
        });
        using var hostHandle = host;
        using var clientHandle = client;

        var payload = new
        {
            action = "update",
            items = new object[]
            {
                new { id, body = new { name = "Updated", price = 20m, category = "hardware", is_active = true } }
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/items/batch", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var entity = json.GetProperty("items")[0].GetProperty("entity");
        entity.GetProperty("name").GetString().Should().Be("Updated");
        entity.TryGetProperty("internal_token", out _).Should().BeFalse();

        var stored = await repository.GetByIdAsync(id);
        stored.Should().NotBeNull();
        stored!.Name.Should().Be("Updated");
    }

    [Fact]
    public async Task AddJsonResource_WithDbHookModelAndBatchPatch_RevalidatesApiModelAfterDbHookMutation()
    {
        // Arrange
        var repository = CreateRepository<JsonMappedDbItem>(item => item.Id, Guid.NewGuid);
        var id = Guid.NewGuid();
        repository.Seed(
        [
            new JsonMappedDbItem
            {
                Id = id,
                Name = "Original",
                Price = 5m,
                Category = "hardware",
                IsActive = true,
                InternalToken = "db-only"
            }
        ]);

        var (host, client) = await CreateHostAsync(services =>
        {
            services.AddSingleton(repository);
            services.AddSingleton<IRepository<JsonMappedDbItem, Guid>>(repository);
            services.AddSingleton<IBatchRepository<JsonMappedDbItem, Guid>>(repository);
            services.AddRestLibMapper<JsonMappedApiItem, JsonMappedDbItem, JsonMappedNamedMapper>();
            services.AddNamedHook<JsonMappedDbItem, Guid>(HookNames.MarkDbModel, context =>
            {
                if (context.Entity is not null)
                {
                    context.Entity.Name = string.Empty;
                }

                return Task.CompletedTask;
            });

            services.AddJsonResource<JsonMappedApiItem, JsonMappedDbItem, Guid>(new RestLibJsonResourceConfiguration
            {
                Name = "items",
                Route = "/api/items",
                AllowAnonymousAll = true,
                Mapping = new RestLibJsonMappingConfiguration
                {
                    DbType = typeof(JsonMappedDbItem).AssemblyQualifiedName,
                    Mapper = nameof(JsonMappedNamedMapper),
                    HookModel = "Db"
                },
                Batch = new RestLibJsonBatchConfiguration
                {
                    Actions = [BatchAction.Patch]
                },
                Validation = new Dictionary<string, RestLibJsonValidationRuleConfiguration>(StringComparer.OrdinalIgnoreCase)
                {
                    [nameof(JsonMappedApiItem.Name)] = new RestLibJsonValidationRuleConfiguration
                    {
                        Required = true
                    }
                },
                Hooks = new RestLibJsonHookConfiguration
                {
                    OnRequestValidated = new RestLibJsonHookStage
                    {
                        Default = [HookNames.MarkDbModel]
                    }
                }
            });
        });
        using var hostHandle = host;
        using var clientHandle = client;

        var payload = new
        {
            action = "patch",
            items = new object[]
            {
                new { id, body = new { price = 25m } }
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/items/batch", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var item = json.GetProperty("items")[0];
        item.GetProperty("status").GetInt32().Should().Be(400);
        item.GetProperty("error").GetProperty("errors").TryGetProperty("name", out _).Should().BeTrue();

        var stored = await repository.GetByIdAsync(id);
        stored.Should().NotBeNull();
        stored!.Price.Should().Be(5m);
    }

    [Fact]
    public async Task AddJsonResource_WithInvalidMappingConfiguration_ThrowsOnMapping()
    {
        // Act
        var act = async () =>
        {
            _ = await CreateHostAsync(services =>
            {
                services.AddJsonResource<JsonMappedApiItem, JsonMappedDbItem, Guid>(new RestLibJsonResourceConfiguration
                {
                    Name = "items",
                    Route = "/api/items",
                    AllowAnonymousAll = true,
                    Mapping = new RestLibJsonMappingConfiguration
                    {
                        DbType = typeof(JsonMappedDbItem).AssemblyQualifiedName,
                        Mapper = nameof(JsonMappedNamedMapper),
                        Auto = true
                    }
                });
            });
        };

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*items*")
            .WithMessage("*Mapping.Mapper*")
            .WithMessage("*Mapping.Auto*");
    }

    [Fact]
    public async Task AddJsonResource_WithKeyPropertyAndBatchUpdate_PreservesCustomDbKey()
    {
        // Arrange
        var repository = CreateRepository<JsonMappedCustomKeyDbItem>(item => item.Code, Guid.NewGuid);
        var id = Guid.NewGuid();
        repository.Seed(
        [
            new JsonMappedCustomKeyDbItem
            {
                Code = id,
                Name = "Original",
                Price = 12.5m,
                Category = "hardware",
                IsActive = true,
                InternalToken = "db-only"
            }
        ]);

        var (host, client) = await CreateHostAsync(services =>
        {
            services.AddSingleton(repository);
            services.AddSingleton<IRepository<JsonMappedCustomKeyDbItem, Guid>>(repository);
            services.AddSingleton<IBatchRepository<JsonMappedCustomKeyDbItem, Guid>>(repository);
            services.AddRestLibMapper<JsonMappedCustomKeyApiItem, JsonMappedCustomKeyDbItem>(
                _ => new JsonMappedCustomKeyMapper());
            services.AddJsonResource<JsonMappedCustomKeyApiItem, JsonMappedCustomKeyDbItem, Guid>(new RestLibJsonResourceConfiguration
            {
                Name = "items",
                Route = "/api/items",
                AllowAnonymousAll = true,
                KeyProperty = nameof(JsonMappedCustomKeyApiItem.Code),
                Mapping = new RestLibJsonMappingConfiguration
                {
                    DbType = typeof(JsonMappedCustomKeyDbItem).AssemblyQualifiedName,
                    Auto = false
                },
                Batch = new RestLibJsonBatchConfiguration
                {
                    Actions = [BatchAction.Update, BatchAction.Patch]
                }
            });
        });
        using var hostHandle = host;
        using var clientHandle = client;

        var updatePayload = new
        {
            action = "update",
            items = new object[]
            {
                new { id, body = new { name = "Updated", price = 20m, category = "hardware", is_active = true } }
            }
        };

        // Act
        var updateResponse = await client.PostAsJsonAsync("/api/items/batch", updatePayload);

        // Assert
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await repository.GetByIdAsync(id);
        updated.Should().NotBeNull();
        updated!.Code.Should().Be(id);
        updated.Name.Should().Be("Updated");

        var patchPayload = new
        {
            action = "patch",
            items = new object[]
            {
                new { id, body = new { price = 30m } }
            }
        };

        // Act
        var patchResponse = await client.PostAsJsonAsync("/api/items/batch", patchPayload);

        // Assert
        patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var patched = await repository.GetByIdAsync(id);
        patched.Should().NotBeNull();
        patched!.Code.Should().Be(id);
        patched.Price.Should().Be(30m);
    }

    [Fact]
    public async Task AddJsonResource_WithDbHookModelAndBatchPatch_NonObjectPatchBodyReturnsBadRequest()
    {
        // Arrange
        var repository = CreateRepository<JsonMappedDbItem>(item => item.Id, Guid.NewGuid);
        var id = Guid.NewGuid();
        repository.Seed(
        [
            new JsonMappedDbItem
            {
                Id = id,
                Name = "Original",
                Price = 5m,
                Category = "hardware",
                IsActive = true,
                InternalToken = "db-only"
            }
        ]);

        var (host, client) = await CreateHostAsync(services =>
        {
            services.AddSingleton(repository);
            services.AddSingleton<IRepository<JsonMappedDbItem, Guid>>(repository);
            services.AddSingleton<IBatchRepository<JsonMappedDbItem, Guid>>(repository);
            services.AddRestLibMapper<JsonMappedApiItem, JsonMappedDbItem, JsonMappedNamedMapper>();
            services.AddJsonResource<JsonMappedApiItem, JsonMappedDbItem, Guid>(new RestLibJsonResourceConfiguration
            {
                Name = "items",
                Route = "/api/items",
                AllowAnonymousAll = true,
                Mapping = new RestLibJsonMappingConfiguration
                {
                    DbType = typeof(JsonMappedDbItem).AssemblyQualifiedName,
                    Mapper = nameof(JsonMappedNamedMapper),
                    HookModel = "Db"
                },
                Batch = new RestLibJsonBatchConfiguration
                {
                    Actions = [BatchAction.Patch]
                }
            });
        });
        using var hostHandle = host;
        using var clientHandle = client;

        using var content = new StringContent(
            $$"""
            {
              "action": "patch",
              "items": [
                {
                  "id": "{{id}}",
                  "body": []
                }
              ]
            }
            """,
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await client.PostAsync("/api/items/batch", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var item = json.GetProperty("items")[0];
        item.GetProperty("status").GetInt32().Should().Be(400);
        item.GetProperty("error").GetProperty("type").GetString().Should().Be(ProblemTypes.BadRequest);
    }

    [Fact]
    public async Task AddJsonResource_TwoModelWithoutMapping_ThrowsOnMapping()
    {
        // Act
        var act = async () =>
        {
            _ = await CreateHostAsync(services =>
            {
                services.AddJsonResource<JsonMappedApiItem, JsonMappedDbItem, Guid>(new RestLibJsonResourceConfiguration
                {
                    Name = "items",
                    Route = "/api/items",
                    AllowAnonymousAll = true
                });
            });
        };

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*items*")
            .WithMessage("*Mapping section*");
    }

    [Fact]
    public async Task AddJsonResource_TwoModelWithMismatchedDbType_ThrowsOnMapping()
    {
        // Act
        var act = async () =>
        {
            _ = await CreateHostAsync(services =>
            {
                services.AddJsonResource<JsonMappedApiItem, JsonMappedDbItem, Guid>(new RestLibJsonResourceConfiguration
                {
                    Name = "items",
                    Route = "/api/items",
                    AllowAnonymousAll = true,
                    Mapping = new RestLibJsonMappingConfiguration
                    {
                        DbType = typeof(AutoMappedDbItem).AssemblyQualifiedName,
                        Mapper = nameof(JsonMappedNamedMapper)
                    }
                });
            });
        };

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*items*")
            .WithMessage("*Mapping.DbType*")
            .WithMessage("*JsonMappedDbItem*");
    }

    private static async Task<(IHost Host, HttpClient Client)> CreateHostAsync(
        Action<IServiceCollection> configureServices)
    {
        return await new TestJsonHostBuilder()
            .WithServices(configureServices)
            .BuildAsync();
    }

    private static InMemoryRepository<TEntity, Guid> CreateRepository<TEntity>(
        Func<TEntity, Guid> keySelector,
        Func<Guid> keyGenerator)
        where TEntity : class
    {
        return new InMemoryRepository<TEntity, Guid>(keySelector, keyGenerator, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        });
    }

    private static string BuildNamedMapperResourceJson(string name, string route)
    {
        return $$"""
        {
          "Name": "{{name}}",
          "Route": "{{route}}",
          "AllowAnonymousAll": true,
          "EntityType": "{{typeof(JsonMappedApiItem).AssemblyQualifiedName}}",
          "Mapping": {
            "DbType": "{{typeof(JsonMappedDbItem).AssemblyQualifiedName}}",
            "Mapper": "{{nameof(JsonMappedNamedMapper)}}"
          }
        }
        """;
    }

    private static string BuildNamedMapperBatchResourceJson(string name, string route)
    {
        return $$"""
        {
          "Name": "{{name}}",
          "Route": "{{route}}",
          "AllowAnonymousAll": true,
          "EntityType": "{{typeof(JsonMappedApiItem).AssemblyQualifiedName}}",
          "Mapping": {
            "DbType": "{{typeof(JsonMappedDbItem).AssemblyQualifiedName}}",
            "Mapper": "{{nameof(JsonMappedNamedMapper)}}"
          },
          "Batch": {
            "Actions": ["Update", "Patch"]
          }
        }
        """;
    }

    private static string BuildAutoMapperResourceJson(string name, string route)
    {
        return $$"""
        {
          "Name": "{{name}}",
          "Route": "{{route}}",
          "AllowAnonymousAll": true,
          "EntityType": "{{typeof(AutoMappedApiItem).AssemblyQualifiedName}}",
          "Mapping": {
            "DbType": "{{typeof(AutoMappedDbItem).AssemblyQualifiedName}}",
            "Auto": true
          }
        }
        """;
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"restlib-json-two-model-tests-{Guid.NewGuid():N}");
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

    private static class HookNames
    {
        public const string MarkDbModel = nameof(MarkDbModel);
    }

    private sealed class JsonMappedApiItem
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public decimal Price { get; set; }

        public string Category { get; set; } = string.Empty;

        public bool IsActive { get; set; }
    }

    private sealed class JsonMappedDbItem
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public decimal Price { get; set; }

        public string Category { get; set; } = string.Empty;

        public bool IsActive { get; set; }

        public string InternalToken { get; set; } = string.Empty;
    }

    private sealed class JsonMappedCustomKeyApiItem
    {
        public Guid Code { get; set; }

        public string Name { get; set; } = string.Empty;

        public decimal Price { get; set; }

        public string Category { get; set; } = string.Empty;

        public bool IsActive { get; set; }
    }

    private sealed class JsonMappedCustomKeyDbItem
    {
        public Guid Code { get; set; }

        public string Name { get; set; } = string.Empty;

        public decimal Price { get; set; }

        public string Category { get; set; } = string.Empty;

        public bool IsActive { get; set; }

        public string InternalToken { get; set; } = string.Empty;
    }

    private sealed class JsonMappedNamedMapper : IRestLibMapper<JsonMappedApiItem, JsonMappedDbItem>
    {
        public JsonMappedApiItem ToApi(JsonMappedDbItem dbModel)
        {
            return new JsonMappedApiItem
            {
                Id = dbModel.Id,
                Name = dbModel.Name,
                Price = dbModel.Price,
                Category = dbModel.Category,
                IsActive = dbModel.IsActive
            };
        }

        public JsonMappedDbItem ToDb(JsonMappedApiItem apiModel)
        {
            return new JsonMappedDbItem
            {
                Id = apiModel.Id,
                Name = apiModel.Name,
                Price = apiModel.Price,
                Category = apiModel.Category,
                IsActive = apiModel.IsActive,
                InternalToken = "mapped-from-api"
            };
        }
    }

    private sealed class JsonMappedCustomKeyMapper : IRestLibMapper<JsonMappedCustomKeyApiItem, JsonMappedCustomKeyDbItem>
    {
        public JsonMappedCustomKeyApiItem ToApi(JsonMappedCustomKeyDbItem dbModel)
        {
            return new JsonMappedCustomKeyApiItem
            {
                Code = dbModel.Code,
                Name = dbModel.Name,
                Price = dbModel.Price,
                Category = dbModel.Category,
                IsActive = dbModel.IsActive
            };
        }

        public JsonMappedCustomKeyDbItem ToDb(JsonMappedCustomKeyApiItem apiModel)
        {
            return new JsonMappedCustomKeyDbItem
            {
                Code = apiModel.Code,
                Name = apiModel.Name,
                Price = apiModel.Price,
                Category = apiModel.Category,
                IsActive = apiModel.IsActive,
                InternalToken = "mapped-from-api"
            };
        }
    }

    private sealed class AutoMappedApiItem
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public int Quantity { get; set; }
    }

    private sealed class AutoMappedDbItem
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public int Quantity { get; set; }
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
