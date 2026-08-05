using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using RestLib.Abstractions;
using RestLib.InMemory;
using RestLib.Pagination;
using RestLib.Responses;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Verifies that If-Match validation remains bound to the repository mutation.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "ConditionalRequests")]
public class AtomicConditionalWriteTests
{
    [Fact]
    public async Task InMemoryConditionalUpdate_TwoSimultaneousWriters_AllowsOnlyOneExpectedState()
    {
        // Arrange
        var id = Guid.NewGuid();
        var repository = new InMemoryRepository<ProductEntity, Guid>(entity => entity.Id, Guid.NewGuid);
        repository.Seed([CreateProduct(id, "Original")]);
        var conditionalRepository = (IConditionalWriteRepository<ProductEntity, Guid>)repository;
        using var ready = new CountdownEvent(2);
        using var start = new ManualResetEventSlim();

        Task<ConditionalWriteResult<ProductEntity>> StartWriter(string name)
        {
            return Task.Run(async () =>
            {
                ready.Signal();
                start.Wait();
                return await conditionalRepository.UpdateConditionallyAsync(
                    id,
                    CreateProduct(id, name),
                    current => current.ProductName == "Original");
            });
        }

        var first = StartWriter("First");
        var second = StartWriter("Second");
        ready.Wait();

        // Act
        start.Set();
        var results = await Task.WhenAll(first, second);

        // Assert
        results.Should().ContainSingle(result => result.Status == ConditionalWriteStatus.Succeeded);
        results.Should().ContainSingle(result => result.Status == ConditionalWriteStatus.PreconditionFailed);
        var persisted = await repository.GetByIdAsync(id);
        persisted!.ProductName.Should().BeOneOf("First", "Second");
    }

    [Fact]
    public async Task InMemoryConditionalMutations_FailedPreconditions_DoNotChangePersistence()
    {
        // Arrange
        var id = Guid.NewGuid();
        var repository = new InMemoryRepository<ProductEntity, Guid>(entity => entity.Id, Guid.NewGuid);
        repository.Seed([CreateProduct(id, "Original")]);
        var conditionalRepository = (IConditionalWriteRepository<ProductEntity, Guid>)repository;
        using var patch = JsonDocument.Parse("{\"product_name\":\"Patched\"}");

        // Act
        var update = await conditionalRepository.UpdateConditionallyAsync(
            id,
            CreateProduct(id, "Updated"),
            _ => false);
        var patchResult = await conditionalRepository.PatchConditionallyAsync(
            id,
            patch.RootElement,
            _ => false);
        var delete = await conditionalRepository.DeleteConditionallyAsync(id, _ => false);

        // Assert
        update.Status.Should().Be(ConditionalWriteStatus.PreconditionFailed);
        patchResult.Status.Should().Be(ConditionalWriteStatus.PreconditionFailed);
        delete.Status.Should().Be(ConditionalWriteStatus.PreconditionFailed);
        var persisted = await repository.GetByIdAsync(id);
        persisted!.ProductName.Should().Be("Original");
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task IfMatch_EntityChangesBeforeAtomicMutation_Returns412AndPreservesNewerState(
        string operation)
    {
        // Arrange
        var id = Guid.NewGuid();
        var repository = new InterleavingConditionalRepository(CreateProduct(id, "Original"));
        var (host, client) = await new TestHostBuilder<ProductEntity, Guid>(repository, "/api/products")
            .WithOptions(options => options.EnableETagSupport = true)
            .WithEndpoint(config => config.AllowAnonymous())
            .BuildAsync();
        using var hostHandle = host;
        using var clientHandle = client;
        var getResponse = await client.GetAsync($"/api/products/{id}");
        var etag = getResponse.Headers.ETag!.Tag;
        repository.ChangeBeforeConditionalWrite = true;
        using var request = CreateConditionalRequest(operation, id, etag);

        // Act
        var response = await client.SendAsync(request);

        // Assert
        var problem = await response.ShouldBeProblemDetailsJson(
            HttpStatusCode.PreconditionFailed,
            ProblemTypes.PreconditionFailed);
        problem.GetProperty("status").GetInt32().Should().Be(412);
        repository.Current.ProductName.Should().Be("Concurrent");
        repository.ConditionalWriteCount.Should().Be(1);
        repository.UnconditionalWriteCount.Should().Be(0);
    }

    [Fact]
    public async Task IfMatch_RepositoryWithoutAtomicCapability_Returns501WithoutWriting()
    {
        // Arrange
        var id = Guid.NewGuid();
        var inner = new InMemoryRepository<ProductEntity, Guid>(entity => entity.Id, Guid.NewGuid);
        inner.Seed([CreateProduct(id, "Original")]);
        var repository = new NonConditionalRepository(inner);
        var (host, client) = await new TestHostBuilder<ProductEntity, Guid>(repository, "/api/products")
            .WithOptions(options => options.EnableETagSupport = true)
            .WithEndpoint(config => config.AllowAnonymous())
            .BuildAsync();
        using var hostHandle = host;
        using var clientHandle = client;
        var getResponse = await client.GetAsync($"/api/products/{id}");
        var etag = getResponse.Headers.ETag!.Tag;
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/products/{id}")
        {
            Content = JsonContent.Create(new
            {
                product_name = "Stale",
                unit_price = 20m,
                stock_quantity = 10,
                is_active = true
            })
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);

        // Act
        var response = await client.SendAsync(request);

        // Assert
        await response.ShouldBeProblemDetailsJson(
            HttpStatusCode.NotImplemented,
            ProblemTypes.ConditionalWriteNotSupported);
        repository.WriteCount.Should().Be(0);
        var persisted = await inner.GetByIdAsync(id);
        persisted!.ProductName.Should().Be("Original");
    }

    private static ProductEntity CreateProduct(Guid id, string name)
    {
        return new ProductEntity
        {
            Id = id,
            ProductName = name,
            UnitPrice = 10m,
            StockQuantity = 5,
            CreatedAt = new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc),
            IsActive = true
        };
    }

    private static HttpRequestMessage CreateConditionalRequest(string operation, Guid id, string etag)
    {
        var method = new HttpMethod(operation);
        var request = new HttpRequestMessage(method, $"/api/products/{id}");
        if (method == HttpMethod.Put)
        {
            request.Content = JsonContent.Create(new
            {
                product_name = "Stale",
                unit_price = 20m,
                stock_quantity = 10,
                is_active = true
            });
        }
        else if (method == HttpMethod.Patch)
        {
            request.Content = new StringContent(
                "{\"product_name\":\"Stale\"}",
                System.Text.Encoding.UTF8,
                "application/merge-patch+json");
        }

        request.Headers.TryAddWithoutValidation("If-Match", etag);
        return request;
    }

    private sealed class InterleavingConditionalRepository :
        IRepository<ProductEntity, Guid>,
        IConditionalWriteRepository<ProductEntity, Guid>
    {
        private readonly object _mutationLock = new();
        private ProductEntity _current;

        public InterleavingConditionalRepository(ProductEntity current)
        {
            _current = current;
        }

        public bool ChangeBeforeConditionalWrite { get; set; }

        public int ConditionalWriteCount { get; private set; }

        public int UnconditionalWriteCount { get; private set; }

        public ProductEntity Current => Clone(_current);

        public Task<ProductEntity?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            lock (_mutationLock)
            {
                return Task.FromResult<ProductEntity?>(_current.Id == id ? Clone(_current) : null);
            }
        }

        public Task<PagedResult<ProductEntity>> GetAllAsync(
            PaginationRequest pagination,
            CancellationToken ct = default)
        {
            lock (_mutationLock)
            {
                return Task.FromResult(new PagedResult<ProductEntity>
                {
                    Items = [Clone(_current)]
                });
            }
        }

        public Task<ProductEntity> CreateAsync(ProductEntity entity, CancellationToken ct = default)
        {
            lock (_mutationLock)
            {
                UnconditionalWriteCount++;
                _current = Clone(entity);
                return Task.FromResult(Clone(_current));
            }
        }

        public Task<ProductEntity?> UpdateAsync(
            Guid id,
            ProductEntity entity,
            CancellationToken ct = default)
        {
            lock (_mutationLock)
            {
                UnconditionalWriteCount++;
                _current = Clone(entity);
                _current.Id = id;
                return Task.FromResult<ProductEntity?>(Clone(_current));
            }
        }

        public Task<ProductEntity?> PatchAsync(
            Guid id,
            JsonElement patchDocument,
            CancellationToken ct = default)
        {
            lock (_mutationLock)
            {
                UnconditionalWriteCount++;
                ApplyPatch(patchDocument);
                return Task.FromResult<ProductEntity?>(Clone(_current));
            }
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        {
            lock (_mutationLock)
            {
                UnconditionalWriteCount++;
                return Task.FromResult(true);
            }
        }

        public Task<ConditionalWriteResult<ProductEntity>> UpdateConditionallyAsync(
            Guid id,
            ProductEntity entity,
            Func<ProductEntity, bool> precondition,
            CancellationToken ct = default)
        {
            lock (_mutationLock)
            {
                PrepareConditionalWrite();
                if (!precondition(_current))
                    return Task.FromResult(ConditionalWriteResult<ProductEntity>.PreconditionFailed());

                _current = Clone(entity);
                _current.Id = id;
                return Task.FromResult(ConditionalWriteResult<ProductEntity>.Success(Clone(_current)));
            }
        }

        public Task<ConditionalWriteResult<ProductEntity>> PatchConditionallyAsync(
            Guid id,
            JsonElement patchDocument,
            Func<ProductEntity, bool> precondition,
            CancellationToken ct = default)
        {
            lock (_mutationLock)
            {
                PrepareConditionalWrite();
                if (!precondition(_current))
                    return Task.FromResult(ConditionalWriteResult<ProductEntity>.PreconditionFailed());

                ApplyPatch(patchDocument);
                return Task.FromResult(ConditionalWriteResult<ProductEntity>.Success(Clone(_current)));
            }
        }

        public Task<ConditionalWriteResult<ProductEntity>> DeleteConditionallyAsync(
            Guid id,
            Func<ProductEntity, bool> precondition,
            CancellationToken ct = default)
        {
            lock (_mutationLock)
            {
                PrepareConditionalWrite();
                if (!precondition(_current))
                    return Task.FromResult(ConditionalWriteResult<ProductEntity>.PreconditionFailed());

                return Task.FromResult(ConditionalWriteResult<ProductEntity>.Success(Clone(_current)));
            }
        }

        private static ProductEntity Clone(ProductEntity entity)
        {
            return new ProductEntity
            {
                Id = entity.Id,
                ProductName = entity.ProductName,
                UnitPrice = entity.UnitPrice,
                StockQuantity = entity.StockQuantity,
                CreatedAt = entity.CreatedAt,
                IsActive = entity.IsActive,
                OptionalDescription = entity.OptionalDescription,
                Status = entity.Status
            };
        }

        private void PrepareConditionalWrite()
        {
            ConditionalWriteCount++;
            if (ChangeBeforeConditionalWrite)
            {
                _current.ProductName = "Concurrent";
                ChangeBeforeConditionalWrite = false;
            }
        }

        private void ApplyPatch(JsonElement patchDocument)
        {
            if (patchDocument.TryGetProperty("product_name", out var name))
            {
                _current.ProductName = name.GetString()!;
            }
        }
    }

    private sealed class NonConditionalRepository(
        InMemoryRepository<ProductEntity, Guid> inner) : IRepository<ProductEntity, Guid>
    {
        public int WriteCount { get; private set; }

        public Task<ProductEntity?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            inner.GetByIdAsync(id, ct);

        public Task<PagedResult<ProductEntity>> GetAllAsync(
            PaginationRequest pagination,
            CancellationToken ct = default) => inner.GetAllAsync(pagination, ct);

        public Task<ProductEntity> CreateAsync(ProductEntity entity, CancellationToken ct = default)
        {
            WriteCount++;
            return inner.CreateAsync(entity, ct);
        }

        public Task<ProductEntity?> UpdateAsync(
            Guid id,
            ProductEntity entity,
            CancellationToken ct = default)
        {
            WriteCount++;
            return inner.UpdateAsync(id, entity, ct);
        }

        public Task<ProductEntity?> PatchAsync(
            Guid id,
            JsonElement patchDocument,
            CancellationToken ct = default)
        {
            WriteCount++;
            return inner.PatchAsync(id, patchDocument, ct);
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        {
            WriteCount++;
            return inner.DeleteAsync(id, ct);
        }
    }
}
