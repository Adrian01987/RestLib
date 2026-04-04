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
  #region AC2: Can Modify Entity

  [Fact]
  public async Task HookContext_CanModify_Entity_InOnRequestReceived()
  {
    // Arrange
    var repository = new ContextTestRepository();

    using var host = await CreateHostWithHooks(repository, hooks =>
    {
      hooks.OnRequestReceived = async ctx =>
      {
        if (ctx.Entity != null)
        {
          ctx.Entity.Category = "Modified in OnRequestReceived";
        }
        await Task.CompletedTask;
      };
    });

    var client = host.GetTestClient();
    var entity = new ContextTestEntity { Name = "Test" };

    // Act
    var response = await client.PostAsJsonAsync("/api/items", entity);
    var created = await response.Content.ReadFromJsonAsync<ContextTestEntity>(_jsonOptions);

    // Assert
    created!.Category.Should().Be("Modified in OnRequestReceived");
  }

  [Fact]
  public async Task HookContext_CanModify_Entity_InOnRequestValidated()
  {
    // Arrange
    var repository = new ContextTestRepository();

    using var host = await CreateHostWithHooks(repository, hooks =>
    {
      hooks.OnRequestValidated = async ctx =>
      {
        if (ctx.Entity != null)
        {
          ctx.Entity.Description = "Validated and modified";
        }
        await Task.CompletedTask;
      };
    });

    var client = host.GetTestClient();
    var entity = new ContextTestEntity { Name = "Test" };

    // Act
    var response = await client.PostAsJsonAsync("/api/items", entity);
    var created = await response.Content.ReadFromJsonAsync<ContextTestEntity>(_jsonOptions);

    // Assert
    created!.Description.Should().Be("Validated and modified");
  }

  [Fact]
  public async Task HookContext_CanModify_Entity_InBeforePersist()
  {
    // Arrange
    var repository = new ContextTestRepository();
    var testTime = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc);

    using var host = await CreateHostWithHooks(repository, hooks =>
    {
      hooks.BeforePersist = async ctx =>
      {
        if (ctx.Entity != null)
        {
          ctx.Entity.CreatedAt = testTime;
          ctx.Entity.ModifiedBy = "system";
        }
        await Task.CompletedTask;
      };
    });

    var client = host.GetTestClient();
    var entity = new ContextTestEntity { Name = "Test" };

    // Act
    var response = await client.PostAsJsonAsync("/api/items", entity);
    var created = await response.Content.ReadFromJsonAsync<ContextTestEntity>(_jsonOptions);

    // Assert
    created!.CreatedAt.Should().Be(testTime);
    created.ModifiedBy.Should().Be("system");
  }

  [Fact]
  public async Task HookContext_ModifiedEntity_IsPersisted()
  {
    // Arrange
    var repository = new ContextTestRepository();

    using var host = await CreateHostWithHooks(repository, hooks =>
    {
      hooks.BeforePersist = async ctx =>
      {
        if (ctx.Entity != null)
        {
          ctx.Entity.Price = 999.99m;
        }
        await Task.CompletedTask;
      };
    });

    var client = host.GetTestClient();
    var entity = new ContextTestEntity { Name = "Test", Price = 10.00m };

    // Act
    await client.PostAsJsonAsync("/api/items", entity);

    // Assert - Verify the modification was persisted
    repository.LastCreated.Should().NotBeNull();
    repository.LastCreated!.Price.Should().Be(999.99m);
  }

  [Fact]
  public async Task HookContext_CanModify_Entity_AcrossMultipleHooks()
  {
    // Arrange
    var repository = new ContextTestRepository();

    using var host = await CreateHostWithHooks(repository, hooks =>
    {
      hooks.OnRequestReceived = async ctx =>
      {
        if (ctx.Entity != null)
        {
          ctx.Entity.Category = "Step1";
        }
        await Task.CompletedTask;
      };
      hooks.OnRequestValidated = async ctx =>
      {
        if (ctx.Entity != null)
        {
          ctx.Entity.Category += "-Step2";
        }
        await Task.CompletedTask;
      };
      hooks.BeforePersist = async ctx =>
      {
        if (ctx.Entity != null)
        {
          ctx.Entity.Category += "-Step3";
        }
        await Task.CompletedTask;
      };
    });

    var client = host.GetTestClient();
    var entity = new ContextTestEntity { Name = "Test" };

    // Act
    var response = await client.PostAsJsonAsync("/api/items", entity);
    var created = await response.Content.ReadFromJsonAsync<ContextTestEntity>(_jsonOptions);

    // Assert
    created!.Category.Should().Be("Step1-Step2-Step3");
  }

  [Fact]
  public async Task HookContext_CanModify_Entity_OnUpdate()
  {
    // Arrange
    var repository = new ContextTestRepository();
    repository.AddTestData(new ContextTestEntity { Id = 1, Name = "Original" });
    var updateTime = new DateTime(2024, 7, 20, 15, 30, 0, DateTimeKind.Utc);

    using var host = await CreateHostWithHooks(repository, hooks =>
    {
      hooks.BeforePersist = async ctx =>
      {
        if (ctx.Operation == RestLibOperation.Update && ctx.Entity != null)
        {
          ctx.Entity.ModifiedAt = updateTime;
          ctx.Entity.ModifiedBy = "updater";
        }
        await Task.CompletedTask;
      };
    });

    var client = host.GetTestClient();
    var updated = new ContextTestEntity { Name = "Updated" };

    // Act
    var response = await client.PutAsJsonAsync("/api/items/1", updated);
    var result = await response.Content.ReadFromJsonAsync<ContextTestEntity>(_jsonOptions);

    // Assert
    result!.ModifiedAt.Should().Be(updateTime);
    result.ModifiedBy.Should().Be("updater");
  }

  #endregion
}
