using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestLib.Abstractions;
using RestLib.Hooks;
using RestLib.Pagination;
using RestLib.Tests.Fakes;
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
[Trait("Type", "Integration")]
[Trait("Feature", "Hooks")]
public partial class HookContextTests
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

    #region Test Host Helper

    private static Task<IHost> CreateHostWithHooks(
      ContextTestRepository repository,
      Action<RestLibHooks<ContextTestEntity, int>> configureHooks)
    {
        var (host, _) = new TestHostBuilder<ContextTestEntity, int>(repository, "/api/items")
            .SkipRestLibRegistration()
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.KeySelector = e => e.Id;
                config.UseHooks(configureHooks);
            })
            .Build();

        return Task.FromResult(host);
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
