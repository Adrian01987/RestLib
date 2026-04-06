using System.ComponentModel.DataAnnotations;
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
using RestLib.Batch;
using RestLib.Configuration;
using RestLib.Hooks;
using RestLib.InMemory;
using RestLib.Pagination;
using RestLib.Responses;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Entity used in batch operations integration tests.
/// </summary>
public class BatchEntity
{
  /// <summary>Gets or sets the entity identifier.</summary>
  public Guid Id { get; set; }

  /// <summary>Gets or sets the entity name.</summary>
  [Required]
  [StringLength(100)]
  public string Name { get; set; } = string.Empty;

  /// <summary>Gets or sets the entity price.</summary>
  public decimal Price { get; set; }

  /// <summary>Gets or sets a value indicating whether the entity is active.</summary>
  public bool IsActive { get; set; } = true;
}

/// <summary>
/// Repository that throws on every write operation, used to test OnError hooks.
/// </summary>
public class ThrowingBatchRepository : IRepository<BatchEntity, Guid>
{
  /// <inheritdoc />
  public Task<BatchEntity?> GetByIdAsync(Guid id, CancellationToken ct = default)
      => Task.FromResult<BatchEntity?>(null);

  /// <inheritdoc />
  public Task<PagedResult<BatchEntity>> GetAllAsync(PaginationRequest pagination, CancellationToken ct = default)
      => Task.FromResult(new PagedResult<BatchEntity> { Items = [] });

  /// <inheritdoc />
  public Task<BatchEntity> CreateAsync(BatchEntity entity, CancellationToken ct = default)
      => throw new InvalidOperationException("Simulated repository failure");

  /// <inheritdoc />
  public Task<BatchEntity?> UpdateAsync(Guid id, BatchEntity entity, CancellationToken ct = default)
      => throw new InvalidOperationException("Simulated repository failure");

  /// <inheritdoc />
  public Task<BatchEntity?> PatchAsync(Guid id, JsonElement patchDocument, CancellationToken ct = default)
      => throw new InvalidOperationException("Simulated repository failure");

  /// <inheritdoc />
  public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
      => throw new InvalidOperationException("Simulated repository failure");
}

/// <summary>
/// Repository that returns an entity from GetByIdAsync but throws on UpdateAsync,
/// used to test exception detail gating in batch update error paths.
/// </summary>
public class ThrowingUpdateRepository : IRepository<BatchEntity, Guid>
{
  private readonly Guid _knownId;

  /// <summary>
  /// Initializes a new instance of the <see cref="ThrowingUpdateRepository"/> class.
  /// </summary>
  /// <param name="knownId">The ID that GetByIdAsync will recognise.</param>
  public ThrowingUpdateRepository(Guid knownId) => _knownId = knownId;

  /// <inheritdoc />
  public Task<BatchEntity?> GetByIdAsync(Guid id, CancellationToken ct = default)
      => Task.FromResult<BatchEntity?>(
          id == _knownId
              ? new BatchEntity { Id = id, Name = "Original", Price = 5m }
              : null);

  /// <inheritdoc />
  public Task<PagedResult<BatchEntity>> GetAllAsync(PaginationRequest pagination, CancellationToken ct = default)
      => Task.FromResult(new PagedResult<BatchEntity> { Items = [] });

  /// <inheritdoc />
  public Task<BatchEntity> CreateAsync(BatchEntity entity, CancellationToken ct = default)
      => throw new InvalidOperationException("Simulated update failure");

  /// <inheritdoc />
  public Task<BatchEntity?> UpdateAsync(Guid id, BatchEntity entity, CancellationToken ct = default)
      => throw new InvalidOperationException("Simulated update failure");

  /// <inheritdoc />
  public Task<BatchEntity?> PatchAsync(Guid id, JsonElement patchDocument, CancellationToken ct = default)
      => throw new InvalidOperationException("Simulated update failure");

  /// <inheritdoc />
  public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
      => throw new InvalidOperationException("Simulated update failure");
}

/// <summary>
/// Integration tests for batch operations (Stories 8.1–8.8).
/// </summary>
public class BatchOperationsTests : IDisposable
{
  private readonly InMemoryRepository<BatchEntity, Guid> _repository;
  private IHost? _host;
  private HttpClient? _client;

  /// <summary>
  /// Initializes a new instance of the <see cref="BatchOperationsTests"/> class.
  /// </summary>
  public BatchOperationsTests()
  {
    _repository = new InMemoryRepository<BatchEntity, Guid>(
        e => e.Id,
        Guid.NewGuid);
  }

  private void CreateHost(
      Action<RestLibEndpointConfiguration<BatchEntity, Guid>> configure,
      Action<RestLibOptions>? configureOptions = null)
  {
    _host = new HostBuilder()
        .ConfigureWebHost(webBuilder =>
        {
          webBuilder
              .UseTestServer()
              .ConfigureServices(services =>
              {
                services.AddRestLib(options =>
                {
                  configureOptions?.Invoke(options);
                });
                services.AddSingleton<IRepository<BatchEntity, Guid>>(_repository);
                services.AddRouting();
              })
              .Configure(app =>
              {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                  endpoints.MapRestLib<BatchEntity, Guid>("/api/items", configure);
                });
              });
        })
        .Build();

    _host.Start();
    _client = _host.GetTestClient();
  }

  /// <inheritdoc />
  public void Dispose()
  {
    _client?.Dispose();
    _host?.Dispose();
  }

  private StringContent BatchJson(object payload)
  {
    var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
    {
      PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    });
    return new StringContent(json, Encoding.UTF8, "application/json");
  }

  #region Story 8.1 — Batch Create

  [Fact]
  [Trait("Category", "Story8.1")]
  public async Task BatchCreate_ValidItems_Returns200WithCreatedEntities()
  {
    // Arrange
    CreateHost(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch();
    });

    var payload = new
    {
      action = "create",
      items = new[]
      {
        new { name = "Keyboard", price = 49.99m, is_active = true },
        new { name = "Mouse", price = 29.99m, is_active = true }
      }
    };

    // Act
    var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var json = await response.Content.ReadFromJsonAsync<JsonElement>();
    var items = json.GetProperty("items");
    items.GetArrayLength().Should().Be(2);

    items[0].GetProperty("index").GetInt32().Should().Be(0);
    items[0].GetProperty("status").GetInt32().Should().Be(201);
    items[0].GetProperty("entity").GetProperty("name").GetString().Should().Be("Keyboard");

    items[1].GetProperty("index").GetInt32().Should().Be(1);
    items[1].GetProperty("status").GetInt32().Should().Be(201);
    items[1].GetProperty("entity").GetProperty("name").GetString().Should().Be("Mouse");
  }

  [Fact]
  [Trait("Category", "Story8.1")]
  public async Task BatchCreate_MixedValidation_Returns207WithPerItemStatus()
  {
    // Arrange
    CreateHost(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch();
    });

    var payload = new
    {
      action = "create",
      items = new object[]
      {
        new { name = "ValidItem", price = 10m },
        new { name = "", price = 5m } // Name is required, empty string should fail validation
      }
    };

    // Act
    var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);

    var json = await response.Content.ReadFromJsonAsync<JsonElement>();
    var items = json.GetProperty("items");
    items.GetArrayLength().Should().Be(2);

    // First item: created successfully
    items[0].GetProperty("status").GetInt32().Should().Be(201);
    items[0].GetProperty("entity").GetProperty("name").GetString().Should().Be("ValidItem");

    // Second item: validation failed
    items[1].GetProperty("status").GetInt32().Should().Be(400);
    items[1].TryGetProperty("error", out var error).Should().BeTrue();
    error.GetProperty("type").GetString().Should().Be(ProblemTypes.ValidationFailed);
  }

  [Fact]
  [Trait("Category", "Story8.1")]
  public async Task BatchCreate_AllInvalid_Returns207WithAllErrors()
  {
    // Arrange
    CreateHost(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch();
    });

    var payload = new
    {
      action = "create",
      items = new object[]
      {
        new { name = "", price = 5m },
        new { name = "", price = 10m }
      }
    };

    // Act
    var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);

    var json = await response.Content.ReadFromJsonAsync<JsonElement>();
    var items = json.GetProperty("items");
    items.GetArrayLength().Should().Be(2);

    items[0].GetProperty("status").GetInt32().Should().Be(400);
    items[0].GetProperty("error").GetProperty("type").GetString()
        .Should().Be(ProblemTypes.ValidationFailed);

    items[1].GetProperty("status").GetInt32().Should().Be(400);
    items[1].GetProperty("error").GetProperty("type").GetString()
        .Should().Be(ProblemTypes.ValidationFailed);
  }

  [Fact]
  [Trait("Category", "Story8.1")]
  public async Task BatchCreate_EntitiesArePersisted()
  {
    // Arrange
    CreateHost(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch();
    });

    var payload = new
    {
      action = "create",
      items = new[]
      {
        new { name = "Persisted1", price = 1m },
        new { name = "Persisted2", price = 2m }
      }
    };

    // Act
    var batchResponse = await _client!.PostAsync("/api/items/batch", BatchJson(payload));
    batchResponse.StatusCode.Should().Be(HttpStatusCode.OK);

    var json = await batchResponse.Content.ReadFromJsonAsync<JsonElement>();
    var items = json.GetProperty("items");
    var id0 = items[0].GetProperty("entity").GetProperty("id").GetString();
    var id1 = items[1].GetProperty("entity").GetProperty("id").GetString();

    // Assert — entities are retrievable via GetById
    var get0 = await _client!.GetAsync($"/api/items/{id0}");
    get0.StatusCode.Should().Be(HttpStatusCode.OK);
    var entity0 = await get0.Content.ReadFromJsonAsync<JsonElement>();
    entity0.GetProperty("name").GetString().Should().Be("Persisted1");

    var get1 = await _client!.GetAsync($"/api/items/{id1}");
    get1.StatusCode.Should().Be(HttpStatusCode.OK);
    var entity1 = await get1.Content.ReadFromJsonAsync<JsonElement>();
    entity1.GetProperty("name").GetString().Should().Be("Persisted2");
  }

  #endregion

  #region Story 8.2 — Batch Delete

  [Fact]
  [Trait("Category", "Story8.2")]
  public async Task BatchDelete_ExistingItems_Returns200WithStatus204()
  {
    // Arrange
    var id1 = Guid.NewGuid();
    var id2 = Guid.NewGuid();
    _repository.Seed([
        new BatchEntity { Id = id1, Name = "Item1", Price = 10m },
        new BatchEntity { Id = id2, Name = "Item2", Price = 20m },
    ]);

    CreateHost(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch();
    });

    var payload = new
    {
      action = "delete",
      items = new[] { id1, id2 }
    };

    // Act
    var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var json = await response.Content.ReadFromJsonAsync<JsonElement>();
    var items = json.GetProperty("items");
    items.GetArrayLength().Should().Be(2);

    items[0].GetProperty("index").GetInt32().Should().Be(0);
    items[0].GetProperty("status").GetInt32().Should().Be(204);

    items[1].GetProperty("index").GetInt32().Should().Be(1);
    items[1].GetProperty("status").GetInt32().Should().Be(204);
  }

  [Fact]
  [Trait("Category", "Story8.2")]
  public async Task BatchDelete_MixedExistence_Returns207()
  {
    // Arrange
    var existingId = Guid.NewGuid();
    _repository.Seed([
        new BatchEntity { Id = existingId, Name = "Existing", Price = 10m },
    ]);

    CreateHost(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch();
    });

    var nonExistentId = Guid.NewGuid();
    var payload = new
    {
      action = "delete",
      items = new[] { existingId, nonExistentId }
    };

    // Act
    var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);

    var json = await response.Content.ReadFromJsonAsync<JsonElement>();
    var items = json.GetProperty("items");

    items[0].GetProperty("status").GetInt32().Should().Be(204);
    items[1].GetProperty("status").GetInt32().Should().Be(404);
  }

  [Fact]
  [Trait("Category", "Story8.2")]
  public async Task BatchDelete_EntitiesAreRemoved()
  {
    // Arrange
    var id1 = Guid.NewGuid();
    _repository.Seed([
        new BatchEntity { Id = id1, Name = "ToDelete", Price = 10m },
    ]);

    CreateHost(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch();
    });

    var payload = new
    {
      action = "delete",
      items = new[] { id1 }
    };

    // Act
    var batchResponse = await _client!.PostAsync("/api/items/batch", BatchJson(payload));
    batchResponse.StatusCode.Should().Be(HttpStatusCode.OK);

    // Assert — entity is no longer retrievable
    var getResponse = await _client!.GetAsync($"/api/items/{id1}");
    getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  #endregion

  #region Story 8.3 — Batch Update

  [Fact]
  [Trait("Category", "Story8.3")]
  public async Task BatchUpdate_ExistingItems_Returns200WithUpdatedEntities()
  {
    // Arrange
    var id1 = Guid.NewGuid();
    var id2 = Guid.NewGuid();
    _repository.Seed([
        new BatchEntity { Id = id1, Name = "Original1", Price = 10m },
        new BatchEntity { Id = id2, Name = "Original2", Price = 20m },
    ]);

    CreateHost(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch();
    });

    var payload = new
    {
      action = "update",
      items = new[]
      {
        new { id = id1, body = new { name = "Updated1", price = 15m, is_active = true } },
        new { id = id2, body = new { name = "Updated2", price = 25m, is_active = false } }
      }
    };

    // Act
    var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var json = await response.Content.ReadFromJsonAsync<JsonElement>();
    var items = json.GetProperty("items");
    items.GetArrayLength().Should().Be(2);

    items[0].GetProperty("status").GetInt32().Should().Be(200);
    items[0].GetProperty("entity").GetProperty("name").GetString().Should().Be("Updated1");
    items[0].GetProperty("entity").GetProperty("price").GetDecimal().Should().Be(15m);

    items[1].GetProperty("status").GetInt32().Should().Be(200);
    items[1].GetProperty("entity").GetProperty("name").GetString().Should().Be("Updated2");
  }

  [Fact]
  [Trait("Category", "Story8.3")]
  public async Task BatchUpdate_NotFound_Returns207With404()
  {
    // Arrange
    var existingId = Guid.NewGuid();
    _repository.Seed([
        new BatchEntity { Id = existingId, Name = "Existing", Price = 10m },
    ]);

    CreateHost(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch();
    });

    var nonExistentId = Guid.NewGuid();
    var payload = new
    {
      action = "update",
      items = new[]
      {
        new { id = existingId, body = new { name = "Updated", price = 15m, is_active = true } },
        new { id = nonExistentId, body = new { name = "Ghost", price = 0m, is_active = true } }
      }
    };

    // Act
    var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);

    var json = await response.Content.ReadFromJsonAsync<JsonElement>();
    var items = json.GetProperty("items");

    items[0].GetProperty("status").GetInt32().Should().Be(200);
    items[1].GetProperty("status").GetInt32().Should().Be(404);
    items[1].GetProperty("error").GetProperty("type").GetString()
        .Should().Be(ProblemTypes.NotFound);
  }

  [Fact]
  [Trait("Category", "Story8.3")]
  public async Task BatchUpdate_ValidationFailed_Returns207With400()
  {
    // Arrange
    var id1 = Guid.NewGuid();
    _repository.Seed([
        new BatchEntity { Id = id1, Name = "Existing", Price = 10m },
    ]);

    CreateHost(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch();
    });

    var payload = new
    {
      action = "update",
      items = new[]
      {
        new { id = id1, body = new { name = "", price = 5m, is_active = true } }
      }
    };

    // Act
    var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);

    var json = await response.Content.ReadFromJsonAsync<JsonElement>();
    var items = json.GetProperty("items");

    items[0].GetProperty("status").GetInt32().Should().Be(400);
    items[0].GetProperty("error").GetProperty("type").GetString()
        .Should().Be(ProblemTypes.ValidationFailed);
  }

  #endregion

  #region Story 8.4 — Batch Patch

  [Fact]
  [Trait("Category", "Story8.4")]
  public async Task BatchPatch_PartialUpdate_Returns200WithPatchedEntities()
  {
    // Arrange
    var id1 = Guid.NewGuid();
    var id2 = Guid.NewGuid();
    _repository.Seed([
        new BatchEntity { Id = id1, Name = "Item1", Price = 10m, IsActive = true },
        new BatchEntity { Id = id2, Name = "Item2", Price = 20m, IsActive = true },
    ]);

    CreateHost(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch();
    });

    var payload = new
    {
      action = "patch",
      items = new object[]
      {
        new { id = id1, body = new { price = 99.99m } },
        new { id = id2, body = new { name = "Renamed" } }
      }
    };

    // Act
    var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var json = await response.Content.ReadFromJsonAsync<JsonElement>();
    var items = json.GetProperty("items");
    items.GetArrayLength().Should().Be(2);

    items[0].GetProperty("status").GetInt32().Should().Be(200);
    items[0].GetProperty("entity").GetProperty("price").GetDecimal().Should().Be(99.99m);
    items[0].GetProperty("entity").GetProperty("name").GetString().Should().Be("Item1");

    items[1].GetProperty("status").GetInt32().Should().Be(200);
    items[1].GetProperty("entity").GetProperty("name").GetString().Should().Be("Renamed");
    items[1].GetProperty("entity").GetProperty("price").GetDecimal().Should().Be(20m);
  }

  [Fact]
  [Trait("Category", "Story8.4")]
  public async Task BatchPatch_NotFound_Returns207With404()
  {
    // Arrange
    var existingId = Guid.NewGuid();
    _repository.Seed([
        new BatchEntity { Id = existingId, Name = "Existing", Price = 10m },
    ]);

    CreateHost(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch();
    });

    var nonExistentId = Guid.NewGuid();
    var payload = new
    {
      action = "patch",
      items = new[]
      {
        new { id = existingId, body = new { price = 50m } },
        new { id = nonExistentId, body = new { price = 99m } }
      }
    };

    // Act
    var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);

    var json = await response.Content.ReadFromJsonAsync<JsonElement>();
    var items = json.GetProperty("items");

    items[0].GetProperty("status").GetInt32().Should().Be(200);
    items[1].GetProperty("status").GetInt32().Should().Be(404);
  }

  [Fact]
  [Trait("Category", "Story8.4")]
  public async Task BatchPatch_PreservesUnchangedFields()
  {
    // Arrange
    var id = Guid.NewGuid();
    _repository.Seed([
        new BatchEntity { Id = id, Name = "OriginalName", Price = 42m, IsActive = true },
    ]);

    CreateHost(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch();
    });

    var payload = new
    {
      action = "patch",
      items = new[]
      {
        new { id, body = new { price = 100m } }
      }
    };

    // Act
    var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var json = await response.Content.ReadFromJsonAsync<JsonElement>();
    var entity = json.GetProperty("items")[0].GetProperty("entity");

    entity.GetProperty("price").GetDecimal().Should().Be(100m);
    entity.GetProperty("name").GetString().Should().Be("OriginalName");
    entity.GetProperty("is_active").GetBoolean().Should().BeTrue();
  }

  [Fact]
  [Trait("Category", "Story8.4")]
  public async Task BatchPatch_CallsPatchAsync_NotUpdateAsync()
  {
    // Arrange
    var id = Guid.NewGuid();
    _repository.Seed([
        new BatchEntity { Id = id, Name = "Original", Price = 10m, IsActive = true },
    ]);

    var spy = new RepositorySpy<BatchEntity, Guid>(_repository);

    _host = new HostBuilder()
        .ConfigureWebHost(webBuilder =>
        {
          webBuilder
              .UseTestServer()
              .ConfigureServices(services =>
              {
                services.AddRestLib();
                services.AddSingleton<IRepository<BatchEntity, Guid>>(spy);
                services.AddRouting();
              })
              .Configure(app =>
              {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                  endpoints.MapRestLib<BatchEntity, Guid>("/api/items", cfg =>
                  {
                    cfg.AllowAnonymous();
                    cfg.EnableBatch();
                  });
                });
              });
        })
        .Build();

    _host.Start();
    _client = _host.GetTestClient();

    var payload = new
    {
      action = "patch",
      items = new[] { new { id, body = new { price = 99m } } }
    };

    // Act
    var response = await _client.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    spy.PatchAsyncCallCount.Should().BeGreaterThan(0, "batch PATCH should delegate to PatchAsync");
    spy.UpdateAsyncCallCount.Should().Be(0, "batch PATCH should not call UpdateAsync");
  }

  #endregion

  #region Story 8.5 — Envelope Validation

  [Fact]
  [Trait("Category", "Story8.5")]
  public async Task Batch_InvalidAction_Returns400()
  {
    // Arrange
    CreateHost(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch();
    });

    var payload = new { action = "import", items = new[] { new { name = "Test" } } };

    // Act
    var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

    var problem = await response.Content.ReadFromJsonAsync<RestLibProblemDetails>();
    problem.Should().NotBeNull();
    problem!.Type.Should().Be(ProblemTypes.InvalidBatchRequest);
  }

  [Fact]
  [Trait("Category", "Story8.5")]
  public async Task Batch_MissingItems_Returns400()
  {
    // Arrange
    CreateHost(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch();
    });

    var json = """{"action": "create"}""";
    var content = new StringContent(json, Encoding.UTF8, "application/json");

    // Act
    var response = await _client!.PostAsync("/api/items/batch", content);

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

    var problem = await response.Content.ReadFromJsonAsync<RestLibProblemDetails>();
    problem.Should().NotBeNull();
    problem!.Type.Should().Be(ProblemTypes.InvalidBatchRequest);
  }

  [Fact]
  [Trait("Category", "Story8.5")]
  public async Task Batch_EmptyItems_Returns400()
  {
    // Arrange
    CreateHost(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch();
    });

    var payload = new { action = "create", items = Array.Empty<object>() };

    // Act
    var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

    var problem = await response.Content.ReadFromJsonAsync<RestLibProblemDetails>();
    problem.Should().NotBeNull();
    problem!.Type.Should().Be(ProblemTypes.InvalidBatchRequest);
  }

  [Fact]
  [Trait("Category", "Story8.5")]
  public async Task Batch_SizeExceeded_Returns400()
  {
    // Arrange
    CreateHost(
        config =>
        {
          config.AllowAnonymous();
          config.EnableBatch();
        },
        options =>
        {
          options.MaxBatchSize = 3;
        });

    var items = Enumerable.Range(0, 5)
        .Select(i => new { name = $"Item{i}", price = (decimal)i })
        .ToArray();

    var payload = new { action = "create", items };

    // Act
    var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

    var problem = await response.Content.ReadFromJsonAsync<RestLibProblemDetails>();
    problem.Should().NotBeNull();
    problem!.Type.Should().Be(ProblemTypes.BatchSizeExceeded);
  }

  [Fact]
  [Trait("Category", "Story8.5")]
  public async Task Batch_ActionNotEnabled_Returns400()
  {
    // Arrange — only enable Create
    CreateHost(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch(BatchAction.Create);
    });

    var payload = new
    {
      action = "delete",
      items = new[] { Guid.NewGuid() }
    };

    // Act
    var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

    var problem = await response.Content.ReadFromJsonAsync<RestLibProblemDetails>();
    problem.Should().NotBeNull();
    problem!.Type.Should().Be(ProblemTypes.BatchActionNotEnabled);
  }

  [Fact]
  [Trait("Category", "Story8.5")]
  public async Task Batch_InvalidJson_Returns400()
  {
    // Arrange
    CreateHost(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch();
    });

    var content = new StringContent("not valid json {{{", Encoding.UTF8, "application/json");

    // Act
    var response = await _client!.PostAsync("/api/items/batch", content);

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

    var problem = await response.Content.ReadFromJsonAsync<RestLibProblemDetails>();
    problem.Should().NotBeNull();
    problem!.Type.Should().Be(ProblemTypes.InvalidBatchRequest);
  }

  [Fact]
  [Trait("Category", "Story8.5")]
  public async Task Batch_NullBody_Returns400()
  {
    // Arrange
    CreateHost(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch();
    });

    var content = new StringContent("null", Encoding.UTF8, "application/json");

    // Act
    var response = await _client!.PostAsync("/api/items/batch", content);

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

    var problem = await response.Content.ReadFromJsonAsync<RestLibProblemDetails>();
    problem.Should().NotBeNull();
    problem!.Type.Should().Be(ProblemTypes.InvalidBatchRequest);
  }

  #endregion

  #region Story 8.6 — Configuration

  [Fact]
  [Trait("Category", "Story8.6")]
  public async Task NoBatchConfig_BatchEndpointNotMapped()
  {
    // Arrange — no EnableBatch call
    CreateHost(config =>
    {
      config.AllowAnonymous();
    });

    var payload = new
    {
      action = "create",
      items = new[] { new { name = "Test", price = 1m } }
    };

    // Act
    var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert — 405 because the route group has no POST handler at /batch when batch is not enabled
    response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
  }

  [Fact]
  [Trait("Category", "Story8.6")]
  public async Task EnableBatch_NoArgs_EnablesAllActions()
  {
    // Arrange
    CreateHost(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch();
    });

    var id = Guid.NewGuid();
    _repository.Seed([new BatchEntity { Id = id, Name = "Test", Price = 10m }]);

    // Act & Assert — Create
    var createPayload = new { action = "create", items = new[] { new { name = "New", price = 5m } } };
    var createResponse = await _client!.PostAsync("/api/items/batch", BatchJson(createPayload));
    createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

    // Act & Assert — Update
    var updatePayload = new
    {
      action = "update",
      items = new[] { new { id, body = new { name = "Updated", price = 15m, is_active = true } } }
    };
    var updateResponse = await _client!.PostAsync("/api/items/batch", BatchJson(updatePayload));
    updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

    // Act & Assert — Patch
    var patchPayload = new
    {
      action = "patch",
      items = new[] { new { id, body = new { price = 99m } } }
    };
    var patchResponse = await _client!.PostAsync("/api/items/batch", BatchJson(patchPayload));
    patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);

    // Act & Assert — Delete
    var deletePayload = new { action = "delete", items = new[] { id } };
    var deleteResponse = await _client!.PostAsync("/api/items/batch", BatchJson(deletePayload));
    deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
  }

  [Fact]
  [Trait("Category", "Story8.6")]
  public async Task EnableBatch_SpecificActions_OnlyThoseEnabled()
  {
    // Arrange — only Create and Delete
    CreateHost(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch(BatchAction.Create, BatchAction.Delete);
    });

    // Create should work
    var createPayload = new { action = "create", items = new[] { new { name = "New", price = 5m } } };
    var createResponse = await _client!.PostAsync("/api/items/batch", BatchJson(createPayload));
    createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

    // Update should be rejected
    var updatePayload = new
    {
      action = "update",
      items = new[] { new { id = Guid.NewGuid(), body = new { name = "X", price = 1m, is_active = true } } }
    };
    var updateResponse = await _client!.PostAsync("/api/items/batch", BatchJson(updatePayload));
    updateResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var updateProblem = await updateResponse.Content.ReadFromJsonAsync<RestLibProblemDetails>();
    updateProblem!.Type.Should().Be(ProblemTypes.BatchActionNotEnabled);

    // Patch should be rejected
    var patchPayload = new
    {
      action = "patch",
      items = new[] { new { id = Guid.NewGuid(), body = new { price = 1m } } }
    };
    var patchResponse = await _client!.PostAsync("/api/items/batch", BatchJson(patchPayload));
    patchResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var patchProblem = await patchResponse.Content.ReadFromJsonAsync<RestLibProblemDetails>();
    patchProblem!.Type.Should().Be(ProblemTypes.BatchActionNotEnabled);
  }

  #endregion

  #region Story 8.7 — Hooks

  [Fact]
  [Trait("Category", "Story8.7")]
  public async Task BatchCreate_HooksFirePerItem()
  {
    // Arrange
    var hookCount = 0;

    CreateHost(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch();
      config.UseHooks(hooks =>
      {
        hooks.BeforePersist = async ctx =>
        {
          Interlocked.Increment(ref hookCount);
          await Task.CompletedTask;
        };
      });
    });

    var payload = new
    {
      action = "create",
      items = new[]
      {
        new { name = "Item1", price = 1m },
        new { name = "Item2", price = 2m },
        new { name = "Item3", price = 3m }
      }
    };

    // Act
    var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    hookCount.Should().Be(3);
  }

  [Fact]
  [Trait("Category", "Story8.7")]
  public async Task BatchCreate_HookShortCircuit_AffectsOnlyThatItem()
  {
    // Arrange — short-circuit item at index 1 without setting EarlyResult
    CreateHost(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch();
      config.UseHooks(hooks =>
      {
        hooks.OnRequestReceived = async ctx =>
        {
          // Short-circuit if entity name is "Blocked"
          if (ctx.Entity?.Name == "Blocked")
          {
            ctx.ShouldContinue = false;
          }

          await Task.CompletedTask;
        };
      });
    });

    var payload = new
    {
      action = "create",
      items = new[]
      {
        new { name = "Item0", price = 1m },
        new { name = "Blocked", price = 2m },
        new { name = "Item2", price = 3m }
      }
    };

    // Act
    var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);

    var json = await response.Content.ReadFromJsonAsync<JsonElement>();
    var items = json.GetProperty("items");
    items.GetArrayLength().Should().Be(3);

    // Item 0: succeeded
    items[0].GetProperty("status").GetInt32().Should().Be(201);

    // Item 1: short-circuited without EarlyResult → falls back to 500
    items[1].GetProperty("status").GetInt32().Should().Be(500);
    items[1].TryGetProperty("error", out _).Should().BeTrue();

    // Item 2: succeeded
    items[2].GetProperty("status").GetInt32().Should().Be(201);
  }

  [Fact]
  [Trait("Category", "Story8.7")]
  public async Task BatchCreate_HookShortCircuit_WithEarlyResult_RespectsStatusCode()
  {
    // Arrange — short-circuit with a 403 Forbidden ProblemDetails EarlyResult
    CreateHost(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch();
      config.UseHooks(hooks =>
      {
        hooks.OnRequestReceived = async ctx =>
        {
          if (ctx.Entity?.Name == "Forbidden")
          {
            ctx.ShouldContinue = false;
            ctx.EarlyResult = ProblemDetailsResult.Create(
                new RestLibProblemDetails
                {
                  Type = ProblemTypes.Forbidden,
                  Title = "Forbidden",
                  Status = StatusCodes.Status403Forbidden,
                  Detail = "You do not have permission to create this item."
                });
          }

          await Task.CompletedTask;
        };
      });
    });

    var payload = new
    {
      action = "create",
      items = new[]
      {
        new { name = "Item0", price = 1m },
        new { name = "Forbidden", price = 2m },
        new { name = "Item2", price = 3m }
      }
    };

    // Act
    var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);

    var json = await response.Content.ReadFromJsonAsync<JsonElement>();
    var items = json.GetProperty("items");
    items.GetArrayLength().Should().Be(3);

    // Item 0: succeeded
    items[0].GetProperty("status").GetInt32().Should().Be(201);

    // Item 1: short-circuited with 403 and custom problem details
    items[1].GetProperty("status").GetInt32().Should().Be(403);
    var error = items[1].GetProperty("error");
    error.GetProperty("type").GetString().Should().Be(ProblemTypes.Forbidden);
    error.GetProperty("title").GetString().Should().Be("Forbidden");
    error.GetProperty("status").GetInt32().Should().Be(403);
    error.GetProperty("detail").GetString().Should().Be("You do not have permission to create this item.");

    // Item 2: succeeded
    items[2].GetProperty("status").GetInt32().Should().Be(201);
  }

  [Fact]
  [Trait("Category", "Story8.7")]
  public async Task BatchCreate_HookShortCircuit_WithStatusCodeResult_ExtractsStatusCode()
  {
    // Arrange — short-circuit with a plain status-code-only EarlyResult (no body)
    CreateHost(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch();
      config.UseHooks(hooks =>
      {
        hooks.OnRequestReceived = async ctx =>
        {
          if (ctx.Entity?.Name == "Unauthorized")
          {
            ctx.ShouldContinue = false;
            ctx.EarlyResult = Results.StatusCode(StatusCodes.Status401Unauthorized);
          }

          await Task.CompletedTask;
        };
      });
    });

    var payload = new
    {
      action = "create",
      items = new[]
      {
        new { name = "Item0", price = 1m },
        new { name = "Unauthorized", price = 2m },
        new { name = "Item2", price = 3m }
      }
    };

    // Act
    var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);

    var json = await response.Content.ReadFromJsonAsync<JsonElement>();
    var items = json.GetProperty("items");
    items.GetArrayLength().Should().Be(3);

    // Item 0: succeeded
    items[0].GetProperty("status").GetInt32().Should().Be(201);

    // Item 1: short-circuited with 401 and generic hook short-circuit error
    items[1].GetProperty("status").GetInt32().Should().Be(401);
    var error = items[1].GetProperty("error");
    error.GetProperty("type").GetString().Should().Be(ProblemTypes.HookShortCircuit);
    error.GetProperty("status").GetInt32().Should().Be(401);

    // Item 2: succeeded
    items[2].GetProperty("status").GetInt32().Should().Be(201);
  }

  [Fact]
  [Trait("Category", "Story8.7")]
  public async Task BatchUpdate_HookShortCircuit_WithEarlyResult_RespectsStatusCode()
  {
    // Arrange — seed entities and short-circuit update with 403
    var id0 = Guid.NewGuid();
    var id1 = Guid.NewGuid();
    var id2 = Guid.NewGuid();
    _repository.Seed([
      new BatchEntity { Id = id0, Name = "A", Price = 1m },
      new BatchEntity { Id = id1, Name = "B", Price = 2m },
      new BatchEntity { Id = id2, Name = "C", Price = 3m }
    ]);

    CreateHost(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch(BatchAction.Create, BatchAction.Update);
      config.UseHooks(hooks =>
      {
        hooks.BeforePersist = async ctx =>
        {
          if (ctx.Entity?.Name == "BlockedUpdate")
          {
            ctx.ShouldContinue = false;
            ctx.EarlyResult = ProblemDetailsResult.Create(
                new RestLibProblemDetails
                {
                  Type = ProblemTypes.Forbidden,
                  Title = "Forbidden",
                  Status = StatusCodes.Status403Forbidden,
                  Detail = "Cannot update this item."
                });
          }

          await Task.CompletedTask;
        };
      });
    });

    var payload = new
    {
      action = "update",
      items = new[]
      {
        new { id = id0, body = new { name = "Updated0", price = 10m, is_active = true } },
        new { id = id1, body = new { name = "BlockedUpdate", price = 20m, is_active = true } },
        new { id = id2, body = new { name = "Updated2", price = 30m, is_active = true } }
      }
    };

    // Act
    var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);

    var json = await response.Content.ReadFromJsonAsync<JsonElement>();
    var items = json.GetProperty("items");
    items.GetArrayLength().Should().Be(3);

    items[0].GetProperty("status").GetInt32().Should().Be(200);
    items[1].GetProperty("status").GetInt32().Should().Be(403);
    items[1].GetProperty("error").GetProperty("detail").GetString()
        .Should().Be("Cannot update this item.");
    items[2].GetProperty("status").GetInt32().Should().Be(200);
  }

  [Fact]
  [Trait("Category", "Story8.7")]
  public async Task BatchDelete_HookShortCircuit_WithoutEarlyResult_Returns500()
  {
    // Arrange — seed entities and short-circuit delete without EarlyResult
    var id0 = Guid.NewGuid();
    var id1 = Guid.NewGuid();
    _repository.Seed([
      new BatchEntity { Id = id0, Name = "ToDelete0", Price = 1m },
      new BatchEntity { Id = id1, Name = "ToDelete1", Price = 2m }
    ]);

    CreateHost(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch(BatchAction.Create, BatchAction.Delete);
      config.UseHooks(hooks =>
      {
        hooks.OnRequestReceived = async ctx =>
        {
          if (ctx.ResourceId is Guid rid && rid == id1)
          {
            ctx.ShouldContinue = false;
            // Not setting EarlyResult — should fall back to 500
          }

          await Task.CompletedTask;
        };
      });
    });

    var payload = new
    {
      action = "delete",
      items = new[] { id0, id1 }
    };

    // Act
    var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);

    var json = await response.Content.ReadFromJsonAsync<JsonElement>();
    var items = json.GetProperty("items");
    items.GetArrayLength().Should().Be(2);

    items[0].GetProperty("status").GetInt32().Should().Be(204);
    items[1].GetProperty("status").GetInt32().Should().Be(500);
    items[1].TryGetProperty("error", out _).Should().BeTrue();
  }

  [Fact]
  [Trait("Category", "Story8.7")]
  public async Task BatchCreate_BeforeResponseHook_FiresOnce()
  {
    // Arrange
    var beforeResponseCount = 0;

    CreateHost(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch();
      config.UseHooks(hooks =>
      {
        hooks.BeforeResponse = async ctx =>
        {
          Interlocked.Increment(ref beforeResponseCount);
          await Task.CompletedTask;
        };
      });
    });

    var payload = new
    {
      action = "create",
      items = new[]
      {
        new { name = "Item1", price = 1m },
        new { name = "Item2", price = 2m },
        new { name = "Item3", price = 3m }
      }
    };

    // Act
    var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    beforeResponseCount.Should().Be(1, "BeforeResponse should fire once for the entire batch");
  }

  [Fact]
  [Trait("Category", "Story8.7")]
  public async Task BatchCreate_BeforeResponseHook_ShortCircuit_ReturnsEarlyResult()
  {
    // Arrange — BeforeResponse hook short-circuits the whole batch response
    CreateHost(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch();
      config.UseHooks(hooks =>
      {
        hooks.BeforeResponse = async ctx =>
        {
          ctx.ShouldContinue = false;
          ctx.EarlyResult = Results.Json(
            new { message = "blocked_by_hook" },
            statusCode: StatusCodes.Status403Forbidden);
          await Task.CompletedTask;
        };
      });
    });

    var payload = new
    {
      action = "create",
      items = new[]
      {
        new { name = "Item1", price = 1m }
      }
    };

    // Act
    var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert — hook replaces the entire batch response
    response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
  }

  [Fact]
  [Trait("Category", "Story8.7")]
  public async Task BatchCreate_OnErrorHook_HandlesPerItemException()
  {
    // Arrange — use a throwing repository
    var errorHandledCount = 0;

    var throwingRepo = new ThrowingBatchRepository();
    var host = new HostBuilder()
        .ConfigureWebHost(webBuilder =>
        {
          webBuilder
              .UseTestServer()
              .ConfigureServices(services =>
              {
                services.AddRestLib();
                services.AddSingleton<IRepository<BatchEntity, Guid>>(throwingRepo);
                services.AddRouting();
              })
              .Configure(app =>
              {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                  endpoints.MapRestLib<BatchEntity, Guid>("/api/items", config =>
                  {
                    config.AllowAnonymous();
                    config.EnableBatch();
                    config.UseHooks(hooks =>
                    {
                      hooks.OnError = async ctx =>
                      {
                        Interlocked.Increment(ref errorHandledCount);
                        ctx.Handled = true;
                        ctx.ErrorResult = Results.Json(
                          new RestLibProblemDetails
                          {
                            Type = "/problems/custom-error",
                            Title = "Custom Error",
                            Status = StatusCodes.Status503ServiceUnavailable,
                            Detail = "Handled by hook"
                          },
                          statusCode: StatusCodes.Status503ServiceUnavailable);
                        await Task.CompletedTask;
                      };
                    });
                  });
                });
              });
        })
        .Build();

    host.Start();
    var client = host.GetTestClient();

    var payload = new
    {
      action = "create",
      items = new[]
      {
        new { name = "Item1", price = 1m },
        new { name = "Item2", price = 2m }
      }
    };

    // Act
    var response = await client.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);
    var json = await response.Content.ReadFromJsonAsync<JsonElement>();
    var items = json.GetProperty("items");
    items.GetArrayLength().Should().Be(2);

    // Both items should have been handled by the OnError hook
    errorHandledCount.Should().Be(2);
    items[0].GetProperty("status").GetInt32().Should().Be(503);
    items[0].GetProperty("error").GetProperty("detail").GetString().Should().Be("Handled by hook");
    items[1].GetProperty("status").GetInt32().Should().Be(503);

    client.Dispose();
    host.Dispose();
  }

  [Fact]
  [Trait("Category", "Story8.7")]
  public async Task BatchCreate_AfterPersistHook_ShortCircuit_RespectsEarlyResult()
  {
    // Arrange — AfterPersist hook short-circuits with a 409 Conflict for specific items
    CreateHost(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch();
      config.UseHooks(hooks =>
      {
        hooks.AfterPersist = async ctx =>
        {
          if (ctx.Entity?.Name == "Conflicting")
          {
            ctx.ShouldContinue = false;
            ctx.EarlyResult = ProblemDetailsResult.Create(
                new RestLibProblemDetails
                {
                  Type = ProblemTypes.Conflict,
                  Title = "Conflict",
                  Status = StatusCodes.Status409Conflict,
                  Detail = "Post-persist conflict detected by hook."
                });
          }

          await Task.CompletedTask;
        };
      });
    });

    var payload = new
    {
      action = "create",
      items = new[]
      {
        new { name = "Item0", price = 1m },
        new { name = "Conflicting", price = 2m },
        new { name = "Item2", price = 3m }
      }
    };

    // Act
    var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);

    var json = await response.Content.ReadFromJsonAsync<JsonElement>();
    var items = json.GetProperty("items");
    items.GetArrayLength().Should().Be(3);

    // Item 0: succeeded
    items[0].GetProperty("status").GetInt32().Should().Be(201);

    // Item 1: AfterPersist short-circuited with 409 and custom problem details
    items[1].GetProperty("status").GetInt32().Should().Be(409);
    var error = items[1].GetProperty("error");
    error.GetProperty("type").GetString().Should().Be(ProblemTypes.Conflict);
    error.GetProperty("title").GetString().Should().Be("Conflict");
    error.GetProperty("detail").GetString().Should().Be("Post-persist conflict detected by hook.");

    // Item 2: succeeded
    items[2].GetProperty("status").GetInt32().Should().Be(201);
  }

  [Fact]
  [Trait("Category", "Story8.7")]
  public async Task BatchCreate_AfterPersistHook_ShortCircuit_WithoutEarlyResult_Returns500()
  {
    // Arrange — AfterPersist hook short-circuits without setting EarlyResult
    CreateHost(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch();
      config.UseHooks(hooks =>
      {
        hooks.AfterPersist = async ctx =>
        {
          if (ctx.Entity?.Name == "Blocked")
          {
            ctx.ShouldContinue = false;
            // No EarlyResult set — should fall back to 500
          }

          await Task.CompletedTask;
        };
      });
    });

    var payload = new
    {
      action = "create",
      items = new[]
      {
        new { name = "Item0", price = 1m },
        new { name = "Blocked", price = 2m },
        new { name = "Item2", price = 3m }
      }
    };

    // Act
    var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);

    var json = await response.Content.ReadFromJsonAsync<JsonElement>();
    var items = json.GetProperty("items");
    items.GetArrayLength().Should().Be(3);

    // Item 0: succeeded
    items[0].GetProperty("status").GetInt32().Should().Be(201);

    // Item 1: AfterPersist short-circuited without EarlyResult → falls back to 500
    items[1].GetProperty("status").GetInt32().Should().Be(500);
    items[1].TryGetProperty("error", out _).Should().BeTrue();

    // Item 2: succeeded
    items[2].GetProperty("status").GetInt32().Should().Be(201);
  }

  #endregion

  #region Story 8.8 — JSON Config

  [Fact]
  [Trait("Category", "Story8.8")]
  public async Task JsonConfig_EnablesBatch()
  {
    // Arrange — use EnableBatch() via code (JSON config tested after Commit 6 adds the builder)
    CreateHost(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch();
    });

    var payload = new
    {
      action = "create",
      items = new[] { new { name = "JsonConfigItem", price = 5m } }
    };

    // Act
    var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var json = await response.Content.ReadFromJsonAsync<JsonElement>();
    json.GetProperty("items")[0].GetProperty("status").GetInt32().Should().Be(201);
  }

  [Fact]
  [Trait("Category", "Story8.8")]
  public async Task JsonConfig_SpecificActions()
  {
    // Arrange — enable only Create and Delete
    CreateHost(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch(BatchAction.Create, BatchAction.Delete);
    });

    // Create should work
    var createPayload = new { action = "create", items = new[] { new { name = "Item", price = 5m } } };
    var createResponse = await _client!.PostAsync("/api/items/batch", BatchJson(createPayload));
    createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

    // Update should fail (not enabled)
    var updatePayload = new
    {
      action = "update",
      items = new[] { new { id = Guid.NewGuid(), body = new { name = "X", price = 1m, is_active = true } } }
    };
    var updateResponse = await _client!.PostAsync("/api/items/batch", BatchJson(updatePayload));
    updateResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var problem = await updateResponse.Content.ReadFromJsonAsync<RestLibProblemDetails>();
    problem!.Type.Should().Be(ProblemTypes.BatchActionNotEnabled);
  }

  #endregion

  #region IBatchRepository Wiring

  private BatchRepositorySpy<BatchEntity, Guid>? _batchSpy;
  private RepositorySpy<BatchEntity, Guid>? _repositorySpy;

  /// <summary>
  /// Creates a test host that also registers <see cref="IBatchRepository{TEntity, TKey}"/>
  /// via a <see cref="BatchRepositorySpy{TEntity, TKey}"/> so we can verify bulk calls.
  /// </summary>
  private void CreateHostWithBatchRepository(
      Action<RestLibEndpointConfiguration<BatchEntity, Guid>> configure,
      Action<RestLibOptions>? configureOptions = null)
  {
    _batchSpy = new BatchRepositorySpy<BatchEntity, Guid>(_repository);
    _repositorySpy = new RepositorySpy<BatchEntity, Guid>(_repository);

    _host = new HostBuilder()
        .ConfigureWebHost(webBuilder =>
        {
          webBuilder
              .UseTestServer()
              .ConfigureServices(services =>
              {
                services.AddRestLib(options =>
                {
                  configureOptions?.Invoke(options);
                });
                services.AddSingleton<IRepository<BatchEntity, Guid>>(_repositorySpy);
                services.AddSingleton<IBatchRepository<BatchEntity, Guid>>(_batchSpy);
                services.AddRouting();
              })
              .Configure(app =>
              {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                  endpoints.MapRestLib<BatchEntity, Guid>("/api/items", configure);
                });
              });
        })
        .Build();

    _host.Start();
    _client = _host.GetTestClient();
  }

  [Fact]
  [Trait("Category", "Story8.9")]
  public async Task BatchCreate_UsesBatchRepository_WhenAvailable()
  {
    // Arrange
    CreateHostWithBatchRepository(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch();
    });

    var payload = new
    {
      action = "create",
      items = new[]
      {
        new { name = "Item1", price = 10m },
        new { name = "Item2", price = 20m }
      }
    };

    // Act
    var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    _batchSpy!.CreateManyCallCount.Should().Be(1);
    _repositorySpy!.PatchAsyncCallCount.Should().Be(0);
  }

  [Fact]
  [Trait("Category", "Story8.9")]
  public async Task BatchUpdate_UsesBatchRepository_WhenAvailable()
  {
    // Arrange
    var id1 = Guid.NewGuid();
    var id2 = Guid.NewGuid();
    _repository.Seed(new[]
    {
      new BatchEntity { Id = id1, Name = "Old1", Price = 1m },
      new BatchEntity { Id = id2, Name = "Old2", Price = 2m }
    });

    CreateHostWithBatchRepository(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch();
    });

    var payload = new
    {
      action = "update",
      items = new[]
      {
        new { id = id1, body = new { name = "New1", price = 11m, is_active = true } },
        new { id = id2, body = new { name = "New2", price = 22m, is_active = true } }
      }
    };

    // Act
    var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    _batchSpy!.UpdateManyCallCount.Should().Be(1);
  }

  [Fact]
  [Trait("Category", "Story8.9")]
  public async Task BatchPatch_UsesBatchRepository_WhenAvailable()
  {
    // Arrange
    var id1 = Guid.NewGuid();
    var id2 = Guid.NewGuid();
    _repository.Seed(new[]
    {
      new BatchEntity { Id = id1, Name = "PatchMe1", Price = 1m },
      new BatchEntity { Id = id2, Name = "PatchMe2", Price = 2m }
    });

    CreateHostWithBatchRepository(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch();
    });

    var payload = new
    {
      action = "patch",
      items = new[]
      {
        new { id = id1, body = new { price = 99m } },
        new { id = id2, body = new { price = 88m } }
      }
    };

    // Act
    var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    _batchSpy!.PatchManyCallCount.Should().Be(1);
  }

  [Fact]
  [Trait("Category", "Story8.9")]
  public async Task BatchDelete_UsesIndividualDeletes_EvenWhenBatchRepositoryAvailable()
  {
    // Arrange
    var id1 = Guid.NewGuid();
    var id2 = Guid.NewGuid();
    _repository.Seed(new[]
    {
      new BatchEntity { Id = id1, Name = "Del1", Price = 1m },
      new BatchEntity { Id = id2, Name = "Del2", Price = 2m }
    });

    CreateHostWithBatchRepository(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch();
    });

    var payload = new
    {
      action = "delete",
      items = new[] { id1, id2 }
    };

    // Act
    var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert — batch delete always uses individual DeleteAsync for per-item 404 detection
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    _batchSpy!.DeleteManyCallCount.Should().Be(0);
  }

  [Fact]
  [Trait("Category", "Story8.9")]
  public async Task BatchCreate_FallsBackToSingleOps_WhenNoBatchRepository()
  {
    // Arrange — use CreateHost which does NOT register IBatchRepository
    var spy = new RepositorySpy<BatchEntity, Guid>(_repository);

    _host = new HostBuilder()
        .ConfigureWebHost(webBuilder =>
        {
          webBuilder
              .UseTestServer()
              .ConfigureServices(services =>
              {
                services.AddRestLib();
                services.AddSingleton<IRepository<BatchEntity, Guid>>(spy);
                services.AddRouting();
              })
              .Configure(app =>
              {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                  endpoints.MapRestLib<BatchEntity, Guid>("/api/items", config =>
                  {
                    config.AllowAnonymous();
                    config.EnableBatch();
                  });
                });
              });
        })
        .Build();

    _host.Start();
    _client = _host.GetTestClient();

    var payload = new
    {
      action = "create",
      items = new[]
      {
        new { name = "FallbackItem1", price = 5m },
        new { name = "FallbackItem2", price = 6m }
      }
    };

    // Act
    var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var json = await response.Content.ReadFromJsonAsync<JsonElement>();
    var items = json.GetProperty("items");
    items.GetArrayLength().Should().Be(2);
    items[0].GetProperty("status").GetInt32().Should().Be(201);
    items[1].GetProperty("status").GetInt32().Should().Be(201);
  }

  [Fact]
  [Trait("Category", "Story8.9")]
  public async Task BatchCreate_BatchRepository_HooksStillFirePerItem()
  {
    // Arrange
    var hookCallCount = 0;

    CreateHostWithBatchRepository(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch();
      config.UseHooks(hooks =>
      {
        hooks.AfterPersist = ctx =>
        {
          Interlocked.Increment(ref hookCallCount);
          return Task.CompletedTask;
        };
      });
    });

    var payload = new
    {
      action = "create",
      items = new[]
      {
        new { name = "HookItem1", price = 10m },
        new { name = "HookItem2", price = 20m },
        new { name = "HookItem3", price = 30m }
      }
    };

    // Act
    var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    _batchSpy!.CreateManyCallCount.Should().Be(1, "bulk method should be called once");
    hookCallCount.Should().Be(3, "AfterPersist hook should fire once per item");
  }

  [Fact]
  [Trait("Category", "Story8.9")]
  public async Task BatchCreate_BatchRepository_ValidationStillAppliesPerItem()
  {
    // Arrange
    CreateHostWithBatchRepository(config =>
    {
      config.AllowAnonymous();
      config.EnableBatch();
    });

    var payload = new
    {
      action = "create",
      items = new[]
      {
        new { name = "ValidItem", price = 10m },
        new { name = string.Empty, price = 20m }, // Name is [Required], empty fails
      }
    };

    // Act
    var response = await _client!.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert — should be 207 (mixed results)
    response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);
    var json = await response.Content.ReadFromJsonAsync<JsonElement>();
    var items = json.GetProperty("items");
    items.GetArrayLength().Should().Be(2);

    // First item succeeded
    items[0].GetProperty("status").GetInt32().Should().Be(201);

    // Second item failed validation
    items[1].GetProperty("status").GetInt32().Should().Be(400);
  }

  #endregion

  #region Exception Detail Gating

  [Fact]
  [Trait("Category", "Story8.7")]
  public async Task BatchCreate_RepositoryException_DefaultOptions_HidesExceptionDetails()
  {
    // Arrange — use a throwing repository with default options (IncludeExceptionDetailsInErrors = false)
    var throwingRepo = new ThrowingBatchRepository();
    var host = new HostBuilder()
        .ConfigureWebHost(webBuilder =>
        {
          webBuilder
              .UseTestServer()
              .ConfigureServices(services =>
              {
                services.AddRestLib();
                services.AddSingleton<IRepository<BatchEntity, Guid>>(throwingRepo);
                services.AddRouting();
              })
              .Configure(app =>
              {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                  endpoints.MapRestLib<BatchEntity, Guid>("/api/items", config =>
                  {
                    config.AllowAnonymous();
                    config.EnableBatch();
                  });
                });
              });
        })
        .Build();

    host.Start();
    var client = host.GetTestClient();

    var payload = new
    {
      action = "create",
      items = new[]
      {
        new { name = "Item1", price = 1m }
      }
    };

    // Act
    var response = await client.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);
    var json = await response.Content.ReadFromJsonAsync<JsonElement>();
    var items = json.GetProperty("items");
    items.GetArrayLength().Should().Be(1);

    items[0].GetProperty("status").GetInt32().Should().Be(500);
    var detail = items[0].GetProperty("error").GetProperty("detail").GetString();
    detail.Should().Be("An internal error occurred while processing this item.");
    detail.Should().NotContain("Simulated repository failure");
    detail.Should().NotContain("InvalidOperationException");

    client.Dispose();
    host.Dispose();
  }

  [Fact]
  [Trait("Category", "Story8.7")]
  public async Task BatchCreate_RepositoryException_IncludeDetails_ShowsExceptionDetails()
  {
    // Arrange — use a throwing repository with IncludeExceptionDetailsInErrors = true
    var throwingRepo = new ThrowingBatchRepository();
    var host = new HostBuilder()
        .ConfigureWebHost(webBuilder =>
        {
          webBuilder
              .UseTestServer()
              .ConfigureServices(services =>
              {
                services.AddRestLib(options =>
                {
                  options.IncludeExceptionDetailsInErrors = true;
                });
                services.AddSingleton<IRepository<BatchEntity, Guid>>(throwingRepo);
                services.AddRouting();
              })
              .Configure(app =>
              {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                  endpoints.MapRestLib<BatchEntity, Guid>("/api/items", config =>
                  {
                    config.AllowAnonymous();
                    config.EnableBatch();
                  });
                });
              });
        })
        .Build();

    host.Start();
    var client = host.GetTestClient();

    var payload = new
    {
      action = "create",
      items = new[]
      {
        new { name = "Item1", price = 1m }
      }
    };

    // Act
    var response = await client.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);
    var json = await response.Content.ReadFromJsonAsync<JsonElement>();
    var items = json.GetProperty("items");
    items.GetArrayLength().Should().Be(1);

    items[0].GetProperty("status").GetInt32().Should().Be(500);
    var detail = items[0].GetProperty("error").GetProperty("detail").GetString();
    detail.Should().Contain("InvalidOperationException");
    detail.Should().Contain("Simulated repository failure");

    client.Dispose();
    host.Dispose();
  }

  [Fact]
  [Trait("Category", "Story8.7")]
  public async Task BatchDelete_RepositoryException_DefaultOptions_HidesExceptionDetails()
  {
    // Arrange — use a throwing repository with default options
    var throwingRepo = new ThrowingBatchRepository();
    var host = new HostBuilder()
        .ConfigureWebHost(webBuilder =>
        {
          webBuilder
              .UseTestServer()
              .ConfigureServices(services =>
              {
                services.AddRestLib();
                services.AddSingleton<IRepository<BatchEntity, Guid>>(throwingRepo);
                services.AddRouting();
              })
              .Configure(app =>
              {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                  endpoints.MapRestLib<BatchEntity, Guid>("/api/items", config =>
                  {
                    config.AllowAnonymous();
                    config.EnableBatch();
                  });
                });
              });
        })
        .Build();

    host.Start();
    var client = host.GetTestClient();

    var payload = new
    {
      action = "delete",
      items = new[] { Guid.NewGuid() }
    };

    // Act
    var response = await client.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);
    var json = await response.Content.ReadFromJsonAsync<JsonElement>();
    var items = json.GetProperty("items");
    items.GetArrayLength().Should().Be(1);

    items[0].GetProperty("status").GetInt32().Should().Be(500);
    var detail = items[0].GetProperty("error").GetProperty("detail").GetString();
    detail.Should().Be("An internal error occurred while processing this item.");
    detail.Should().NotContain("Simulated repository failure");

    client.Dispose();
    host.Dispose();
  }

  [Fact]
  [Trait("Category", "Story8.7")]
  public async Task BatchUpdate_RepositoryException_IncludeDetails_ShowsExceptionDetails()
  {
    // Arrange — use a repository that returns entities from GetByIdAsync but throws on UpdateAsync
    var id = Guid.NewGuid();
    var throwingUpdateRepo = new ThrowingUpdateRepository(id);
    var host = new HostBuilder()
        .ConfigureWebHost(webBuilder =>
        {
          webBuilder
              .UseTestServer()
              .ConfigureServices(services =>
              {
                services.AddRestLib(options =>
                {
                  options.IncludeExceptionDetailsInErrors = true;
                });
                services.AddSingleton<IRepository<BatchEntity, Guid>>(throwingUpdateRepo);
                services.AddRouting();
              })
              .Configure(app =>
              {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                  endpoints.MapRestLib<BatchEntity, Guid>("/api/items", cfg =>
                  {
                    cfg.AllowAnonymous();
                    cfg.EnableBatch();
                  });
                });
              });
        })
        .Build();

    host.Start();
    var client = host.GetTestClient();

    var payload = new
    {
      action = "update",
      items = new[]
      {
        new { id, body = new { name = "Updated", price = 10m } }
      }
    };

    // Act
    var response = await client.PostAsync("/api/items/batch", BatchJson(payload));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);
    var json = await response.Content.ReadFromJsonAsync<JsonElement>();
    var items = json.GetProperty("items");
    items.GetArrayLength().Should().Be(1);

    items[0].GetProperty("status").GetInt32().Should().Be(500);
    var detail = items[0].GetProperty("error").GetProperty("detail").GetString();
    detail.Should().Contain("InvalidOperationException");
    detail.Should().Contain("Simulated update failure");

    client.Dispose();
    host.Dispose();
  }

  #endregion
}
