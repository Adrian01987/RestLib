using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using RestLib.Abstractions;
using RestLib.Batch;
using RestLib.Hooks;
using RestLib.InMemory;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Integration tests that verify structured log messages are emitted by RestLib handlers.
/// Uses <see cref="FakeLogCollector"/> from Microsoft.Extensions.Diagnostics.Testing.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "Logging")]
public class EndpointLoggingTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private FakeLogCollector _logCollector = null!;
    private InMemoryRepository<TestEntity, Guid> _repository = null!;

    private Guid _seededId;

    public async Task InitializeAsync()
    {
        _seededId = Guid.NewGuid();
        _repository = new InMemoryRepository<TestEntity, Guid>(e => e.Id, Guid.NewGuid);
        _repository.Seed([new TestEntity { Id = _seededId, Name = "Existing", Price = 10m }]);

        (_host, _client, _logCollector) = await new TestHostBuilder<TestEntity, Guid>(_repository, "/api/items")
            .WithEndpoint(config => config.AllowAnonymous())
            .BuildWithLoggingAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    // ──────────────────────────────────────────────────────────────
    //  GetAll (EventId 1000, 1001)
    // ──────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Logging")]
    public async Task GetAll_LogsRequestReceivedAndResponse()
    {
        // Act
        var response = await _client.GetAsync("/api/items");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var logs = _logCollector.GetSnapshot();
        logs.Should().Contain(r => r.Id.Id == 1000 && r.Category == "RestLib.GetAll" && r.Level == LogLevel.Debug);
        logs.Should().Contain(r => r.Id.Id == 1001 && r.Category == "RestLib.GetAll" && r.Level == LogLevel.Debug);
    }

    [Fact]
    [Trait("Category", "Logging")]
    public async Task GetAll_WithLimit_LogsLimitInMessage()
    {
        // Act
        await _client.GetAsync("/api/items?limit=5");

        // Assert
        var entry = _logCollector.GetSnapshot()
            .FirstOrDefault(r => r.Id.Id == 1000);
        entry.Should().NotBeNull();
        entry!.Message.Should().Contain("5");
    }

    // ──────────────────────────────────────────────────────────────
    //  GetById (EventId 1010)
    // ──────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Logging")]
    public async Task GetById_LogsRequestReceived()
    {
        // Act
        var response = await _client.GetAsync($"/api/items/{_seededId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var logs = _logCollector.GetSnapshot();
        var entry = logs.FirstOrDefault(r => r.Id.Id == 1010);
        entry.Should().NotBeNull();
        entry!.Category.Should().Be("RestLib.GetById");
        entry.Level.Should().Be(LogLevel.Debug);
        entry.Message.Should().Contain(_seededId.ToString());
    }

    // ──────────────────────────────────────────────────────────────
    //  Create (EventId 1020, 1021)
    // ──────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Logging")]
    public async Task Create_LogsRequestReceivedAndEntityCreated()
    {
        // Act
        var response = await _client.PostAsJsonAsync("/api/items", new { name = "New", price = 5m });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var logs = _logCollector.GetSnapshot();
        logs.Should().Contain(r => r.Id.Id == 1020 && r.Category == "RestLib.Create" && r.Level == LogLevel.Debug);
        logs.Should().Contain(r => r.Id.Id == 1021 && r.Category == "RestLib.Create" && r.Level == LogLevel.Information);
    }

    [Fact]
    [Trait("Category", "Logging")]
    public async Task Create_EntityCreated_LogsLocationAndId()
    {
        // Act
        var response = await _client.PostAsJsonAsync("/api/items", new { name = "Tracked", price = 1m });

        // Assert
        var createdLog = _logCollector.GetSnapshot()
            .FirstOrDefault(r => r.Id.Id == 1021);
        createdLog.Should().NotBeNull();
        createdLog!.Message.Should().Contain("location:");
    }

    // ──────────────────────────────────────────────────────────────
    //  Update (EventId 1030)
    // ──────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Logging")]
    public async Task Update_LogsRequestReceived()
    {
        // Act
        var response = await _client.PutAsJsonAsync(
            $"/api/items/{_seededId}",
            new { name = "Updated", price = 20m });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var entry = _logCollector.GetSnapshot()
            .FirstOrDefault(r => r.Id.Id == 1030);
        entry.Should().NotBeNull();
        entry!.Category.Should().Be("RestLib.Update");
        entry.Level.Should().Be(LogLevel.Debug);
        entry.Message.Should().Contain(_seededId.ToString());
    }

    // ──────────────────────────────────────────────────────────────
    //  Patch (EventId 1040)
    // ──────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Logging")]
    public async Task Patch_LogsRequestReceived()
    {
        // Arrange
        var patchContent = new StringContent(
            JsonSerializer.Serialize(new { name = "Patched" }),
            Encoding.UTF8,
            "application/merge-patch+json");

        // Act
        var response = await _client.PatchAsync($"/api/items/{_seededId}", patchContent);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var entry = _logCollector.GetSnapshot()
            .FirstOrDefault(r => r.Id.Id == 1040);
        entry.Should().NotBeNull();
        entry!.Category.Should().Be("RestLib.Patch");
        entry.Level.Should().Be(LogLevel.Debug);
        entry.Message.Should().Contain(_seededId.ToString());
    }

    // ──────────────────────────────────────────────────────────────
    //  Delete (EventId 1050, 1051)
    // ──────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Logging")]
    public async Task Delete_LogsRequestReceivedAndEntityDeleted()
    {
        // Arrange — seed a dedicated entity to delete
        var deleteId = Guid.NewGuid();
        _repository.Seed([new TestEntity { Id = deleteId, Name = "ToDelete", Price = 1m }]);

        // Act
        var response = await _client.DeleteAsync($"/api/items/{deleteId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var logs = _logCollector.GetSnapshot();
        var received = logs.FirstOrDefault(r => r.Id.Id == 1050);
        received.Should().NotBeNull();
        received!.Category.Should().Be("RestLib.Delete");
        received.Level.Should().Be(LogLevel.Debug);

        var deleted = logs.FirstOrDefault(r => r.Id.Id == 1051);
        deleted.Should().NotBeNull();
        deleted!.Level.Should().Be(LogLevel.Information);
        deleted.Message.Should().Contain(deleteId.ToString());
    }
}

/// <summary>
/// Tests verifying that ProblemDetails responses generate appropriate log messages.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "Logging")]
public class ProblemDetailsLoggingTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private FakeLogCollector _logCollector = null!;
    private InMemoryRepository<ProductEntity, Guid> _repository = null!;

    public async Task InitializeAsync()
    {
        _repository = new InMemoryRepository<ProductEntity, Guid>(e => e.Id, Guid.NewGuid);

        (_host, _client, _logCollector) = await new TestHostBuilder<ProductEntity, Guid>(_repository, "/api/products")
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.AllowSorting(p => p.ProductName, p => p.UnitPrice);
                config.AllowFiltering(p => p.ProductName);
            })
            .BuildWithLoggingAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    // ──────────────────────────────────────────────────────────────
    //  ProblemDetailsClientError (EventId 1300) — 4xx responses
    // ──────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Logging")]
    public async Task GetById_NotFound_LogsProblemDetailsClientError()
    {
        // Act
        var response = await _client.GetAsync($"/api/products/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var logs = _logCollector.GetSnapshot();
        var problemLog = logs.FirstOrDefault(r => r.Id.Id == 1300);
        problemLog.Should().NotBeNull();
        problemLog!.Level.Should().Be(LogLevel.Information);
        problemLog.Message.Should().Contain("404");
    }

    [Fact]
    [Trait("Category", "Logging")]
    public async Task Delete_NotFound_LogsProblemDetailsClientError()
    {
        // Act
        var response = await _client.DeleteAsync($"/api/products/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var logs = _logCollector.GetSnapshot();
        logs.Should().Contain(r => r.Id.Id == 1300 && r.Level == LogLevel.Information);
    }

    [Fact]
    [Trait("Category", "Logging")]
    public async Task GetAll_InvalidSort_LogsProblemDetailsClientError()
    {
        // Act
        var response = await _client.GetAsync("/api/products?sort=invalid_field");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var logs = _logCollector.GetSnapshot();
        logs.Should().Contain(r => r.Id.Id == 1300 && r.Level == LogLevel.Information);
    }

    [Fact]
    [Trait("Category", "Logging")]
    public async Task GetAll_InvalidLimit_LogsProblemDetailsClientError()
    {
        // Act
        var response = await _client.GetAsync("/api/products?limit=0");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var logs = _logCollector.GetSnapshot();
        logs.Should().Contain(r => r.Id.Id == 1300 && r.Level == LogLevel.Information);
    }

    [Fact]
    [Trait("Category", "Logging")]
    public async Task Update_NotFound_LogsProblemDetailsClientError()
    {
        // Act
        var response = await _client.PutAsJsonAsync(
            $"/api/products/{Guid.NewGuid()}",
            new { product_name = "Ghost", unit_price = 0m, stock_quantity = 0, is_active = false });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var logs = _logCollector.GetSnapshot();
        logs.Should().Contain(r => r.Id.Id == 1300 && r.Level == LogLevel.Information);
    }
}

/// <summary>
/// Tests verifying that hook pipeline stages emit trace/debug log messages.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "Logging")]
public class HookLoggingTests : IAsyncLifetime
{
    private IHost? _host;
    private HttpClient? _client;
    private FakeLogCollector? _logCollector;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_host is not null)
        {
            await _host.StopAsync();
        }

        _host?.Dispose();
    }

    private async Task CreateHostWithHooksAsync(
        InMemoryRepository<TestEntity, Guid> repository,
        Action<RestLibHooks<TestEntity, Guid>> configureHooks)
    {
        (_host, _client, _logCollector) = await new TestHostBuilder<TestEntity, Guid>(repository, "/api/items")
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.UseHooks(configureHooks);
            })
            .BuildWithLoggingAsync();
    }

    // ──────────────────────────────────────────────────────────────
    //  HookStageEntry/Exit (EventId 1200, 1201) — Trace level
    // ──────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Logging")]
    public async Task Hook_OnRequestReceived_LogsEntryAndExit()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var repository = new InMemoryRepository<TestEntity, Guid>(e => e.Id, Guid.NewGuid);
        repository.Seed([new TestEntity { Id = entityId, Name = "Test", Price = 1m }]);

        await CreateHostWithHooksAsync(repository, hooks =>
        {
            hooks.OnRequestReceived = async ctx =>
            {
                await Task.CompletedTask;
            };
        });

        // Act
        await _client!.GetAsync($"/api/items/{entityId}");

        // Assert — Trace-level logs should be collected by FakeLogCollector
        var logs = _logCollector!.GetSnapshot();
        logs.Should().Contain(r => r.Id.Id == 1200 && r.Message.Contains("OnRequestReceived"));
        logs.Should().Contain(r => r.Id.Id == 1201 && r.Message.Contains("OnRequestReceived"));
    }

    // ──────────────────────────────────────────────────────────────
    //  HookStageShortCircuit (EventId 1202) — Debug level
    // ──────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Logging")]
    public async Task Hook_ShortCircuit_LogsShortCircuitMessage()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var repository = new InMemoryRepository<TestEntity, Guid>(e => e.Id, Guid.NewGuid);
        repository.Seed([new TestEntity { Id = entityId, Name = "Test", Price = 1m }]);

        await CreateHostWithHooksAsync(repository, hooks =>
        {
            hooks.OnRequestReceived = async ctx =>
            {
                ctx.ShouldContinue = false;
                ctx.EarlyResult = Microsoft.AspNetCore.Http.Results.Ok(new { shortCircuited = true });
                await Task.CompletedTask;
            };
        });

        // Act
        await _client!.GetAsync($"/api/items/{entityId}");

        // Assert
        var logs = _logCollector!.GetSnapshot();
        logs.Should().Contain(r => r.Id.Id == 1202 && r.Level == LogLevel.Debug
            && r.Message.Contains("OnRequestReceived"));
    }

    // ──────────────────────────────────────────────────────────────
    //  ErrorHookSwallowed (EventId 1220) — Error level
    // ──────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Logging")]
    public async Task ErrorHook_ThrowsException_LogsErrorHookSwallowed()
    {
        // Arrange
        var repository = new InMemoryRepository<TestEntity, Guid>(e => e.Id, Guid.NewGuid);

        await CreateHostWithHooksAsync(repository, hooks =>
        {
            hooks.BeforePersist = _ => throw new InvalidOperationException("Simulated error");
            hooks.OnError = _ => throw new InvalidOperationException("Error hook blew up");
        });

        // Act — The create triggers BeforePersist which throws, then OnError also throws (swallowed),
        // and the original exception re-throws through TestServer
        try
        {
            await _client!.PostAsJsonAsync("/api/items", new { name = "Boom", price = 1m });
        }
        catch (InvalidOperationException)
        {
            // Expected — the original exception propagates through TestServer
        }

        // Assert — The ErrorHookSwallowed should be logged even though the request ultimately failed
        var logs = _logCollector!.GetSnapshot();
        logs.Should().Contain(r => r.Id.Id == 1220 && r.Level == LogLevel.Error);
    }
}

/// <summary>
/// Tests verifying batch pipeline logging.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "Logging")]
public class BatchLoggingTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private FakeLogCollector _logCollector = null!;
    private InMemoryRepository<TestEntity, Guid> _repository = null!;

    public async Task InitializeAsync()
    {
        _repository = new InMemoryRepository<TestEntity, Guid>(e => e.Id, Guid.NewGuid);

        (_host, _client, _logCollector) = await new TestHostBuilder<TestEntity, Guid>(_repository, "/api/items")
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.EnableBatch(BatchAction.Create, BatchAction.Delete);
            })
            .BuildWithLoggingAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    // ──────────────────────────────────────────────────────────────
    //  BatchRequestReceived (EventId 1100) & BatchCompleted (EventId 1102)
    // ──────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Logging")]
    public async Task Batch_Create_LogsRequestReceivedAndCompleted()
    {
        // Arrange
        var batchPayload = new
        {
            action = "create",
            items = new[]
            {
                new { body = new { name = "Item1", price = 1m } },
                new { body = new { name = "Item2", price = 2m } },
            },
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/items/batch", batchPayload);

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.MultiStatus);

        var logs = _logCollector.GetSnapshot();
        logs.Should().Contain(r => r.Id.Id == 1100 && r.Category == "RestLib.Batch" && r.Level == LogLevel.Debug
            && r.Message.Contains("create") && r.Message.Contains("2"));

        logs.Should().Contain(r => r.Id.Id == 1102 && r.Category == "RestLib.Batch" && r.Level == LogLevel.Information);
    }

    // ──────────────────────────────────────────────────────────────
    //  BatchCreateCompleted (EventId 1130)
    // ──────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Logging")]
    public async Task Batch_Create_LogsBatchCreateCompleted()
    {
        // Arrange — use a host that also registers IBatchRepository so PersistBulkAsync is used
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();

        _repository = new InMemoryRepository<TestEntity, Guid>(e => e.Id, Guid.NewGuid);

        (_host, _client, _logCollector) = await new TestHostBuilder<TestEntity, Guid>(_repository, "/api/items")
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.EnableBatch(BatchAction.Create, BatchAction.Delete);
            })
            .WithServices(services =>
            {
                services.AddSingleton<IBatchRepository<TestEntity, Guid>>(_repository);
            })
            .BuildWithLoggingAsync();

        var batchPayload = new
        {
            action = "create",
            items = new[]
            {
                new { body = new { name = "A", price = 1m } },
                new { body = new { name = "B", price = 2m } },
                new { body = new { name = "C", price = 3m } },
            },
        };

        // Act
        await _client.PostAsJsonAsync("/api/items/batch", batchPayload);

        // Assert
        var logs = _logCollector.GetSnapshot();
        logs.Should().Contain(r => r.Id.Id == 1130 && r.Category == "RestLib.Batch"
            && r.Level == LogLevel.Information && r.Message.Contains("3"));
    }

    // ──────────────────────────────────────────────────────────────
    //  BatchEnvelopeDeserializationFailed (EventId 1101) — invalid JSON
    // ──────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Logging")]
    public async Task Batch_InvalidJson_LogsEnvelopeDeserializationFailed()
    {
        // Arrange
        var invalidJson = new StringContent(
            "{ not valid json }}}",
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _client.PostAsync("/api/items/batch", invalidJson);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var logs = _logCollector.GetSnapshot();
        logs.Should().Contain(r => r.Id.Id == 1101 && r.Level == LogLevel.Warning);
    }

    // ──────────────────────────────────────────────────────────────
    //  Batch validation errors generate ProblemDetailsClientError (EventId 1300)
    // ──────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Logging")]
    public async Task Batch_InvalidAction_LogsProblemDetailsClientError()
    {
        // Arrange
        var batchPayload = new
        {
            action = "invalid_action",
            items = new[] { new { body = new { name = "X", price = 1m } } },
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/items/batch", batchPayload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var logs = _logCollector.GetSnapshot();
        logs.Should().Contain(r => r.Id.Id == 1300 && r.Level == LogLevel.Information);
    }
}

/// <summary>
/// Tests verifying that the OptionsResolver logs when RestLib options are not registered.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "Logging")]
public class InfrastructureLoggingTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private FakeLogCollector _logCollector = null!;

    public async Task InitializeAsync()
    {
        var repository = new InMemoryRepository<TestEntity, Guid>(e => e.Id, Guid.NewGuid);
        repository.Seed([new TestEntity { Id = Guid.NewGuid(), Name = "Test", Price = 1m }]);

        (_host, _client, _logCollector) = await new TestHostBuilder<TestEntity, Guid>(repository, "/api/items")
            .SkipRestLibRegistration()
            .WithEndpoint(config => config.AllowAnonymous())
            .BuildWithLoggingAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    // ──────────────────────────────────────────────────────────────
    //  OptionsNotRegistered (EventId 1340) — Warning level
    // ──────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Logging")]
    public async Task OptionsNotRegistered_LogsWarning()
    {
        // Act — Making a request without having called AddRestLib()
        await _client.GetAsync("/api/items");

        // Assert
        var logs = _logCollector.GetSnapshot();
        logs.Should().Contain(r => r.Id.Id == 1340 && r.Level == LogLevel.Warning
            && r.Category == "RestLib.OptionsResolver");
    }
}

/// <summary>
/// Tests verifying that multiple handler categories produce logs with correct category names.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "Logging")]
public class LogCategoryTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private FakeLogCollector _logCollector = null!;
    private InMemoryRepository<TestEntity, Guid> _repository = null!;

    private Guid _seededId;

    public async Task InitializeAsync()
    {
        _seededId = Guid.NewGuid();
        _repository = new InMemoryRepository<TestEntity, Guid>(e => e.Id, Guid.NewGuid);
        _repository.Seed([new TestEntity { Id = _seededId, Name = "Test", Price = 1m }]);

        (_host, _client, _logCollector) = await new TestHostBuilder<TestEntity, Guid>(_repository, "/api/items")
            .WithEndpoint(config => config.AllowAnonymous())
            .BuildWithLoggingAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    [Trait("Category", "Logging")]
    public async Task AllHandlers_UseCorrectCategoryNames()
    {
        // Act — Hit each handler type
        await _client.GetAsync("/api/items");
        await _client.GetAsync($"/api/items/{_seededId}");
        await _client.PostAsJsonAsync("/api/items", new { name = "New", price = 1m });
        await _client.PutAsJsonAsync($"/api/items/{_seededId}", new { name = "Updated", price = 2m });
        var patch = new StringContent(
            JsonSerializer.Serialize(new { name = "Patched" }),
            Encoding.UTF8,
            "application/merge-patch+json");
        await _client.PatchAsync($"/api/items/{_seededId}", patch);
        await _client.DeleteAsync($"/api/items/{_seededId}");

        // Assert — Each category should appear at least once
        var logs = _logCollector.GetSnapshot();
        var categories = logs.Select(r => r.Category).Distinct().ToList();

        categories.Should().Contain("RestLib.GetAll");
        categories.Should().Contain("RestLib.GetById");
        categories.Should().Contain("RestLib.Create");
        categories.Should().Contain("RestLib.Update");
        categories.Should().Contain("RestLib.Patch");
        categories.Should().Contain("RestLib.Delete");
    }

    [Fact]
    [Trait("Category", "Logging")]
    public async Task GetAll_ResponseLog_ContainsItemCountAndHasNextPage()
    {
        // Act
        await _client.GetAsync("/api/items");

        // Assert — filter by category to avoid EventId collision with ASP.NET Core routing
        var responseLog = _logCollector.GetSnapshot()
            .FirstOrDefault(r => r.Id.Id == 1001 && r.Category == "RestLib.GetAll");
        responseLog.Should().NotBeNull();
        responseLog!.Message.Should().MatchRegex(@"\d+ items");
        responseLog.Message.Should().Contain("has next page:");
    }
}

/// <summary>
/// Tests verifying that no ILoggerFactory still works (NullLogger fallback).
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "Logging")]
public class NullLoggerFallbackTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var repository = new InMemoryRepository<TestEntity, Guid>(e => e.Id, Guid.NewGuid);
        repository.Seed([new TestEntity { Id = Guid.NewGuid(), Name = "Test", Price = 1m }]);

        // Use standard BuildAsync (no fake logging) — HostBuilder doesn't register ILoggerFactory by default.
        // Skip RestLib registration to avoid any logging framework being registered.
        (_host, _client) = await new TestHostBuilder<TestEntity, Guid>(repository, "/api/items")
            .SkipRestLibRegistration()
            .WithEndpoint(config => config.AllowAnonymous())
            .BuildAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    [Trait("Category", "Logging")]
    public async Task NoLoggerFactory_HandlerStillWorks()
    {
        // Act — Verify that endpoints work even without an ILoggerFactory registered
        var response = await _client.GetAsync("/api/items");

        // Assert — Should still return success (NullLogger fallback means no crash)
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
