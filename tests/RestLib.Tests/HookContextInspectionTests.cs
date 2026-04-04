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
  #region AC1: Can Inspect Request/Entity

  [Fact]
  public async Task HookContext_CanInspect_HttpMethod()
  {
    // Arrange
    string? capturedMethod = null;
    var repository = new ContextTestRepository();
    repository.AddTestData(new ContextTestEntity { Id = 1, Name = "Test" });

    using var host = await CreateHostWithHooks(repository, hooks =>
    {
      hooks.OnRequestReceived = async ctx =>
      {
        capturedMethod = ctx.HttpContext.Request.Method;
        await Task.CompletedTask;
      };
    });

    var client = host.GetTestClient();

    // Act
    await client.GetAsync("/api/items/1");

    // Assert
    capturedMethod.Should().Be("GET");
  }

  [Fact]
  public async Task HookContext_CanInspect_RequestPath()
  {
    // Arrange
    string? capturedPath = null;
    var repository = new ContextTestRepository();
    repository.AddTestData(new ContextTestEntity { Id = 42, Name = "Test" });

    using var host = await CreateHostWithHooks(repository, hooks =>
    {
      hooks.OnRequestReceived = async ctx =>
      {
        capturedPath = ctx.HttpContext.Request.Path.Value;
        await Task.CompletedTask;
      };
    });

    var client = host.GetTestClient();

    // Act
    await client.GetAsync("/api/items/42");

    // Assert
    capturedPath.Should().Be("/api/items/42");
  }

  [Fact]
  public async Task HookContext_CanInspect_QueryString()
  {
    // Arrange
    string? capturedQuery = null;
    var repository = new ContextTestRepository();

    using var host = await CreateHostWithHooks(repository, hooks =>
    {
      hooks.OnRequestReceived = async ctx =>
      {
        capturedQuery = ctx.HttpContext.Request.QueryString.Value;
        await Task.CompletedTask;
      };
    });

    var client = host.GetTestClient();

    // Act
    await client.GetAsync("/api/items?limit=5");

    // Assert
    capturedQuery.Should().Be("?limit=5");
  }

  [Fact]
  public async Task HookContext_CanInspect_RequestHeaders()
  {
    // Arrange
    string? capturedHeader = null;
    var repository = new ContextTestRepository();
    repository.AddTestData(new ContextTestEntity { Id = 1, Name = "Test" });

    using var host = await CreateHostWithHooks(repository, hooks =>
    {
      hooks.OnRequestReceived = async ctx =>
      {
        capturedHeader = ctx.HttpContext.Request.Headers["X-Custom-Header"].ToString();
        await Task.CompletedTask;
      };
    });

    var client = host.GetTestClient();
    var request = new HttpRequestMessage(HttpMethod.Get, "/api/items/1");
    request.Headers.Add("X-Custom-Header", "CustomValue");

    // Act
    await client.SendAsync(request);

    // Assert
    capturedHeader.Should().Be("CustomValue");
  }

  [Fact]
  public async Task HookContext_CanInspect_Operation()
  {
    // Arrange
    var capturedOperations = new List<RestLibOperation>();
    var repository = new ContextTestRepository();
    repository.AddTestData(new ContextTestEntity { Id = 1, Name = "Test" });

    using var host = await CreateHostWithHooks(repository, hooks =>
    {
      hooks.OnRequestReceived = async ctx =>
      {
        capturedOperations.Add(ctx.Operation);
        await Task.CompletedTask;
      };
    });

    var client = host.GetTestClient();

    // Act - Test all operations
    await client.GetAsync("/api/items");
    await client.GetAsync("/api/items/1");
    await client.PostAsJsonAsync("/api/items", new ContextTestEntity { Name = "New" });
    await client.PutAsJsonAsync("/api/items/1", new ContextTestEntity { Name = "Updated" });
    var patch = new StringContent("{\"name\":\"Patched\"}", Encoding.UTF8, "application/json");
    await client.PatchAsync("/api/items/1", patch);
    await client.DeleteAsync("/api/items/1");

    // Assert
    capturedOperations.Should().ContainInOrder(
      RestLibOperation.GetAll,
      RestLibOperation.GetById,
      RestLibOperation.Create,
      RestLibOperation.Update,
      RestLibOperation.Patch,
      RestLibOperation.Delete
    );
  }

  [Fact]
  public async Task HookContext_CanInspect_ResourceId()
  {
    // Arrange
    int? capturedId = null;
    var repository = new ContextTestRepository();
    repository.AddTestData(new ContextTestEntity { Id = 99, Name = "Test" });

    using var host = await CreateHostWithHooks(repository, hooks =>
    {
      hooks.OnRequestReceived = async ctx =>
      {
        capturedId = ctx.ResourceId;
        await Task.CompletedTask;
      };
    });

    var client = host.GetTestClient();

    // Act
    await client.GetAsync("/api/items/99");

    // Assert
    capturedId.Should().Be(99);
  }

  [Fact]
  public async Task HookContext_CanInspect_Entity_InOnRequestValidated()
  {
    // Arrange
    ContextTestEntity? capturedEntity = null;
    var repository = new ContextTestRepository();

    using var host = await CreateHostWithHooks(repository, hooks =>
    {
      hooks.OnRequestValidated = async ctx =>
      {
        capturedEntity = ctx.Entity;
        await Task.CompletedTask;
      };
    });

    var client = host.GetTestClient();
    var entity = new ContextTestEntity { Name = "InspectMe", Price = 19.99m };

    // Act
    await client.PostAsJsonAsync("/api/items", entity);

    // Assert
    capturedEntity.Should().NotBeNull();
    capturedEntity!.Name.Should().Be("InspectMe");
    capturedEntity.Price.Should().Be(19.99m);
  }

  [Fact]
  public async Task HookContext_CanInspect_OriginalEntity_InBeforePersist()
  {
    // Arrange
    ContextTestEntity? capturedOriginal = null;
    var repository = new ContextTestRepository();
    repository.AddTestData(new ContextTestEntity { Id = 1, Name = "Original", Price = 10.00m });

    using var host = await CreateHostWithHooks(repository, hooks =>
    {
      hooks.BeforePersist = async ctx =>
      {
        capturedOriginal = ctx.OriginalEntity;
        await Task.CompletedTask;
      };
    });

    var client = host.GetTestClient();
    var updated = new ContextTestEntity { Name = "Updated", Price = 20.00m };

    // Act
    await client.PutAsJsonAsync("/api/items/1", updated);

    // Assert
    capturedOriginal.Should().NotBeNull();
    capturedOriginal!.Name.Should().Be("Original");
    capturedOriginal.Price.Should().Be(10.00m);
  }

  [Fact]
  public async Task HookContext_CanInspect_Services()
  {
    // Arrange
    bool foundRepository = false;
    var repository = new ContextTestRepository();
    repository.AddTestData(new ContextTestEntity { Id = 1, Name = "Test" });

    using var host = await CreateHostWithHooks(repository, hooks =>
    {
      hooks.OnRequestReceived = async ctx =>
      {
        foundRepository = ctx.Services.GetService<IRepository<ContextTestEntity, int>>() is not null;
        await Task.CompletedTask;
      };
    });

    var client = host.GetTestClient();

    // Act
    await client.GetAsync("/api/items/1");

    // Assert
    foundRepository.Should().BeTrue();
  }

  [Fact]
  public async Task HookContext_CanInspect_CancellationToken()
  {
    // Arrange
    bool tokenValid = false;
    var repository = new ContextTestRepository();
    repository.AddTestData(new ContextTestEntity { Id = 1, Name = "Test" });

    using var host = await CreateHostWithHooks(repository, hooks =>
    {
      hooks.OnRequestReceived = async ctx =>
      {
        // Token should be valid (not default and not cancelled)
        tokenValid = ctx.CancellationToken != default && !ctx.CancellationToken.IsCancellationRequested;
        await Task.CompletedTask;
      };
    });

    var client = host.GetTestClient();

    // Act
    await client.GetAsync("/api/items/1");

    // Assert
    tokenValid.Should().BeTrue();
  }

  #endregion
}
