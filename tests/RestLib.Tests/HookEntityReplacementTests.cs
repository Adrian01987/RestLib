using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestLib.Abstractions;
using RestLib.Batch;
using RestLib.Configuration;
using RestLib.Hooks;
using RestLib.Hypermedia;
using RestLib.Mapping;
using RestLib.Pagination;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Verifies the effective-entity replacement contract across hook stages and model modes.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "Hooks")]
public class HookEntityReplacementTests
{
    [Fact]
    public async Task Create_ReplacementsAcrossStages_PersistAndShapeFinalResponse()
    {
        // Arrange
        var repository = new ReplacementRepository();
        var etagGenerator = new CapturingETagGenerator();
        var linkProvider = new CapturingLinkProvider();
        var beforeResponseInput = string.Empty;
        var beforeResponseId = 0;

        var (host, client) = await BuildSingleModelHostAsync(
            repository,
            config => config.UseHooks(hooks =>
            {
                hooks.OnRequestReceived = context =>
                {
                    context.Entity = NewEntity(name: "received");
                    return Task.CompletedTask;
                };
                hooks.OnRequestValidated = context =>
                {
                    context.Entity = NewEntity(name: "validated");
                    return Task.CompletedTask;
                };
                hooks.BeforePersist = context =>
                {
                    context.Entity = NewEntity(name: "persisted", marker: "before-persist");
                    return Task.CompletedTask;
                };
                hooks.AfterPersist = context =>
                {
                    context.Entity = NewEntity(id: 999, name: "after-persist");
                    return Task.CompletedTask;
                };
                hooks.BeforeResponse = context =>
                {
                    beforeResponseInput = context.Entity!.Name;
                    beforeResponseId = context.Entity.Id;
                    context.Entity = NewEntity(id: 998, name: "final-response");
                    return Task.CompletedTask;
                };
            }),
            etagGenerator,
            linkProvider);
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.PostAsJsonAsync("/api/items", NewEntity(name: "request"));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var stored = await repository.GetByIdAsync(1);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.ToString().Should().EndWith("/api/items/1");
        beforeResponseInput.Should().Be("after-persist");
        beforeResponseId.Should().Be(1);
        stored!.Name.Should().Be("persisted");
        stored.Marker.Should().Be("before-persist");
        body.GetProperty("id").GetInt32().Should().Be(1);
        body.GetProperty("name").GetString().Should().Be("final-response");
        etagGenerator.LastEntity.Should().BeOfType<ReplacementEntity>()
            .Which.Name.Should().Be("final-response");
        linkProvider.LastEntity!.Name.Should().Be("final-response");
        linkProvider.LastKey.Should().Be(1);
    }

    [Fact]
    public async Task Update_ReplacementsAcrossStages_PersistAndShapeFinalResponse()
    {
        // Arrange
        var repository = new ReplacementRepository();
        repository.Seed(NewEntity(id: 7, name: "original"));
        var etagGenerator = new CapturingETagGenerator();
        var linkProvider = new CapturingLinkProvider();
        var beforeResponseInput = string.Empty;
        var beforeResponseId = 0;

        var (host, client) = await BuildSingleModelHostAsync(
            repository,
            config => config.UseHooks(hooks =>
            {
                hooks.OnRequestValidated = context =>
                {
                    if (context.Operation == RestLibOperation.Update)
                    {
                        context.Entity = NewEntity(id: 700, name: "validated");
                    }

                    return Task.CompletedTask;
                };
                hooks.BeforePersist = context =>
                {
                    if (context.Operation == RestLibOperation.Update)
                    {
                        context.Entity = NewEntity(id: 701, name: "persisted", marker: "updated");
                    }

                    return Task.CompletedTask;
                };
                hooks.AfterPersist = context =>
                {
                    if (context.Operation == RestLibOperation.Update)
                    {
                        context.Entity = NewEntity(id: 702, name: "after-persist");
                    }

                    return Task.CompletedTask;
                };
                hooks.BeforeResponse = context =>
                {
                    if (context.Operation == RestLibOperation.Update)
                    {
                        beforeResponseInput = context.Entity!.Name;
                        beforeResponseId = context.Entity.Id;
                        context.Entity = NewEntity(id: 703, name: "final-response");
                    }

                    return Task.CompletedTask;
                };
            }),
            etagGenerator,
            linkProvider);
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.PutAsJsonAsync("/api/items/7", NewEntity(name: "request"));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var stored = await repository.GetByIdAsync(7);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        beforeResponseInput.Should().Be("after-persist");
        beforeResponseId.Should().Be(7);
        stored!.Id.Should().Be(7);
        stored.Name.Should().Be("persisted");
        stored.Marker.Should().Be("updated");
        body.GetProperty("id").GetInt32().Should().Be(7);
        body.GetProperty("name").GetString().Should().Be("final-response");
        etagGenerator.LastEntity.Should().BeOfType<ReplacementEntity>()
            .Which.Name.Should().Be("final-response");
        linkProvider.LastEntity!.Name.Should().Be("final-response");
    }

    [Fact]
    public async Task Patch_PrePersistReplacement_IsStoredAndPostPersistReplacementIsReturned()
    {
        // Arrange
        var repository = new ReplacementRepository();
        repository.Seed(NewEntity(id: 11, name: "original", marker: "original"));
        var beforePersistInput = string.Empty;

        var (host, client) = await BuildSingleModelHostAsync(repository, config => config.UseHooks(hooks =>
        {
            hooks.OnRequestValidated = context =>
            {
                if (context.Operation == RestLibOperation.Patch)
                {
                    context.Entity = NewEntity(id: 110, name: "validated", marker: "validated");
                }

                return Task.CompletedTask;
            };
            hooks.BeforePersist = context =>
            {
                if (context.Operation == RestLibOperation.Patch)
                {
                    beforePersistInput = context.Entity!.Name;
                    context.Entity = NewEntity(id: 111, name: "persisted", marker: "replacement");
                }

                return Task.CompletedTask;
            };
            hooks.AfterPersist = context =>
            {
                if (context.Operation == RestLibOperation.Patch)
                {
                    context.Entity = NewEntity(id: 112, name: "after-persist");
                }

                return Task.CompletedTask;
            };
            hooks.BeforeResponse = context =>
            {
                if (context.Operation == RestLibOperation.Patch)
                {
                    context.Entity = NewEntity(id: 113, name: "final-response");
                }

                return Task.CompletedTask;
            };
        }));
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/items/11")
        {
            Content = JsonContent.Create(new { name = "from-document" })
        };
        var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var stored = await repository.GetByIdAsync(11);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        beforePersistInput.Should().Be("validated");
        repository.UpdateCallCount.Should().Be(1);
        repository.PatchCallCount.Should().Be(0);
        stored!.Id.Should().Be(11);
        stored.Name.Should().Be("persisted");
        stored.Marker.Should().Be("replacement");
        body.GetProperty("id").GetInt32().Should().Be(11);
        body.GetProperty("name").GetString().Should().Be("final-response");
    }

    [Fact]
    public async Task GetById_ResponseReplacements_DriveETagAndHateoasWithoutChangingStorage()
    {
        // Arrange
        var repository = new ReplacementRepository();
        repository.Seed(NewEntity(id: 5, name: "stored"));
        var etagGenerator = new CapturingETagGenerator();
        var linkProvider = new CapturingLinkProvider();
        var beforeResponseInput = string.Empty;
        var beforeResponseId = 0;

        var (host, client) = await BuildSingleModelHostAsync(
            repository,
            config => config.UseHooks(hooks =>
            {
                hooks.OnRequestValidated = context =>
                {
                    if (context.Operation == RestLibOperation.GetById)
                    {
                        context.Entity = NewEntity(id: 500, name: "validated");
                    }

                    return Task.CompletedTask;
                };
                hooks.BeforeResponse = context =>
                {
                    if (context.Operation == RestLibOperation.GetById)
                    {
                        beforeResponseInput = context.Entity!.Name;
                        beforeResponseId = context.Entity.Id;
                        context.Entity = NewEntity(id: 501, name: "final-response");
                    }

                    return Task.CompletedTask;
                };
            }),
            etagGenerator,
            linkProvider);
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.GetAsync("/api/items/5");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var stored = await repository.GetByIdAsync(5);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        beforeResponseInput.Should().Be("validated");
        beforeResponseId.Should().Be(5);
        stored!.Name.Should().Be("stored");
        body.GetProperty("id").GetInt32().Should().Be(5);
        body.GetProperty("name").GetString().Should().Be("final-response");
        etagGenerator.LastEntity.Should().BeOfType<ReplacementEntity>()
            .Which.Name.Should().Be("final-response");
        linkProvider.LastEntity!.Name.Should().Be("final-response");
        linkProvider.LastKey.Should().Be(5);
    }

    [Fact]
    public async Task Delete_ReplacementsFlowThroughEveryEntityBearingStage()
    {
        // Arrange
        var repository = new ReplacementRepository();
        repository.Seed(NewEntity(id: 9, name: "stored"));
        var stageInputs = new List<string>();
        var stageIds = new List<int>();

        var (host, client) = await BuildSingleModelHostAsync(repository, config => config.UseHooks(hooks =>
        {
            hooks.OnRequestValidated = context =>
            {
                if (context.Operation == RestLibOperation.Delete)
                {
                    stageInputs.Add(context.Entity!.Name);
                    stageIds.Add(context.Entity.Id);
                    context.Entity = NewEntity(id: 90, name: "validated");
                }

                return Task.CompletedTask;
            };
            hooks.BeforePersist = context =>
            {
                if (context.Operation == RestLibOperation.Delete)
                {
                    stageInputs.Add(context.Entity!.Name);
                    stageIds.Add(context.Entity.Id);
                    context.Entity = NewEntity(id: 91, name: "before-persist");
                }

                return Task.CompletedTask;
            };
            hooks.AfterPersist = context =>
            {
                if (context.Operation == RestLibOperation.Delete)
                {
                    stageInputs.Add(context.Entity!.Name);
                    stageIds.Add(context.Entity.Id);
                    context.Entity = NewEntity(id: 92, name: "after-persist");
                }

                return Task.CompletedTask;
            };
            hooks.BeforeResponse = context =>
            {
                if (context.Operation == RestLibOperation.Delete)
                {
                    stageInputs.Add(context.Entity!.Name);
                    stageIds.Add(context.Entity.Id);
                    context.Entity = NewEntity(id: 93, name: "before-response");
                }

                return Task.CompletedTask;
            };
        }));
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.DeleteAsync("/api/items/9");
        var stored = await repository.GetByIdAsync(9);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        stored.Should().BeNull();
        stageInputs.Should().Equal("stored", "validated", "before-persist", "after-persist");
        stageIds.Should().Equal(9, 9, 9, 9);
    }

    [Fact]
    public async Task MappedCreate_ApiModelReplacementsPersistAndReachFinalResponse()
    {
        // Arrange
        var repository = new MappedRepository();
        var etagGenerator = new CapturingETagGenerator();
        var beforeResponseInput = string.Empty;
        var beforeResponseId = 0;
        var (host, client) = await BuildMappedHostAsync(
            repository,
            config => config.UseHooks(hooks =>
            {
                hooks.BeforePersist = context =>
                {
                    context.Entity = NewApi(name: "persisted-api", marker: "mapped-before");
                    return Task.CompletedTask;
                };
                hooks.AfterPersist = context =>
                {
                    context.Entity = NewApi(id: 999, name: "after-api");
                    return Task.CompletedTask;
                };
                hooks.BeforeResponse = context =>
                {
                    beforeResponseInput = context.Entity!.Name;
                    beforeResponseId = context.Entity.Id;
                    context.Entity = NewApi(id: 998, name: "final-api");
                    return Task.CompletedTask;
                };
            }),
            etagGenerator);
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.PostAsJsonAsync("/api/mapped", NewApi(name: "request"));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var stored = await repository.GetByIdAsync(1);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        beforeResponseInput.Should().Be("after-api");
        beforeResponseId.Should().Be(1);
        stored!.Name.Should().Be("persisted-api");
        stored.Marker.Should().Be("mapped-before");
        body.GetProperty("id").GetInt32().Should().Be(1);
        body.GetProperty("name").GetString().Should().Be("final-api");
        etagGenerator.LastEntity.Should().BeOfType<ApiEntity>().Which.Name.Should().Be("final-api");
    }

    [Fact]
    public async Task MappedUpdate_DbModelReplacementsPersistAndReachFinalResponse()
    {
        // Arrange
        var repository = new MappedRepository();
        repository.Seed(NewDb(id: 3, name: "stored", marker: "original", internalValue: "original"));
        var etagGenerator = new CapturingETagGenerator();
        var beforeResponseInput = string.Empty;
        var beforeResponseId = 0;
        var (host, client) = await BuildMappedHostAsync(
            repository,
            config => config.UseDbModelHooks(hooks =>
            {
                hooks.BeforePersist = context =>
                {
                    if (context.Operation == RestLibOperation.Update)
                    {
                        context.Entity = NewDb(
                            id: 300,
                            name: "persisted-db",
                            marker: "db-before",
                            internalValue: "private-before");
                    }

                    return Task.CompletedTask;
                };
                hooks.AfterPersist = context =>
                {
                    if (context.Operation == RestLibOperation.Update)
                    {
                        context.Entity = NewDb(id: 301, name: "after-db", internalValue: "private-after");
                    }

                    return Task.CompletedTask;
                };
                hooks.BeforeResponse = context =>
                {
                    if (context.Operation == RestLibOperation.Update)
                    {
                        beforeResponseInput = context.Entity!.Name;
                        beforeResponseId = context.Entity.Id;
                        context.Entity = NewDb(id: 302, name: "final-db", internalValue: "private-final");
                    }

                    return Task.CompletedTask;
                };
            }),
            etagGenerator);
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.PutAsJsonAsync("/api/mapped/3", NewApi(name: "request"));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var stored = await repository.GetByIdAsync(3);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        beforeResponseInput.Should().Be("after-db");
        beforeResponseId.Should().Be(3);
        stored!.Id.Should().Be(3);
        stored.Name.Should().Be("persisted-db");
        stored.Marker.Should().Be("db-before");
        stored.InternalValue.Should().Be("private-before");
        body.GetProperty("id").GetInt32().Should().Be(3);
        body.GetProperty("name").GetString().Should().Be("final-db");
        etagGenerator.LastEntity.Should().BeOfType<ApiEntity>().Which.Name.Should().Be("final-db");
    }

    [Fact]
    public async Task MappedGetById_DbModelReplacementDrivesResponseAndETag()
    {
        // Arrange
        var repository = new MappedRepository();
        repository.Seed(NewDb(id: 4, name: "stored", internalValue: "private"));
        var etagGenerator = new CapturingETagGenerator();
        var beforeResponseInput = string.Empty;
        var beforeResponseId = 0;
        var (host, client) = await BuildMappedHostAsync(
            repository,
            config => config.UseDbModelHooks(hooks =>
            {
                hooks.OnRequestValidated = context =>
                {
                    if (context.Operation == RestLibOperation.GetById)
                    {
                        context.Entity = NewDb(id: 400, name: "validated-db");
                    }

                    return Task.CompletedTask;
                };
                hooks.BeforeResponse = context =>
                {
                    if (context.Operation == RestLibOperation.GetById)
                    {
                        beforeResponseInput = context.Entity!.Name;
                        beforeResponseId = context.Entity.Id;
                        context.Entity = NewDb(id: 401, name: "final-db");
                    }

                    return Task.CompletedTask;
                };
            }),
            etagGenerator);
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.GetAsync("/api/mapped/4");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        beforeResponseInput.Should().Be("validated-db");
        beforeResponseId.Should().Be(4);
        body.GetProperty("id").GetInt32().Should().Be(4);
        body.GetProperty("name").GetString().Should().Be("final-db");
        etagGenerator.LastEntity.Should().BeOfType<ApiEntity>().Which.Name.Should().Be("final-db");
    }

    [Fact]
    public async Task MappedPatch_ApiModelReplacementsPersistAndReachFinalResponse()
    {
        // Arrange
        var repository = new MappedRepository();
        repository.Seed(NewDb(id: 6, name: "stored", marker: "original", internalValue: "private"));
        var etagGenerator = new CapturingETagGenerator();
        var beforeResponseId = 0;
        var (host, client) = await BuildMappedHostAsync(
            repository,
            config => config.UseHooks(hooks =>
            {
                hooks.BeforePersist = context =>
                {
                    if (context.Operation == RestLibOperation.Patch)
                    {
                        context.Entity = NewApi(id: 600, name: "persisted-api", marker: "patch-replacement");
                    }

                    return Task.CompletedTask;
                };
                hooks.AfterPersist = context =>
                {
                    if (context.Operation == RestLibOperation.Patch)
                    {
                        context.Entity = NewApi(id: 601, name: "after-api");
                    }

                    return Task.CompletedTask;
                };
                hooks.BeforeResponse = context =>
                {
                    if (context.Operation == RestLibOperation.Patch)
                    {
                        beforeResponseId = context.Entity!.Id;
                        context.Entity = NewApi(id: 602, name: "final-api");
                    }

                    return Task.CompletedTask;
                };
            }),
            etagGenerator);
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/mapped/6")
        {
            Content = JsonContent.Create(new { name = "from-document" })
        };
        var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var stored = await repository.GetByIdAsync(6);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        stored!.Id.Should().Be(6);
        stored.Name.Should().Be("persisted-api");
        stored.Marker.Should().Be("patch-replacement");
        beforeResponseId.Should().Be(6);
        body.GetProperty("id").GetInt32().Should().Be(6);
        body.GetProperty("name").GetString().Should().Be("final-api");
        etagGenerator.LastEntity.Should().BeOfType<ApiEntity>().Which.Name.Should().Be("final-api");
    }

    [Fact]
    public async Task MappedDelete_ApiModelReplacementsFlowThroughEntityBearingStages()
    {
        // Arrange
        var repository = new MappedRepository();
        repository.Seed(NewDb(id: 8, name: "stored", internalValue: "private"));
        var etagGenerator = new CapturingETagGenerator();
        var stageInputs = new List<string>();
        var stageIds = new List<int>();
        var (host, client) = await BuildMappedHostAsync(
            repository,
            config => config.UseHooks(hooks =>
            {
                hooks.OnRequestValidated = context =>
                {
                    if (context.Operation == RestLibOperation.Delete)
                    {
                        stageInputs.Add(context.Entity!.Name);
                        stageIds.Add(context.Entity.Id);
                        context.Entity = NewApi(id: 800, name: "validated-api");
                    }

                    return Task.CompletedTask;
                };
                hooks.BeforePersist = context =>
                {
                    if (context.Operation == RestLibOperation.Delete)
                    {
                        stageInputs.Add(context.Entity!.Name);
                        stageIds.Add(context.Entity.Id);
                        context.Entity = NewApi(id: 801, name: "before-api");
                    }

                    return Task.CompletedTask;
                };
                hooks.AfterPersist = context =>
                {
                    if (context.Operation == RestLibOperation.Delete)
                    {
                        stageInputs.Add(context.Entity!.Name);
                        stageIds.Add(context.Entity.Id);
                        context.Entity = NewApi(id: 802, name: "after-api");
                    }

                    return Task.CompletedTask;
                };
                hooks.BeforeResponse = context =>
                {
                    if (context.Operation == RestLibOperation.Delete)
                    {
                        stageInputs.Add(context.Entity!.Name);
                        stageIds.Add(context.Entity.Id);
                    }

                    return Task.CompletedTask;
                };
            }),
            etagGenerator);
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.DeleteAsync("/api/mapped/8");
        var stored = await repository.GetByIdAsync(8);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        stored.Should().BeNull();
        stageInputs.Should().Equal("stored", "validated-api", "before-api", "after-api");
        stageIds.Should().Equal(8, 8, 8, 8);
    }

    [Fact]
    public async Task BatchCreate_AfterPersistReplacement_IsReturnedWithPersistedIdentity()
    {
        // Arrange
        var repository = new ReplacementRepository();
        var (host, client) = await BuildSingleModelHostAsync(repository, config =>
        {
            config.EnableBatch(BatchAction.Create);
            config.UseHooks(hooks =>
            {
                hooks.BeforePersist = context =>
                {
                    if (context.Operation == RestLibOperation.BatchCreate)
                    {
                        context.Entity = NewEntity(name: "batch-persisted");
                    }

                    return Task.CompletedTask;
                };
                hooks.AfterPersist = context =>
                {
                    if (context.Operation == RestLibOperation.BatchCreate)
                    {
                        context.Entity = NewEntity(id: 999, name: "batch-response");
                    }

                    return Task.CompletedTask;
                };
            });
        });
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.PostAsJsonAsync("/api/items/batch", new
        {
            action = "create",
            items = new[] { new { name = "request" } }
        });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var stored = await repository.GetByIdAsync(1);
        var resultEntity = body.GetProperty("items")[0].GetProperty("entity");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        stored!.Name.Should().Be("batch-persisted");
        resultEntity.GetProperty("id").GetInt32().Should().Be(1);
        resultEntity.GetProperty("name").GetString().Should().Be("batch-response");
    }

    [Fact]
    public async Task BatchPatch_PrePersistReplacement_IsStoredThroughUpdateContract()
    {
        // Arrange
        var repository = new ReplacementRepository();
        repository.Seed(NewEntity(id: 13, name: "stored", marker: "original"));
        var (host, client) = await BuildSingleModelHostAsync(repository, config =>
        {
            config.EnableBatch(BatchAction.Patch);
            config.UseHooks(hooks =>
            {
                hooks.OnRequestValidated = context =>
                {
                    if (context.Operation == RestLibOperation.BatchPatch)
                    {
                        context.Entity = NewEntity(id: 130, name: "batch-validated", marker: "validated");
                    }

                    return Task.CompletedTask;
                };
                hooks.BeforePersist = context =>
                {
                    if (context.Operation == RestLibOperation.BatchPatch)
                    {
                        context.Entity!.Name.Should().Be("batch-validated");
                        context.Entity = NewEntity(id: 131, name: "batch-persisted", marker: "replacement");
                    }

                    return Task.CompletedTask;
                };
            });
        });
        using var hostHandle = host;
        using var clientHandle = client;

        // Act
        var response = await client.PostAsJsonAsync("/api/items/batch", new
        {
            action = "patch",
            items = new[] { new { id = 13, body = new { name = "from-document" } } }
        });
        var stored = await repository.GetByIdAsync(13);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        repository.UpdateCallCount.Should().Be(1);
        repository.PatchCallCount.Should().Be(0);
        stored!.Id.Should().Be(13);
        stored.Name.Should().Be("batch-persisted");
        stored.Marker.Should().Be("replacement");
    }

    private static async Task<(Microsoft.Extensions.Hosting.IHost Host, HttpClient Client)> BuildSingleModelHostAsync(
        ReplacementRepository repository,
        Action<RestLibEndpointConfiguration<ReplacementEntity, int>> configureEndpoint,
        IETagGenerator? etagGenerator = null,
        IHateoasLinkProvider<ReplacementEntity, int>? linkProvider = null)
    {
        return await new TestHostBuilder<ReplacementEntity, int>(repository, "/api/items")
            .WithOptions(options =>
            {
                options.EnableETagSupport = etagGenerator is not null;
                options.EnableHateoas = linkProvider is not null;
            })
            .WithServices(services =>
            {
                if (etagGenerator is not null)
                {
                    services.AddSingleton<IETagGenerator>(etagGenerator);
                }

                if (linkProvider is not null)
                {
                    services.AddSingleton<IHateoasLinkProvider<ReplacementEntity, int>>(linkProvider);
                }
            })
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                configureEndpoint(config);
            })
            .BuildAsync();
    }

    private static async Task<(Microsoft.Extensions.Hosting.IHost Host, HttpClient Client)> BuildMappedHostAsync(
        MappedRepository repository,
        Action<RestLibEndpointConfiguration<ApiEntity, DbEntity, int>> configureEndpoint,
        IETagGenerator etagGenerator)
    {
        return await new TestTwoModelHostBuilder<ApiEntity, DbEntity, int>(repository, "/api/mapped")
            .WithOptions(options => options.EnableETagSupport = true)
            .WithServices(services =>
            {
                services.AddRestLibMapper<ApiEntity, DbEntity>(_ => new ReplacementMapper());
                services.AddSingleton<IETagGenerator>(etagGenerator);
            })
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                configureEndpoint(config);
            })
            .BuildAsync();
    }

    private static ReplacementEntity NewEntity(
        int id = 0,
        string name = "entity",
        string? marker = null)
    {
        return new ReplacementEntity { Id = id, Name = name, Marker = marker };
    }

    private static ApiEntity NewApi(int id = 0, string name = "api", string? marker = null)
    {
        return new ApiEntity { Id = id, Name = name, Marker = marker };
    }

    private static DbEntity NewDb(
        int id = 0,
        string name = "db",
        string? marker = null,
        string internalValue = "internal")
    {
        return new DbEntity
        {
            Id = id,
            Name = name,
            Marker = marker,
            InternalValue = internalValue
        };
    }

    private sealed class ReplacementEntity
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? Marker { get; set; }
    }

    private sealed class ApiEntity
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? Marker { get; set; }
    }

    private sealed class DbEntity
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? Marker { get; set; }

        public string InternalValue { get; set; } = string.Empty;
    }

    private sealed class ReplacementMapper : IRestLibMapper<ApiEntity, DbEntity>
    {
        public ApiEntity ToApi(DbEntity dbModel)
        {
            return NewApi(dbModel.Id, dbModel.Name, dbModel.Marker);
        }

        public DbEntity ToDb(ApiEntity apiModel)
        {
            return NewDb(apiModel.Id, apiModel.Name, apiModel.Marker, "mapped");
        }
    }

    private sealed class ReplacementRepository : IRepository<ReplacementEntity, int>
    {
        private readonly Dictionary<int, ReplacementEntity> _entities = [];
        private int _nextId = 1;

        public int PatchCallCount { get; private set; }

        public int UpdateCallCount { get; private set; }

        public Task<ReplacementEntity> CreateAsync(ReplacementEntity entity, CancellationToken ct = default)
        {
            var created = Clone(entity);
            if (created.Id == 0) created.Id = _nextId++;
            _entities[created.Id] = Clone(created);
            return Task.FromResult(Clone(created));
        }

        public Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            return Task.FromResult(_entities.Remove(id));
        }

        public Task<PagedResult<ReplacementEntity>> GetAllAsync(
            PaginationRequest pagination,
            CancellationToken ct = default)
        {
            return Task.FromResult(new PagedResult<ReplacementEntity>
            {
                Items = _entities.Values.Select(Clone).ToList()
            });
        }

        public Task<ReplacementEntity?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            return Task.FromResult(_entities.TryGetValue(id, out var entity) ? Clone(entity) : null);
        }

        public Task<ReplacementEntity?> PatchAsync(
            int id,
            JsonElement patchDocument,
            CancellationToken ct = default)
        {
            PatchCallCount++;
            if (!_entities.TryGetValue(id, out var entity)) return Task.FromResult<ReplacementEntity?>(null);

            var patched = Clone(entity);
            if (patchDocument.TryGetProperty("name", out var name)) patched.Name = name.GetString()!;
            if (patchDocument.TryGetProperty("marker", out var marker)) patched.Marker = marker.GetString();
            _entities[id] = Clone(patched);
            return Task.FromResult<ReplacementEntity?>(Clone(patched));
        }

        public void Seed(params ReplacementEntity[] entities)
        {
            foreach (var entity in entities)
            {
                _entities[entity.Id] = Clone(entity);
                _nextId = Math.Max(_nextId, entity.Id + 1);
            }
        }

        public Task<ReplacementEntity?> UpdateAsync(
            int id,
            ReplacementEntity entity,
            CancellationToken ct = default)
        {
            UpdateCallCount++;
            if (!_entities.ContainsKey(id)) return Task.FromResult<ReplacementEntity?>(null);

            var updated = Clone(entity);
            updated.Id = id;
            _entities[id] = Clone(updated);
            return Task.FromResult<ReplacementEntity?>(Clone(updated));
        }

        private static ReplacementEntity Clone(ReplacementEntity entity)
        {
            return NewEntity(entity.Id, entity.Name, entity.Marker);
        }
    }

    private sealed class MappedRepository : IRepository<DbEntity, int>
    {
        private readonly Dictionary<int, DbEntity> _entities = [];
        private int _nextId = 1;

        public Task<DbEntity> CreateAsync(DbEntity entity, CancellationToken ct = default)
        {
            var created = Clone(entity);
            if (created.Id == 0) created.Id = _nextId++;
            _entities[created.Id] = Clone(created);
            return Task.FromResult(Clone(created));
        }

        public Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            return Task.FromResult(_entities.Remove(id));
        }

        public Task<PagedResult<DbEntity>> GetAllAsync(PaginationRequest pagination, CancellationToken ct = default)
        {
            return Task.FromResult(new PagedResult<DbEntity>
            {
                Items = _entities.Values.Select(Clone).ToList()
            });
        }

        public Task<DbEntity?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            return Task.FromResult(_entities.TryGetValue(id, out var entity) ? Clone(entity) : null);
        }

        public Task<DbEntity?> PatchAsync(int id, JsonElement patchDocument, CancellationToken ct = default)
        {
            if (!_entities.TryGetValue(id, out var entity)) return Task.FromResult<DbEntity?>(null);
            var patched = Clone(entity);
            if (patchDocument.TryGetProperty("name", out var name)) patched.Name = name.GetString()!;
            _entities[id] = Clone(patched);
            return Task.FromResult<DbEntity?>(Clone(patched));
        }

        public void Seed(params DbEntity[] entities)
        {
            foreach (var entity in entities)
            {
                _entities[entity.Id] = Clone(entity);
                _nextId = Math.Max(_nextId, entity.Id + 1);
            }
        }

        public Task<DbEntity?> UpdateAsync(int id, DbEntity entity, CancellationToken ct = default)
        {
            if (!_entities.ContainsKey(id)) return Task.FromResult<DbEntity?>(null);
            var updated = Clone(entity);
            updated.Id = id;
            _entities[id] = Clone(updated);
            return Task.FromResult<DbEntity?>(Clone(updated));
        }

        private static DbEntity Clone(DbEntity entity)
        {
            return NewDb(entity.Id, entity.Name, entity.Marker, entity.InternalValue);
        }
    }

    private sealed class CapturingETagGenerator : IETagGenerator
    {
        public object? LastEntity { get; private set; }

        public string Generate<TEntity>(TEntity entity)
            where TEntity : class
        {
            LastEntity = entity;
            return "\"replacement-etag\"";
        }

        public bool Validate<TEntity>(TEntity entity, string etag)
            where TEntity : class
        {
            return Generate(entity) == etag;
        }
    }

    private sealed class CapturingLinkProvider : IHateoasLinkProvider<ReplacementEntity, int>
    {
        public ReplacementEntity? LastEntity { get; private set; }

        public int LastKey { get; private set; }

        public IReadOnlyDictionary<string, HateoasLink>? GetLinks(ReplacementEntity entity, int key)
        {
            LastEntity = entity;
            LastKey = key;
            return new Dictionary<string, HateoasLink>
            {
                ["observed"] = new() { Href = $"/observed/{entity.Name}" }
            };
        }
    }
}
