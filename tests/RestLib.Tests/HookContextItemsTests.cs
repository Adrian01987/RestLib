using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestLib.Abstractions;
using RestLib.Hooks;
using RestLib.Pagination;
using Xunit;

namespace RestLib.Tests;

public partial class HookContextTests
{
  #region AC4: Items Dictionary for Data Sharing

  [Fact]
  public async Task HookContext_Items_SharesDataBetweenHooks()
  {
    // Arrange
    string? retrievedValue = null;
    var repository = new ContextTestRepository();
    repository.AddTestData(new ContextTestEntity { Id = 1, Name = "Test" });

    using var host = await CreateHostWithHooks(repository, hooks =>
    {
      hooks.OnRequestReceived = async ctx =>
      {
        ctx.Items["SharedKey"] = "SharedValue";
        await Task.CompletedTask;
      };
      hooks.BeforeResponse = async ctx =>
      {
        if (ctx.Items.TryGetValue("SharedKey", out var value))
        {
          retrievedValue = value as string;
        }
        await Task.CompletedTask;
      };
    });

    var client = host.GetTestClient();

    // Act
    await client.GetAsync("/api/items/1");

    // Assert
    retrievedValue.Should().Be("SharedValue");
  }

  [Fact]
  public async Task HookContext_Items_SupportsMultipleValues()
  {
    // Arrange
    var retrievedValues = new Dictionary<string, object?>();
    var repository = new ContextTestRepository();
    repository.AddTestData(new ContextTestEntity { Id = 1, Name = "Test" });

    using var host = await CreateHostWithHooks(repository, hooks =>
    {
      hooks.OnRequestReceived = async ctx =>
      {
        ctx.Items["StringValue"] = "Hello";
        ctx.Items["IntValue"] = 42;
        ctx.Items["BoolValue"] = true;
        ctx.Items["DateValue"] = new DateTime(2024, 1, 1);
        await Task.CompletedTask;
      };
      hooks.BeforeResponse = async ctx =>
      {
        foreach (var item in ctx.Items)
        {
          retrievedValues[item.Key] = item.Value;
        }
        await Task.CompletedTask;
      };
    });

    var client = host.GetTestClient();

    // Act
    await client.GetAsync("/api/items/1");

    // Assert
    retrievedValues.Should().ContainKey("StringValue").WhoseValue.Should().Be("Hello");
    retrievedValues.Should().ContainKey("IntValue").WhoseValue.Should().Be(42);
    retrievedValues.Should().ContainKey("BoolValue").WhoseValue.Should().Be(true);
    retrievedValues.Should().ContainKey("DateValue").WhoseValue.Should().Be(new DateTime(2024, 1, 1));
  }

  [Fact]
  public async Task HookContext_Items_CanBeModifiedAcrossHooks()
  {
    // Arrange
    int? finalCount = null;
    var repository = new ContextTestRepository();
    repository.AddTestData(new ContextTestEntity { Id = 1, Name = "Test" });

    using var host = await CreateHostWithHooks(repository, hooks =>
    {
      hooks.OnRequestReceived = async ctx =>
      {
        ctx.Items["Counter"] = 1;
        await Task.CompletedTask;
      };
      hooks.OnRequestValidated = async ctx =>
      {
        if (ctx.Items.TryGetValue("Counter", out var value) && value is int counter)
        {
          ctx.Items["Counter"] = counter + 1;
        }
        await Task.CompletedTask;
      };
      hooks.BeforeResponse = async ctx =>
      {
        if (ctx.Items.TryGetValue("Counter", out var value) && value is int counter)
        {
          ctx.Items["Counter"] = counter + 1;
          finalCount = counter + 1;
        }
        await Task.CompletedTask;
      };
    });

    var client = host.GetTestClient();

    // Act
    await client.GetAsync("/api/items/1");

    // Assert
    finalCount.Should().Be(3);
  }

  [Fact]
  public async Task HookContext_Items_SupportsNullValues()
  {
    // Arrange
    bool hadNullValue = false;
    bool keyExists = false;
    var repository = new ContextTestRepository();
    repository.AddTestData(new ContextTestEntity { Id = 1, Name = "Test" });

    using var host = await CreateHostWithHooks(repository, hooks =>
    {
      hooks.OnRequestReceived = async ctx =>
      {
        ctx.Items["NullKey"] = null;
        await Task.CompletedTask;
      };
      hooks.BeforeResponse = async ctx =>
      {
        keyExists = ctx.Items.ContainsKey("NullKey");
        hadNullValue = ctx.Items.TryGetValue("NullKey", out var value) && value is null;
        await Task.CompletedTask;
      };
    });

    var client = host.GetTestClient();

    // Act
    await client.GetAsync("/api/items/1");

    // Assert
    keyExists.Should().BeTrue();
    hadNullValue.Should().BeTrue();
  }

  [Fact]
  public async Task HookContext_Items_SupportsComplexObjects()
  {
    // Arrange
    Dictionary<string, int>? retrievedDict = null;
    var repository = new ContextTestRepository();
    repository.AddTestData(new ContextTestEntity { Id = 1, Name = "Test" });

    using var host = await CreateHostWithHooks(repository, hooks =>
    {
      hooks.OnRequestReceived = async ctx =>
      {
        ctx.Items["ComplexObject"] = new Dictionary<string, int>
        {
          ["one"] = 1,
          ["two"] = 2,
          ["three"] = 3
        };
        await Task.CompletedTask;
      };
      hooks.BeforeResponse = async ctx =>
      {
        if (ctx.Items.TryGetValue("ComplexObject", out var value))
        {
          retrievedDict = value as Dictionary<string, int>;
        }
        await Task.CompletedTask;
      };
    });

    var client = host.GetTestClient();

    // Act
    await client.GetAsync("/api/items/1");

    // Assert
    retrievedDict.Should().NotBeNull();
    retrievedDict!.Should().ContainKey("one").WhoseValue.Should().Be(1);
    retrievedDict.Should().ContainKey("two").WhoseValue.Should().Be(2);
    retrievedDict.Should().ContainKey("three").WhoseValue.Should().Be(3);
  }

  [Fact]
  public async Task HookContext_Items_IsolatedPerRequest()
  {
    // Arrange
    var valuesPerRequest = new List<int>();
    var repository = new ContextTestRepository();
    repository.AddTestData(new ContextTestEntity { Id = 1, Name = "Test" });

    using var host = await CreateHostWithHooks(repository, hooks =>
    {
      hooks.OnRequestReceived = async ctx =>
      {
        // Each request should start with an empty Items dictionary
        var existingCount = ctx.Items.Count;
        ctx.Items["RequestId"] = existingCount; // Will be 0 if isolated
        await Task.CompletedTask;
      };
      hooks.BeforeResponse = async ctx =>
      {
        if (ctx.Items.TryGetValue("RequestId", out var value) && value is int count)
        {
          valuesPerRequest.Add(count);
        }
        await Task.CompletedTask;
      };
    });

    var client = host.GetTestClient();

    // Act - Make multiple requests
    await client.GetAsync("/api/items/1");
    await client.GetAsync("/api/items/1");
    await client.GetAsync("/api/items/1");

    // Assert - All should have started with empty Items (count = 0)
    valuesPerRequest.Should().HaveCount(3);
    valuesPerRequest.Should().AllBeEquivalentTo(0);
  }

  [Fact]
  public async Task HookContext_Items_CanPassDataToBeUsedInEntityModification()
  {
    // Arrange
    var repository = new ContextTestRepository();

    using var host = await CreateHostWithHooks(repository, hooks =>
    {
      hooks.OnRequestReceived = async ctx =>
      {
        // Simulate extracting tenant info from header
        var tenantId = ctx.HttpContext.Request.Headers["X-Tenant-Id"].ToString();
        ctx.Items["TenantId"] = string.IsNullOrEmpty(tenantId) ? "default" : tenantId;
        await Task.CompletedTask;
      };
      hooks.BeforePersist = async ctx =>
      {
        if (ctx.Entity != null && ctx.Items.TryGetValue("TenantId", out var tenant))
        {
          ctx.Entity.Category = $"Tenant:{tenant}";
        }
        await Task.CompletedTask;
      };
    });

    var client = host.GetTestClient();
    var request = new HttpRequestMessage(HttpMethod.Post, "/api/items")
    {
      Content = JsonContent.Create(new ContextTestEntity { Name = "TenantTest" })
    };
    request.Headers.Add("X-Tenant-Id", "ACME");

    // Act
    var response = await client.SendAsync(request);
    var created = await response.Content.ReadFromJsonAsync<ContextTestEntity>(_jsonOptions);

    // Assert
    created!.Category.Should().Be("Tenant:ACME");
  }

  #endregion
}
