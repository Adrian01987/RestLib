using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestLib.Abstractions;
using RestLib.Hooks;
using RestLib.Pagination;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Tests for Story 6.1: Hook Pipeline Definition
/// Verifies that all 6 hooks are available, optional, execute in order, and support async.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "Hooks")]
public class HookPipelineTests
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    #region Test Entity

    private class HookTestEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? AuditInfo { get; set; }
        public DateTime? ProcessedAt { get; set; }
    }

    #endregion

    #region Test Repository

    private class HookTestRepository : IRepository<HookTestEntity, int>
    {
        private readonly Dictionary<int, HookTestEntity> _data = [];
        private int _nextId = 1;

        public bool ShouldThrowOnCreate { get; set; }
        public bool ShouldThrowOnUpdate { get; set; }
        public bool ShouldThrowOnDelete { get; set; }
        public bool ShouldThrowOnPatch { get; set; }
        public bool ShouldThrowOnGetById { get; set; }
        public bool ShouldThrowOnGetAll { get; set; }
        public Exception? ExceptionToThrow { get; set; }

        public void AddTestData(params HookTestEntity[] entities)
        {
            foreach (var entity in entities)
            {
                _data[entity.Id] = entity;
                if (entity.Id >= _nextId) _nextId = entity.Id + 1;
            }
        }

        public Task<HookTestEntity> CreateAsync(HookTestEntity entity, CancellationToken ct = default)
        {
            if (ShouldThrowOnCreate) throw ExceptionToThrow ?? new InvalidOperationException("Create error");
            entity.Id = _nextId++;
            _data[entity.Id] = entity;
            return Task.FromResult(entity);
        }

        public Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            if (ShouldThrowOnDelete) throw ExceptionToThrow ?? new InvalidOperationException("Delete error");
            return Task.FromResult(_data.Remove(id));
        }

        public Task<PagedResult<HookTestEntity>> GetAllAsync(PaginationRequest request, CancellationToken ct = default)
        {
            if (ShouldThrowOnGetAll) throw ExceptionToThrow ?? new InvalidOperationException("GetAll error");
            var items = _data.Values.ToList();
            return Task.FromResult(new PagedResult<HookTestEntity> { Items = items, NextCursor = null });
        }

        public Task<HookTestEntity?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            if (ShouldThrowOnGetById) throw ExceptionToThrow ?? new InvalidOperationException("GetById error");
            _data.TryGetValue(id, out var entity);
            return Task.FromResult(entity);
        }

        public Task<HookTestEntity?> PatchAsync(int id, JsonElement patchDocument, CancellationToken ct = default)
        {
            if (ShouldThrowOnPatch) throw ExceptionToThrow ?? new InvalidOperationException("Patch error");
            if (!_data.TryGetValue(id, out var entity)) return Task.FromResult<HookTestEntity?>(null);
            if (patchDocument.TryGetProperty("name", out var name))
            {
                entity.Name = name.GetString() ?? entity.Name;
            }
            if (patchDocument.TryGetProperty("auditInfo", out var audit))
            {
                entity.AuditInfo = audit.GetString();
            }
            return Task.FromResult<HookTestEntity?>(entity);
        }

        public Task<HookTestEntity?> UpdateAsync(int id, HookTestEntity entity, CancellationToken ct = default)
        {
            if (ShouldThrowOnUpdate) throw ExceptionToThrow ?? new InvalidOperationException("Update error");
            if (!_data.ContainsKey(id)) return Task.FromResult<HookTestEntity?>(null);
            entity.Id = id;
            _data[id] = entity;
            return Task.FromResult<HookTestEntity?>(entity);
        }
    }

    #endregion

    #region Hook Execution Order Tests

    [Fact]
    public async Task GetById_Should_ExecuteHooksInCorrectOrder()
    {
        // Arrange
        var executionOrder = new List<string>();
        var repository = new HookTestRepository();
        repository.AddTestData(new HookTestEntity { Id = 1, Name = "Test" });

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            hooks.OnRequestReceived = async ctx => { executionOrder.Add("OnRequestReceived"); await Task.CompletedTask; };
            hooks.OnRequestValidated = async ctx => { executionOrder.Add("OnRequestValidated"); await Task.CompletedTask; };
            hooks.BeforeResponse = async ctx => { executionOrder.Add("BeforeResponse"); await Task.CompletedTask; };
        });

        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/api/items/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        executionOrder.Should().ContainInOrder("OnRequestReceived", "OnRequestValidated", "BeforeResponse");
    }

    [Fact]
    public async Task Create_Should_ExecuteHooksInCorrectOrder()
    {
        // Arrange
        var executionOrder = new List<string>();
        var repository = new HookTestRepository();

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            hooks.OnRequestReceived = async ctx => { executionOrder.Add("OnRequestReceived"); await Task.CompletedTask; };
            hooks.OnRequestValidated = async ctx => { executionOrder.Add("OnRequestValidated"); await Task.CompletedTask; };
            hooks.BeforePersist = async ctx => { executionOrder.Add("BeforePersist"); await Task.CompletedTask; };
            hooks.AfterPersist = async ctx => { executionOrder.Add("AfterPersist"); await Task.CompletedTask; };
            hooks.BeforeResponse = async ctx => { executionOrder.Add("BeforeResponse"); await Task.CompletedTask; };
        });

        var client = host.GetTestClient();
        var entity = new HookTestEntity { Name = "New Item" };

        // Act
        var response = await client.PostAsJsonAsync("/api/items", entity);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        executionOrder.Should().ContainInOrder(
          "OnRequestReceived",
          "OnRequestValidated",
          "BeforePersist",
          "AfterPersist",
          "BeforeResponse");
    }

    [Fact]
    public async Task Update_Should_ExecuteHooksInCorrectOrder()
    {
        // Arrange
        var executionOrder = new List<string>();
        var repository = new HookTestRepository();
        repository.AddTestData(new HookTestEntity { Id = 1, Name = "Original" });

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            hooks.OnRequestReceived = async ctx => { executionOrder.Add("OnRequestReceived"); await Task.CompletedTask; };
            hooks.OnRequestValidated = async ctx => { executionOrder.Add("OnRequestValidated"); await Task.CompletedTask; };
            hooks.BeforePersist = async ctx => { executionOrder.Add("BeforePersist"); await Task.CompletedTask; };
            hooks.AfterPersist = async ctx => { executionOrder.Add("AfterPersist"); await Task.CompletedTask; };
            hooks.BeforeResponse = async ctx => { executionOrder.Add("BeforeResponse"); await Task.CompletedTask; };
        });

        var client = host.GetTestClient();
        var entity = new HookTestEntity { Name = "Updated" };

        // Act
        var response = await client.PutAsJsonAsync("/api/items/1", entity);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        executionOrder.Should().ContainInOrder(
          "OnRequestReceived",
          "OnRequestValidated",
          "BeforePersist",
          "AfterPersist",
          "BeforeResponse");
    }

    [Fact]
    public async Task Patch_Should_ExecuteHooksInCorrectOrder()
    {
        // Arrange
        var executionOrder = new List<string>();
        var repository = new HookTestRepository();
        repository.AddTestData(new HookTestEntity { Id = 1, Name = "Original" });

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            hooks.OnRequestReceived = async ctx => { executionOrder.Add("OnRequestReceived"); await Task.CompletedTask; };
            hooks.OnRequestValidated = async ctx => { executionOrder.Add("OnRequestValidated"); await Task.CompletedTask; };
            hooks.BeforePersist = async ctx => { executionOrder.Add("BeforePersist"); await Task.CompletedTask; };
            hooks.AfterPersist = async ctx => { executionOrder.Add("AfterPersist"); await Task.CompletedTask; };
            hooks.BeforeResponse = async ctx => { executionOrder.Add("BeforeResponse"); await Task.CompletedTask; };
        });

        var client = host.GetTestClient();
        var patch = new { name = "Patched" };
        var content = new StringContent(JsonSerializer.Serialize(patch), Encoding.UTF8, "application/json");

        // Act
        var response = await client.PatchAsync("/api/items/1", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        executionOrder.Should().ContainInOrder(
          "OnRequestReceived",
          "OnRequestValidated",
          "BeforePersist",
          "AfterPersist",
          "BeforeResponse");
    }

    [Fact]
    public async Task Delete_Should_ExecuteHooksInCorrectOrder()
    {
        // Arrange
        var executionOrder = new List<string>();
        var repository = new HookTestRepository();
        repository.AddTestData(new HookTestEntity { Id = 1, Name = "ToDelete" });

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            hooks.OnRequestReceived = async ctx => { executionOrder.Add("OnRequestReceived"); await Task.CompletedTask; };
            hooks.OnRequestValidated = async ctx => { executionOrder.Add("OnRequestValidated"); await Task.CompletedTask; };
            hooks.BeforePersist = async ctx => { executionOrder.Add("BeforePersist"); await Task.CompletedTask; };
            hooks.AfterPersist = async ctx => { executionOrder.Add("AfterPersist"); await Task.CompletedTask; };
            hooks.BeforeResponse = async ctx => { executionOrder.Add("BeforeResponse"); await Task.CompletedTask; };
        });

        var client = host.GetTestClient();

        // Act
        var response = await client.DeleteAsync("/api/items/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        executionOrder.Should().ContainInOrder(
          "OnRequestReceived",
          "OnRequestValidated",
          "BeforePersist",
          "AfterPersist",
          "BeforeResponse");
    }

    [Fact]
    public async Task GetAll_Should_ExecuteHooksInCorrectOrder()
    {
        // Arrange
        var executionOrder = new List<string>();
        var repository = new HookTestRepository();
        repository.AddTestData(new HookTestEntity { Id = 1, Name = "Item1" });

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            hooks.OnRequestReceived = async ctx => { executionOrder.Add("OnRequestReceived"); await Task.CompletedTask; };
            hooks.OnRequestValidated = async ctx => { executionOrder.Add("OnRequestValidated"); await Task.CompletedTask; };
            hooks.BeforeResponse = async ctx => { executionOrder.Add("BeforeResponse"); await Task.CompletedTask; };
        });

        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/api/items");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        executionOrder.Should().ContainInOrder("OnRequestReceived", "OnRequestValidated", "BeforeResponse");
    }

    #endregion

    #region Hook Optional Tests

    [Fact]
    public async Task Endpoint_Should_WorkWithoutAnyHooks()
    {
        // Arrange
        var repository = new HookTestRepository();
        repository.AddTestData(new HookTestEntity { Id = 1, Name = "Test" });

        using var host = await CreateHostWithHooks(repository, hooks => { /* No hooks configured */ });

        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/api/items/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Endpoint_Should_WorkWithOnlySomeHooks()
    {
        // Arrange
        var executionOrder = new List<string>();
        var repository = new HookTestRepository();
        repository.AddTestData(new HookTestEntity { Id = 1, Name = "Test" });

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            // Only configure OnRequestReceived, skip all others
            hooks.OnRequestReceived = async ctx => { executionOrder.Add("OnRequestReceived"); await Task.CompletedTask; };
        });

        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/api/items/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        executionOrder.Should().ContainSingle().Which.Should().Be("OnRequestReceived");
    }

    #endregion

    #region Async Support Tests

    [Fact]
    public async Task Hooks_Should_SupportAsyncOperations()
    {
        // Arrange
        var asyncCompleted = new List<string>();
        var repository = new HookTestRepository();
        repository.AddTestData(new HookTestEntity { Id = 1, Name = "Test" });

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            hooks.OnRequestReceived = async ctx =>
        {
            await Task.Delay(10);
            asyncCompleted.Add("OnRequestReceived");
        };
            hooks.OnRequestValidated = async ctx =>
        {
            await Task.Delay(10);
            asyncCompleted.Add("OnRequestValidated");
        };
            hooks.BeforeResponse = async ctx =>
        {
            await Task.Delay(10);
            asyncCompleted.Add("BeforeResponse");
        };
        });

        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/api/items/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        asyncCompleted.Should().HaveCount(3);
        asyncCompleted.Should().ContainInOrder("OnRequestReceived", "OnRequestValidated", "BeforeResponse");
    }

    #endregion

    #region Hook Context Tests

    [Fact]
    public async Task HookContext_Should_ContainHttpContext()
    {
        // Arrange
        string? capturedMethod = null;
        string? capturedPath = null;
        var repository = new HookTestRepository();
        repository.AddTestData(new HookTestEntity { Id = 1, Name = "Test" });

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            hooks.OnRequestReceived = async ctx =>
        {
            // Capture values synchronously before request completes
            capturedMethod = ctx.HttpContext.Request.Method;
            capturedPath = ctx.HttpContext.Request.Path.Value;
            await Task.CompletedTask;
        };
        });

        var client = host.GetTestClient();

        // Act
        await client.GetAsync("/api/items/1");

        // Assert
        capturedMethod.Should().Be("GET");
        capturedPath.Should().Be("/api/items/1");
    }

    [Fact]
    public async Task HookContext_Should_ContainOperation()
    {
        // Arrange
        RestLibOperation? capturedOperation = null;
        var repository = new HookTestRepository();
        repository.AddTestData(new HookTestEntity { Id = 1, Name = "Test" });

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            hooks.OnRequestReceived = async ctx =>
        {
            capturedOperation = ctx.Operation;
            await Task.CompletedTask;
        };
        });

        var client = host.GetTestClient();

        // Act
        await client.GetAsync("/api/items/1");

        // Assert
        capturedOperation.Should().Be(RestLibOperation.GetById);
    }

    [Fact]
    public async Task HookContext_Should_ContainResourceId()
    {
        // Arrange
        int? capturedResourceId = null;
        var repository = new HookTestRepository();
        repository.AddTestData(new HookTestEntity { Id = 42, Name = "Test" });

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            hooks.OnRequestReceived = async ctx =>
        {
            capturedResourceId = ctx.ResourceId;
            await Task.CompletedTask;
        };
        });

        var client = host.GetTestClient();

        // Act
        await client.GetAsync("/api/items/42");

        // Assert
        capturedResourceId.Should().Be(42);
    }

    [Fact]
    public async Task HookContext_Should_ContainEntity_ForCreate()
    {
        // Arrange
        HookTestEntity? capturedEntity = null;
        var repository = new HookTestRepository();

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            hooks.OnRequestValidated = async ctx =>
        {
            capturedEntity = ctx.Entity;
            await Task.CompletedTask;
        };
        });

        var client = host.GetTestClient();
        var entity = new HookTestEntity { Name = "New Item" };

        // Act
        await client.PostAsJsonAsync("/api/items", entity);

        // Assert
        capturedEntity.Should().NotBeNull();
        capturedEntity!.Name.Should().Be("New Item");
    }

    [Fact]
    public async Task HookContext_Should_ContainServices()
    {
        // Arrange
        bool hasRepositoryService = false;
        var repository = new HookTestRepository();
        repository.AddTestData(new HookTestEntity { Id = 1, Name = "Test" });

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            hooks.OnRequestReceived = async ctx =>
        {
            // Verify services are accessible during request
            hasRepositoryService = ctx.Services.GetService<IRepository<HookTestEntity, int>>() is not null;
            await Task.CompletedTask;
        };
        });

        var client = host.GetTestClient();

        // Act
        await client.GetAsync("/api/items/1");

        // Assert
        hasRepositoryService.Should().BeTrue();
    }

    [Fact]
    public async Task HookContext_Should_ContainCancellationToken()
    {
        // Arrange
        CancellationToken capturedToken = default;
        var repository = new HookTestRepository();
        repository.AddTestData(new HookTestEntity { Id = 1, Name = "Test" });

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            hooks.OnRequestReceived = async ctx =>
        {
            capturedToken = ctx.CancellationToken;
            await Task.CompletedTask;
        };
        });

        var client = host.GetTestClient();

        // Act
        await client.GetAsync("/api/items/1");

        // Assert
        capturedToken.Should().NotBe(default(CancellationToken));
    }

    #endregion

    #region Short-Circuit Tests

    [Fact]
    public async Task HookContext_ShouldContinue_False_Should_StopPipeline()
    {
        // Arrange
        var executionOrder = new List<string>();
        var repository = new HookTestRepository();
        repository.AddTestData(new HookTestEntity { Id = 1, Name = "Test" });

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            hooks.OnRequestReceived = async ctx =>
        {
            executionOrder.Add("OnRequestReceived");
            ctx.ShouldContinue = false;
            ctx.EarlyResult = Results.StatusCode(StatusCodes.Status403Forbidden);
            await Task.CompletedTask;
        };
            hooks.OnRequestValidated = async ctx =>
        {
            executionOrder.Add("OnRequestValidated");
            await Task.CompletedTask;
        };
        });

        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/api/items/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        executionOrder.Should().ContainSingle().Which.Should().Be("OnRequestReceived");
    }

    [Fact]
    public async Task HookContext_EarlyResult_Should_BeReturned()
    {
        // Arrange
        var repository = new HookTestRepository();
        repository.AddTestData(new HookTestEntity { Id = 1, Name = "Test" });

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            hooks.OnRequestReceived = async ctx =>
        {
            ctx.ShouldContinue = false;
            ctx.EarlyResult = Results.Json(new { message = "Custom response" });
            await Task.CompletedTask;
        };
        });

        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/api/items/1");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("Custom response");
    }

    [Fact]
    public async Task StaleEarlyResult_FromPreviousStage_DoesNotLeakIntoNextStage()
    {
        // Arrange — OnRequestReceived sets EarlyResult but leaves ShouldContinue = true
        // (user mistake). BeforePersist then short-circuits without setting EarlyResult.
        // Without the fix, the stale 403 from OnRequestReceived would be returned.
        // With the fix, the stale EarlyResult is cleared, so the 500 fallback is returned.
        var repository = new HookTestRepository();

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            hooks.OnRequestReceived = async ctx =>
        {
            // Set EarlyResult but forget to set ShouldContinue = false
            ctx.EarlyResult = Results.StatusCode(StatusCodes.Status403Forbidden);
            await Task.CompletedTask;
        };
            hooks.BeforePersist = async ctx =>
        {
            // Short-circuit without setting a new EarlyResult
            ctx.ShouldContinue = false;
            await Task.CompletedTask;
        };
        });

        var client = host.GetTestClient();
        var entity = new HookTestEntity { Name = "Test" };

        // Act
        var response = await client.PostAsJsonAsync("/api/items", entity);

        // Assert — should NOT be 403 (that was the stale EarlyResult from OnRequestReceived).
        // Should be 500 (the fallback when ShouldContinue=false but EarlyResult is null).
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    #endregion

    #region Item Sharing Tests

    [Fact]
    public async Task HookContext_Items_Should_ShareDataBetweenHooks()
    {
        // Arrange
        object? retrievedValue = null;
        var repository = new HookTestRepository();
        repository.AddTestData(new HookTestEntity { Id = 1, Name = "Test" });

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            hooks.OnRequestReceived = async ctx =>
        {
            ctx.Items["SharedKey"] = "SharedValue";
            await Task.CompletedTask;
        };
            hooks.BeforeResponse = async ctx =>
        {
            ctx.Items.TryGetValue("SharedKey", out retrievedValue);
            await Task.CompletedTask;
        };
        });

        var client = host.GetTestClient();

        // Act
        await client.GetAsync("/api/items/1");

        // Assert
        retrievedValue.Should().Be("SharedValue");
    }

    #endregion

    #region Entity Modification Tests

    [Fact]
    public async Task BeforePersist_Should_AllowEntityModification()
    {
        // Arrange
        var repository = new HookTestRepository();

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            hooks.BeforePersist = async ctx =>
        {
            if (ctx.Entity != null)
            {
                ctx.Entity.AuditInfo = "Modified by BeforePersist";
                ctx.Entity.ProcessedAt = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);
            }
            await Task.CompletedTask;
        };
        });

        var client = host.GetTestClient();
        var entity = new HookTestEntity { Name = "New Item" };

        // Act
        var response = await client.PostAsJsonAsync("/api/items", entity);
        var created = await response.Content.ReadFromJsonAsync<HookTestEntity>(_jsonOptions);

        // Assert
        created!.AuditInfo.Should().Be("Modified by BeforePersist");
        created.ProcessedAt.Should().Be(new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc));
    }

    #endregion

    #region Error Hook Tests

    [Fact]
    public async Task OnError_Should_BeCalled_WhenExceptionOccurs()
    {
        // Arrange
        Exception? capturedError = null;
        var repository = new HookTestRepository();
        repository.ShouldThrowOnCreate = true;
        repository.ExceptionToThrow = new InvalidOperationException("Test error");

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            hooks.OnError = async ctx =>
        {
            capturedError = ctx.Exception;
            ctx.Handled = true;
            ctx.ErrorResult = Results.StatusCode(StatusCodes.Status500InternalServerError);
            await Task.CompletedTask;
        };
        });

        var client = host.GetTestClient();
        var entity = new HookTestEntity { Name = "Will Fail" };

        // Act
        var response = await client.PostAsJsonAsync("/api/items", entity);

        // Assert
        capturedError.Should().NotBeNull();
        capturedError!.Message.Should().Be("Test error");
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task OnError_Should_AllowCustomErrorResponse()
    {
        // Arrange
        var repository = new HookTestRepository();
        repository.ShouldThrowOnUpdate = true;

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            hooks.OnError = async ctx =>
        {
            ctx.Handled = true;
            ctx.ErrorResult = Results.Json(new { error = "Custom error message" }, statusCode: 422);
            await Task.CompletedTask;
        };
        });

        var client = host.GetTestClient();
        repository.AddTestData(new HookTestEntity { Id = 1, Name = "Test" });
        var entity = new HookTestEntity { Name = "Will Fail" };

        // Act
        var response = await client.PutAsJsonAsync("/api/items/1", entity);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be((HttpStatusCode)422);
        content.Should().Contain("Custom error message");
    }

    [Fact]
    public async Task OnError_Unhandled_Should_RethrowException()
    {
        // Arrange
        var repository = new HookTestRepository();
        repository.ShouldThrowOnCreate = true;

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            hooks.OnError = async ctx =>
        {
            // Don't set Handled = true, let the exception propagate
            await Task.CompletedTask;
        };
        });

        var client = host.GetTestClient();
        var entity = new HookTestEntity { Name = "Will Fail" };

        // Act & Assert
        // When Handled = false, the exception is re-thrown and propagates to the caller
        var act = async () => await client.PostAsJsonAsync("/api/items", entity);
        await act.Should().ThrowAsync<InvalidOperationException>()
          .WithMessage("Create error");
    }

    [Fact]
    public async Task ErrorHookContext_Should_ContainOperation()
    {
        // Arrange
        RestLibOperation? capturedOperation = null;
        var repository = new HookTestRepository();
        repository.ShouldThrowOnDelete = true;
        repository.AddTestData(new HookTestEntity { Id = 1, Name = "Test" });

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            hooks.OnError = async ctx =>
        {
            capturedOperation = ctx.Operation;
            ctx.Handled = true;
            ctx.ErrorResult = Results.StatusCode(500);
            await Task.CompletedTask;
        };
        });

        var client = host.GetTestClient();

        // Act
        await client.DeleteAsync("/api/items/1");

        // Assert
        capturedOperation.Should().Be(RestLibOperation.Delete);
    }

    [Fact]
    public async Task ErrorHookContext_Should_ContainResourceId()
    {
        // Arrange
        int? capturedResourceId = null;
        var repository = new HookTestRepository();
        repository.ShouldThrowOnDelete = true;
        repository.AddTestData(new HookTestEntity { Id = 99, Name = "Test" });

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            hooks.OnError = async ctx =>
        {
            capturedResourceId = ctx.ResourceId;
            ctx.Handled = true;
            ctx.ErrorResult = Results.StatusCode(500);
            await Task.CompletedTask;
        };
        });

        var client = host.GetTestClient();

        // Act
        await client.DeleteAsync("/api/items/99");

        // Assert
        capturedResourceId.Should().Be(99);
    }

    [Fact]
    public async Task OnError_HookThrows_OriginalExceptionPropagates()
    {
        // Arrange — error hook itself throws; the original repository exception should propagate
        var repository = new HookTestRepository();
        repository.ShouldThrowOnCreate = true;

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            hooks.OnError = ctx =>
                throw new InvalidOperationException("Hook explosion");
        });

        var client = host.GetTestClient();
        var entity = new HookTestEntity { Name = "Will Fail" };

        // Act & Assert — the original "Create error" exception must surface, not "Hook explosion"
        var act = async () => await client.PostAsJsonAsync("/api/items", entity);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Create error");
    }

    [Fact]
    public async Task OnError_Patch_Should_BeCalled_WhenRepositoryThrows()
    {
        // Arrange
        Exception? capturedError = null;
        RestLibOperation? capturedOperation = null;
        var repository = new HookTestRepository();
        repository.ShouldThrowOnPatch = true;
        repository.ExceptionToThrow = new InvalidOperationException("Patch error");
        repository.AddTestData(new HookTestEntity { Id = 1, Name = "Existing" });

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            hooks.OnError = async ctx =>
        {
            capturedError = ctx.Exception;
            capturedOperation = ctx.Operation;
            ctx.Handled = true;
            ctx.ErrorResult = Results.StatusCode(StatusCodes.Status500InternalServerError);
            await Task.CompletedTask;
        };
        });

        var client = host.GetTestClient();
        var patch = new { name = "Updated" };
        var content = new StringContent(
            JsonSerializer.Serialize(patch), Encoding.UTF8, "application/json");

        // Act
        var response = await client.PatchAsync("/api/items/1", content);

        // Assert
        capturedError.Should().NotBeNull();
        capturedError!.Message.Should().Be("Patch error");
        capturedOperation.Should().Be(RestLibOperation.Patch);
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task OnError_GetById_Should_BeCalled_WhenRepositoryThrows()
    {
        // Arrange
        Exception? capturedError = null;
        RestLibOperation? capturedOperation = null;
        var repository = new HookTestRepository();
        repository.ShouldThrowOnGetById = true;
        repository.ExceptionToThrow = new InvalidOperationException("GetById error");

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            hooks.OnError = async ctx =>
        {
            capturedError = ctx.Exception;
            capturedOperation = ctx.Operation;
            ctx.Handled = true;
            ctx.ErrorResult = Results.StatusCode(StatusCodes.Status500InternalServerError);
            await Task.CompletedTask;
        };
        });

        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/api/items/1");

        // Assert
        capturedError.Should().NotBeNull();
        capturedError!.Message.Should().Be("GetById error");
        capturedOperation.Should().Be(RestLibOperation.GetById);
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task OnError_GetAll_Should_BeCalled_WhenRepositoryThrows()
    {
        // Arrange
        Exception? capturedError = null;
        RestLibOperation? capturedOperation = null;
        var repository = new HookTestRepository();
        repository.ShouldThrowOnGetAll = true;
        repository.ExceptionToThrow = new InvalidOperationException("GetAll error");

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            hooks.OnError = async ctx =>
        {
            capturedError = ctx.Exception;
            capturedOperation = ctx.Operation;
            ctx.Handled = true;
            ctx.ErrorResult = Results.StatusCode(StatusCodes.Status500InternalServerError);
            await Task.CompletedTask;
        };
        });

        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/api/items");

        // Assert
        capturedError.Should().NotBeNull();
        capturedError!.Message.Should().Be("GetAll error");
        capturedOperation.Should().Be(RestLibOperation.GetAll);
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    #endregion

    #region Unhandled Repository Exception Tests (No Hooks)

    [Fact]
    public async Task Create_RepositoryThrows_NoHooks_ExceptionPropagates()
    {
        // Arrange — no hooks configured at all, so pipeline is null
        var repository = new HookTestRepository();
        repository.ShouldThrowOnCreate = true;
        repository.ExceptionToThrow = new InvalidOperationException("Database timeout on create");

        var (host, client) = await new TestHostBuilder<HookTestEntity, int>(repository, "/api/items")
            .SkipRestLibRegistration()
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.KeySelector = e => e.Id;
            })
            .BuildAsync();

        using (host)
        {
            var entity = new HookTestEntity { Name = "Will Fail" };

            // Act & Assert — exception should propagate since there are no hooks to catch it
            var act = async () => await client.PostAsJsonAsync("/api/items", entity);
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Database timeout on create");
        }
    }

    [Fact]
    public async Task GetById_RepositoryThrows_NoHooks_ExceptionPropagates()
    {
        // Arrange — no hooks configured at all
        var repository = new HookTestRepository();
        repository.ShouldThrowOnGetById = true;
        repository.ExceptionToThrow = new TimeoutException("Database timeout on read");

        var (host, client) = await new TestHostBuilder<HookTestEntity, int>(repository, "/api/items")
            .SkipRestLibRegistration()
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.KeySelector = e => e.Id;
            })
            .BuildAsync();

        using (host)
        {
            // Act & Assert
            var act = async () => await client.GetAsync("/api/items/1");
            await act.Should().ThrowAsync<TimeoutException>()
                .WithMessage("Database timeout on read");
        }
    }

    [Fact]
    public async Task GetAll_RepositoryThrows_NoHooks_ExceptionPropagates()
    {
        // Arrange — no hooks configured at all
        var repository = new HookTestRepository();
        repository.ShouldThrowOnGetAll = true;
        repository.ExceptionToThrow = new TimeoutException("Database timeout on list");

        var (host, client) = await new TestHostBuilder<HookTestEntity, int>(repository, "/api/items")
            .SkipRestLibRegistration()
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.KeySelector = e => e.Id;
            })
            .BuildAsync();

        using (host)
        {
            // Act & Assert
            var act = async () => await client.GetAsync("/api/items");
            await act.Should().ThrowAsync<TimeoutException>()
                .WithMessage("Database timeout on list");
        }
    }

    [Fact]
    public async Task Patch_RepositoryThrows_NoHooks_ExceptionPropagates()
    {
        // Arrange — no hooks configured at all
        var repository = new HookTestRepository();
        repository.ShouldThrowOnPatch = true;
        repository.ExceptionToThrow = new InvalidOperationException("Database timeout on patch");
        repository.AddTestData(new HookTestEntity { Id = 1, Name = "Existing" });

        var (host, client) = await new TestHostBuilder<HookTestEntity, int>(repository, "/api/items")
            .SkipRestLibRegistration()
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.KeySelector = e => e.Id;
            })
            .BuildAsync();

        using (host)
        {
            var patch = new { name = "Updated" };
            var content = new StringContent(
                JsonSerializer.Serialize(patch), Encoding.UTF8, "application/json");

            // Act & Assert
            var act = async () => await client.PatchAsync("/api/items/1", content);
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Database timeout on patch");
        }
    }

    [Fact]
    public async Task Update_RepositoryThrows_NoHooks_ExceptionPropagates()
    {
        // Arrange — no hooks configured at all
        var repository = new HookTestRepository();
        repository.ShouldThrowOnUpdate = true;
        repository.ExceptionToThrow = new InvalidOperationException("Database timeout on update");
        repository.AddTestData(new HookTestEntity { Id = 1, Name = "Existing" });

        var (host, client) = await new TestHostBuilder<HookTestEntity, int>(repository, "/api/items")
            .SkipRestLibRegistration()
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.KeySelector = e => e.Id;
            })
            .BuildAsync();

        using (host)
        {
            var entity = new HookTestEntity { Name = "Will Fail" };

            // Act & Assert
            var act = async () => await client.PutAsJsonAsync("/api/items/1", entity);
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Database timeout on update");
        }
    }

    [Fact]
    public async Task Delete_RepositoryThrows_NoHooks_ExceptionPropagates()
    {
        // Arrange — no hooks configured at all
        var repository = new HookTestRepository();
        repository.ShouldThrowOnDelete = true;
        repository.ExceptionToThrow = new InvalidOperationException("Database timeout on delete");
        repository.AddTestData(new HookTestEntity { Id = 1, Name = "Existing" });

        var (host, client) = await new TestHostBuilder<HookTestEntity, int>(repository, "/api/items")
            .SkipRestLibRegistration()
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.KeySelector = e => e.Id;
            })
            .BuildAsync();

        using (host)
        {
            // Act & Assert
            var act = async () => await client.DeleteAsync("/api/items/1");
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Database timeout on delete");
        }
    }

    #endregion

    #region Operation-Specific Hook Behavior

    [Fact]
    public async Task GetById_Should_ProvideCorrectOperation()
    {
        // Arrange
        RestLibOperation? operation = null;
        var repository = new HookTestRepository();
        repository.AddTestData(new HookTestEntity { Id = 1, Name = "Test" });

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            hooks.OnRequestReceived = async ctx =>
        {
            operation = ctx.Operation;
            await Task.CompletedTask;
        };
        });

        var client = host.GetTestClient();

        // Act
        await client.GetAsync("/api/items/1");

        // Assert
        operation.Should().Be(RestLibOperation.GetById);
    }

    [Fact]
    public async Task GetAll_Should_ProvideCorrectOperation()
    {
        // Arrange
        RestLibOperation? operation = null;
        var repository = new HookTestRepository();

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            hooks.OnRequestReceived = async ctx =>
        {
            operation = ctx.Operation;
            await Task.CompletedTask;
        };
        });

        var client = host.GetTestClient();

        // Act
        await client.GetAsync("/api/items");

        // Assert
        operation.Should().Be(RestLibOperation.GetAll);
    }

    [Fact]
    public async Task Create_Should_ProvideCorrectOperation()
    {
        // Arrange
        RestLibOperation? operation = null;
        var repository = new HookTestRepository();

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            hooks.OnRequestReceived = async ctx =>
        {
            operation = ctx.Operation;
            await Task.CompletedTask;
        };
        });

        var client = host.GetTestClient();
        var entity = new HookTestEntity { Name = "New" };

        // Act
        await client.PostAsJsonAsync("/api/items", entity);

        // Assert
        operation.Should().Be(RestLibOperation.Create);
    }

    [Fact]
    public async Task Update_Should_ProvideCorrectOperation()
    {
        // Arrange
        RestLibOperation? operation = null;
        var repository = new HookTestRepository();
        repository.AddTestData(new HookTestEntity { Id = 1, Name = "Test" });

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            hooks.OnRequestReceived = async ctx =>
        {
            operation = ctx.Operation;
            await Task.CompletedTask;
        };
        });

        var client = host.GetTestClient();
        var entity = new HookTestEntity { Name = "Updated" };

        // Act
        await client.PutAsJsonAsync("/api/items/1", entity);

        // Assert
        operation.Should().Be(RestLibOperation.Update);
    }

    [Fact]
    public async Task Patch_Should_ProvideCorrectOperation()
    {
        // Arrange
        RestLibOperation? operation = null;
        var repository = new HookTestRepository();
        repository.AddTestData(new HookTestEntity { Id = 1, Name = "Test" });

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            hooks.OnRequestReceived = async ctx =>
        {
            operation = ctx.Operation;
            await Task.CompletedTask;
        };
        });

        var client = host.GetTestClient();
        var patch = new { name = "Patched" };
        var content = new StringContent(JsonSerializer.Serialize(patch), Encoding.UTF8, "application/json");

        // Act
        await client.PatchAsync("/api/items/1", content);

        // Assert
        operation.Should().Be(RestLibOperation.Patch);
    }

    [Fact]
    public async Task Delete_Should_ProvideCorrectOperation()
    {
        // Arrange
        RestLibOperation? operation = null;
        var repository = new HookTestRepository();
        repository.AddTestData(new HookTestEntity { Id = 1, Name = "Test" });

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            hooks.OnRequestReceived = async ctx =>
        {
            operation = ctx.Operation;
            await Task.CompletedTask;
        };
        });

        var client = host.GetTestClient();

        // Act
        await client.DeleteAsync("/api/items/1");

        // Assert
        operation.Should().Be(RestLibOperation.Delete);
    }

    #endregion

    #region All 6 Hooks Test

    [Fact]
    public async Task All6Hooks_Should_BeAvailable()
    {
        // Arrange
        var hooksExecuted = new HashSet<string>();
        var repository = new HookTestRepository();
        repository.ShouldThrowOnCreate = true; // To trigger error hook

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            hooks.OnRequestReceived = async ctx => { hooksExecuted.Add("OnRequestReceived"); await Task.CompletedTask; };
            hooks.OnRequestValidated = async ctx => { hooksExecuted.Add("OnRequestValidated"); await Task.CompletedTask; };
            hooks.BeforePersist = async ctx => { hooksExecuted.Add("BeforePersist"); await Task.CompletedTask; };
            hooks.AfterPersist = async ctx => { hooksExecuted.Add("AfterPersist"); await Task.CompletedTask; };
            hooks.BeforeResponse = async ctx => { hooksExecuted.Add("BeforeResponse"); await Task.CompletedTask; };
            hooks.OnError = async ctx =>
        {
            hooksExecuted.Add("OnError");
            ctx.Handled = true;
            ctx.ErrorResult = Results.StatusCode(500);
            await Task.CompletedTask;
        };
        });

        var client = host.GetTestClient();
        var entity = new HookTestEntity { Name = "Test" };

        // Act
        await client.PostAsJsonAsync("/api/items", entity);

        // Assert - OnError will be called instead of AfterPersist and BeforeResponse
        hooksExecuted.Should().Contain("OnRequestReceived");
        hooksExecuted.Should().Contain("OnRequestValidated");
        hooksExecuted.Should().Contain("BeforePersist");
        hooksExecuted.Should().Contain("OnError");

        // Arrange - test successful path
        hooksExecuted.Clear();
        repository.ShouldThrowOnCreate = false;

        // Act
        await client.PostAsJsonAsync("/api/items", entity);

        // Assert
        hooksExecuted.Should().Contain("OnRequestReceived");
        hooksExecuted.Should().Contain("OnRequestValidated");
        hooksExecuted.Should().Contain("BeforePersist");
        hooksExecuted.Should().Contain("AfterPersist");
        hooksExecuted.Should().Contain("BeforeResponse");
    }

    #endregion

    #region Original Entity Tests

    [Fact]
    public async Task Update_BeforePersist_Should_HaveOriginalEntity()
    {
        // Arrange
        HookTestEntity? capturedOriginal = null;
        var repository = new HookTestRepository();
        repository.AddTestData(new HookTestEntity { Id = 1, Name = "Original Name" });

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            hooks.BeforePersist = async ctx =>
        {
            capturedOriginal = ctx.OriginalEntity;
            await Task.CompletedTask;
        };
        });

        var client = host.GetTestClient();
        var updated = new HookTestEntity { Name = "Updated Name" };

        // Act
        await client.PutAsJsonAsync("/api/items/1", updated);

        // Assert
        capturedOriginal.Should().NotBeNull();
        capturedOriginal!.Name.Should().Be("Original Name");
    }

    [Fact]
    public async Task Patch_BeforePersist_Should_HaveOriginalEntity()
    {
        // Arrange
        HookTestEntity? capturedOriginal = null;
        var repository = new HookTestRepository();
        repository.AddTestData(new HookTestEntity { Id = 1, Name = "Original Name", AuditInfo = "Original Audit" });

        using var host = await CreateHostWithHooks(repository, hooks =>
        {
            hooks.BeforePersist = async ctx =>
        {
            // Capture state at hook execution time - we just verify the entity is available
            // Note: For PATCH, the entity reference is passed to the hook before the patch is applied
            capturedOriginal = ctx.OriginalEntity;
            await Task.CompletedTask;
        };
        });

        var client = host.GetTestClient();
        var patch = new { name = "Patched Name" };
        var content = new StringContent(JsonSerializer.Serialize(patch), Encoding.UTF8, "application/json");

        // Act
        await client.PatchAsync("/api/items/1", content);

        // Assert - verify the original entity reference is provided (before patch is applied)
        capturedOriginal.Should().NotBeNull();
        // The original entity reference is available - AuditInfo should remain unchanged
        // since we only patched the name field
        capturedOriginal!.AuditInfo.Should().Be("Original Audit");
    }

    #endregion

    #region Test Host Helper

    private static async Task<IHost> CreateHostWithHooks(
      HookTestRepository repository,
      Action<RestLibHooks<HookTestEntity, int>> configureHooks)
    {
        var (host, _) = await new TestHostBuilder<HookTestEntity, int>(repository, "/api/items")
            .SkipRestLibRegistration()
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.KeySelector = e => e.Id;
                config.UseHooks(configureHooks);
            })
            .BuildAsync();

        return host;
    }

    #endregion
}
