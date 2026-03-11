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
using Microsoft.OpenApi.Readers;
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
    }, mapAll: true);

    var client = host.GetTestClient();
    var repository = host.Services.GetRequiredService<TestEntityRepository>();
    var id = Guid.NewGuid();
    repository.Seed(new TestEntity { Id = id, Name = "alpha", Price = 10m });

    var getAll = await client.GetAsync("/api/items?name=alpha");
    var getById = await client.GetAsync($"/api/items/{id}");
    var create = await client.PostAsJsonAsync("/api/items", new TestEntity { Name = "beta", Price = 5m });
    var delete = await client.DeleteAsync($"/api/items/{id}");

    getAll.StatusCode.Should().Be(HttpStatusCode.OK);
    getById.StatusCode.Should().Be(HttpStatusCode.OK);
    create.StatusCode.Should().Be(HttpStatusCode.Created);
    delete.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);

    var openApi = await client.GetStringAsync("/swagger/v1/swagger.json");
    var document = new OpenApiStringReader().Read(openApi, out _);

    document.Paths["/api/items"].Operations[Microsoft.OpenApi.Models.OperationType.Get].Summary
        .Should().Be("List configured items");
    document.Paths["/api/items"].Operations[Microsoft.OpenApi.Models.OperationType.Get].Tags
        .Should().Contain(tag => tag.Name == "Items");
  }

  [Fact]
  public async Task AddJsonResource_FromConfigurationSection_LoadsResourceDefinition()
  {
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
    }, mapAll: true);

    var client = host.GetTestClient();
    var repository = host.Services.GetRequiredService<TestEntityRepository>();
    var id = Guid.NewGuid();
    repository.Seed(new TestEntity { Id = id, Name = "configured", Price = 5m });

    var getAll = await client.GetAsync("/api/items");
    var delete = await client.DeleteAsync($"/api/items/{id}");

    getAll.StatusCode.Should().Be(HttpStatusCode.OK);
    delete.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
  }

  [Fact]
  public async Task AddJsonResource_WithNamedHooks_RunsConfiguredHooksPerOperation()
  {
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
    }, mapAll: true);

    var client = host.GetTestClient();
    var repository = host.Services.GetRequiredService<TestEntityRepository>();

    var createResponse = await client.PostAsJsonAsync("/api/items", new TestEntity { Name = "first", Price = 1m });
    var created = await createResponse.Content.ReadFromJsonAsync<TestEntity>();

    created.Should().NotBeNull();
    created!.Name.Should().Be("created:first");

    var updateResponse = await client.PutAsJsonAsync($"/api/items/{created.Id}", new TestEntity { Name = "second", Price = 2m });
    var updated = await updateResponse.Content.ReadFromJsonAsync<TestEntity>();

    updated.Should().NotBeNull();
    updated!.Name.Should().Be("updated:second");

    var persisted = await repository.GetByIdAsync(created.Id);
    persisted.Should().NotBeNull();
    persisted!.Name.Should().Be("updated:second");
  }

  private static async Task<IHost> CreateHost(Action<IServiceCollection> configureServices, bool mapAll)
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
                services.AddEndpointsApiExplorer();
                services.AddSwaggerGen();
                configureServices(services);
              })
              .Configure(app =>
              {
                app.UseRouting();
                app.UseSwagger();
                app.UseEndpoints(endpoints =>
                {
                  if (mapAll)
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
