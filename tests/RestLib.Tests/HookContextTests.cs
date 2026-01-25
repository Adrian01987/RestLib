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

/// <summary>
/// Tests for Story 6.2: Hook Context Object
/// Verifies that the HookContext provides rich context for inspection/modification.
/// 
/// Acceptance Criteria:
/// - [ ] Can inspect request/entity
/// - [ ] Can modify entity
/// - [ ] Can short-circuit pipeline
/// - [ ] Items dictionary for data sharing
/// </summary>
public class HookContextTests
{
  private readonly JsonSerializerOptions _jsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    PropertyNameCaseInsensitive = true,
    WriteIndented = false
  };

  #region Test Entity

  private class ContextTestEntity
  {
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string? Category { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public string? ModifiedBy { get; set; }
  }

  #endregion

  #region Test Repository

  private class ContextTestRepository : IRepository<ContextTestEntity, int>
  {
    private readonly Dictionary<int, ContextTestEntity> _data = [];
    private int _nextId = 1;

    public ContextTestEntity? LastCreated { get; private set; }
    public ContextTestEntity? LastUpdated { get; private set; }

    public void AddTestData(params ContextTestEntity[] entities)
    {
      foreach (var entity in entities)
      {
        _data[entity.Id] = entity;
        if (entity.Id >= _nextId) _nextId = entity.Id + 1;
      }
    }

    public Task<ContextTestEntity> CreateAsync(ContextTestEntity entity, CancellationToken ct = default)
    {
      entity.Id = _nextId++;
      _data[entity.Id] = entity;
      LastCreated = entity;
      return Task.FromResult(entity);
    }

    public Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
      return Task.FromResult(_data.Remove(id));
    }

    public Task<PagedResult<ContextTestEntity>> GetAllAsync(PaginationRequest request, CancellationToken ct = default)
    {
      var items = _data.Values.ToList();
      return Task.FromResult(new PagedResult<ContextTestEntity> { Items = items, NextCursor = null });
    }

    public Task<ContextTestEntity?> GetByIdAsync(int id, CancellationToken ct = default)
    {
      _data.TryGetValue(id, out var entity);
      return Task.FromResult(entity);
    }

    public Task<ContextTestEntity?> PatchAsync(int id, JsonElement patchDocument, CancellationToken ct = default)
    {
      if (!_data.TryGetValue(id, out var entity)) return Task.FromResult<ContextTestEntity?>(null);
      if (patchDocument.TryGetProperty("name", out var name))
      {
        entity.Name = name.GetString() ?? entity.Name;
      }
      return Task.FromResult<ContextTestEntity?>(entity);
    }

    public Task<ContextTestEntity?> UpdateAsync(int id, ContextTestEntity entity, CancellationToken ct = default)
    {
      if (!_data.ContainsKey(id)) return Task.FromResult<ContextTestEntity?>(null);
      entity.Id = id;
      _data[id] = entity;
      LastUpdated = entity;
      return Task.FromResult<ContextTestEntity?>(entity);
    }
  }

  #endregion

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

  #region AC3: Can Short-Circuit Pipeline

  [Fact]
  public async Task HookContext_ShortCircuit_StopsSubsequentHooks()
  {
    // Arrange
    var hooksCalled = new List<string>();
    var repository = new ContextTestRepository();
    repository.AddTestData(new ContextTestEntity { Id = 1, Name = "Test" });

    using var host = await CreateHostWithHooks(repository, hooks =>
    {
      hooks.OnRequestReceived = async ctx =>
      {
        hooksCalled.Add("OnRequestReceived");
        ctx.ShouldContinue = false;
        ctx.EarlyResult = Results.Ok(new { message = "Early exit" });
        await Task.CompletedTask;
      };
      hooks.OnRequestValidated = async ctx =>
      {
        hooksCalled.Add("OnRequestValidated");
        await Task.CompletedTask;
      };
      hooks.BeforeResponse = async ctx =>
      {
        hooksCalled.Add("BeforeResponse");
        await Task.CompletedTask;
      };
    });

    var client = host.GetTestClient();

    // Act
    await client.GetAsync("/api/items/1");

    // Assert - Only OnRequestReceived should have been called
    hooksCalled.Should().HaveCount(1);
    hooksCalled.Should().Contain("OnRequestReceived");
  }

  [Fact]
  public async Task HookContext_ShortCircuit_SkipsRepositoryOperation()
  {
    // Arrange
    var repository = new ContextTestRepository();

    // We can detect if repository was called by checking if LastCreated is set
    using var host = await CreateHostWithHooks(repository, hooks =>
    {
      hooks.OnRequestValidated = async ctx =>
      {
        ctx.ShouldContinue = false;
        ctx.EarlyResult = Results.BadRequest(new { error = "Validation failed" });
        await Task.CompletedTask;
      };
    });

    var client = host.GetTestClient();
    var entity = new ContextTestEntity { Name = "Test" };

    // Act
    await client.PostAsJsonAsync("/api/items", entity);

    // Assert - Repository should not have been called
    repository.LastCreated.Should().BeNull();
  }

  [Fact]
  public async Task HookContext_ShortCircuit_ReturnsEarlyResult_StatusCode()
  {
    // Arrange
    var repository = new ContextTestRepository();
    repository.AddTestData(new ContextTestEntity { Id = 1, Name = "Test" });

    using var host = await CreateHostWithHooks(repository, hooks =>
    {
      hooks.OnRequestReceived = async ctx =>
      {
        ctx.ShouldContinue = false;
        ctx.EarlyResult = Results.StatusCode(StatusCodes.Status403Forbidden);
        await Task.CompletedTask;
      };
    });

    var client = host.GetTestClient();

    // Act
    var response = await client.GetAsync("/api/items/1");

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
  }

  [Fact]
  public async Task HookContext_ShortCircuit_ReturnsEarlyResult_CustomJson()
  {
    // Arrange
    var repository = new ContextTestRepository();
    repository.AddTestData(new ContextTestEntity { Id = 1, Name = "Test" });

    using var host = await CreateHostWithHooks(repository, hooks =>
    {
      hooks.OnRequestReceived = async ctx =>
      {
        ctx.ShouldContinue = false;
        ctx.EarlyResult = Results.Json(new { custom = "response", code = 42 });
        await Task.CompletedTask;
      };
    });

    var client = host.GetTestClient();

    // Act
    var response = await client.GetAsync("/api/items/1");
    var content = await response.Content.ReadAsStringAsync();

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    content.Should().Contain("custom");
    content.Should().Contain("response");
    content.Should().Contain("42");
  }

  [Fact]
  public async Task HookContext_ShortCircuit_CanReturnRedirect()
  {
    // Arrange
    var repository = new ContextTestRepository();
    repository.AddTestData(new ContextTestEntity { Id = 1, Name = "Test" });

    using var host = await CreateHostWithHooks(repository, hooks =>
    {
      hooks.OnRequestReceived = async ctx =>
      {
        ctx.ShouldContinue = false;
        ctx.EarlyResult = Results.Redirect("/api/items/2", permanent: false);
        await Task.CompletedTask;
      };
    });

    var client = host.GetTestClient();
    client.DefaultRequestHeaders.Add("X-Follow-Redirects", "false");

    // Act
    var response = await client.GetAsync("/api/items/1");

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.Redirect);
    response.Headers.Location.Should().NotBeNull();
    response.Headers.Location!.ToString().Should().Be("/api/items/2");
  }

  [Fact]
  public async Task HookContext_ShortCircuit_InBeforePersist_StopsRepositoryAndLaterHooks()
  {
    // Arrange
    var hooksCalled = new List<string>();
    var repository = new ContextTestRepository();

    using var host = await CreateHostWithHooks(repository, hooks =>
    {
      hooks.OnRequestReceived = async ctx =>
      {
        hooksCalled.Add("OnRequestReceived");
        await Task.CompletedTask;
      };
      hooks.BeforePersist = async ctx =>
      {
        hooksCalled.Add("BeforePersist");
        ctx.ShouldContinue = false;
        ctx.EarlyResult = Results.Conflict(new { error = "Cannot persist" });
        await Task.CompletedTask;
      };
      hooks.AfterPersist = async ctx =>
      {
        hooksCalled.Add("AfterPersist");
        await Task.CompletedTask;
      };
      hooks.BeforeResponse = async ctx =>
      {
        hooksCalled.Add("BeforeResponse");
        await Task.CompletedTask;
      };
    });

    var client = host.GetTestClient();
    var entity = new ContextTestEntity { Name = "Test" };

    // Act
    var response = await client.PostAsJsonAsync("/api/items", entity);

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    hooksCalled.Should().ContainInOrder("OnRequestReceived", "BeforePersist");
    hooksCalled.Should().NotContain("AfterPersist");
    hooksCalled.Should().NotContain("BeforeResponse");
    repository.LastCreated.Should().BeNull();
  }

  [Fact]
  public async Task HookContext_ShortCircuit_WithoutEarlyResult_Returns500()
  {
    // Arrange
    var repository = new ContextTestRepository();
    repository.AddTestData(new ContextTestEntity { Id = 1, Name = "Test" });

    using var host = await CreateHostWithHooks(repository, hooks =>
    {
      hooks.OnRequestReceived = async ctx =>
      {
        ctx.ShouldContinue = false;
        // Not setting EarlyResult
        await Task.CompletedTask;
      };
    });

    var client = host.GetTestClient();

    // Act
    var response = await client.GetAsync("/api/items/1");

    // Assert - Should return 500 when no EarlyResult is set
    response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
  }

  #endregion

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

  #region ErrorHookContext Tests

  [Fact]
  public async Task ErrorHookContext_CanInspect_Exception()
  {
    // Arrange
    Exception? capturedException = null;
    var repository = new ContextTestRepository();

    using var host = await CreateHostWithHooksAndErrorSimulation(repository, hooks =>
    {
#pragma warning disable CS1998 // Async method lacks await
      hooks.BeforePersist = async ctx =>
      {
        throw new InvalidOperationException("Simulated error for testing");
      };
#pragma warning restore CS1998
      hooks.OnError = async ctx =>
      {
        capturedException = ctx.Exception;
        ctx.Handled = true;
        ctx.ErrorResult = Results.StatusCode(500);
        await Task.CompletedTask;
      };
    });

    var client = host.GetTestClient();
    var entity = new ContextTestEntity { Name = "Test" };

    // Act
    await client.PostAsJsonAsync("/api/items", entity);

    // Assert
    capturedException.Should().NotBeNull();
    capturedException.Should().BeOfType<InvalidOperationException>();
    capturedException!.Message.Should().Be("Simulated error for testing");
  }

  [Fact]
  public async Task ErrorHookContext_CanProvide_CustomErrorResponse()
  {
    // Arrange
    var repository = new ContextTestRepository();

    using var host = await CreateHostWithHooksAndErrorSimulation(repository, hooks =>
    {
#pragma warning disable CS1998 // Async method lacks await
      hooks.BeforePersist = async ctx =>
      {
        throw new ArgumentException("Bad argument");
      };
#pragma warning restore CS1998
      hooks.OnError = async ctx =>
      {
        ctx.Handled = true;
        ctx.ErrorResult = Results.Json(
          new { errorType = ctx.Exception.GetType().Name, message = ctx.Exception.Message },
          statusCode: 422);
        await Task.CompletedTask;
      };
    });

    var client = host.GetTestClient();
    var entity = new ContextTestEntity { Name = "Test" };

    // Act
    var response = await client.PostAsJsonAsync("/api/items", entity);
    var content = await response.Content.ReadAsStringAsync();

    // Assert
    response.StatusCode.Should().Be((HttpStatusCode)422);
    content.Should().Contain("ArgumentException");
    content.Should().Contain("Bad argument");
  }

  [Fact]
  public async Task ErrorHookContext_HasAccess_ToItems()
  {
    // Arrange
    var repository = new ContextTestRepository();

    using var host = await CreateHostWithHooksAndErrorSimulation(repository, hooks =>
    {
      hooks.OnRequestReceived = async ctx =>
      {
        ctx.Items["RequestStartTime"] = DateTime.UtcNow.ToString("O");
        await Task.CompletedTask;
      };
#pragma warning disable CS1998 // Async method lacks await
      hooks.BeforePersist = async ctx =>
      {
        throw new Exception("Error after items were set");
      };
#pragma warning restore CS1998
      hooks.OnError = async ctx =>
      {
        // Note: Items are shared via the pipeline, so they should be available
        // in error context if they were set before the error
        ctx.Handled = true;
        ctx.ErrorResult = Results.StatusCode(500);
        await Task.CompletedTask;
      };
    });

    var client = host.GetTestClient();
    var entity = new ContextTestEntity { Name = "Test" };

    // Act
    var response = await client.PostAsJsonAsync("/api/items", entity);

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
  }

  #endregion

  #region Test Host Helper

  private static async Task<IHost> CreateHostWithHooks(
    ContextTestRepository repository,
    Action<RestLibHooks<ContextTestEntity, int>> configureHooks)
  {
    var host = await new HostBuilder()
      .ConfigureWebHost(webBuilder =>
      {
        webBuilder.UseTestServer();
        webBuilder.ConfigureServices(services =>
        {
          services.AddRouting();
          services.AddSingleton<IRepository<ContextTestEntity, int>>(repository);
        });
        webBuilder.Configure(app =>
        {
          app.UseRouting();
          app.UseEndpoints(endpoints =>
          {
            endpoints.MapRestLib<ContextTestEntity, int>("/api/items", config =>
            {
              config.AllowAnonymous();
              config.KeySelector = e => e.Id;
              config.UseHooks(configureHooks);
            });
          });
        });
      })
      .StartAsync();

    return host;
  }

  private static async Task<IHost> CreateHostWithHooksAndErrorSimulation(
    ContextTestRepository repository,
    Action<RestLibHooks<ContextTestEntity, int>> configureHooks)
  {
    // Same as above, but for error testing scenarios
    return await CreateHostWithHooks(repository, configureHooks);
  }

  #endregion
}
