using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestLib.Abstractions;
using RestLib.Batch;
using RestLib.InMemory;
using RestLib.Responses;
using RestLib.Serialization;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

[Trait("Type", "Integration")]
[Trait("Feature", "Identity")]
public sealed class ResourceIdentityTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = RestLibJsonOptions.CreateDefault();

    private readonly InMemoryRepository<AlternateKeyEntity, Guid> _repository =
        new(entity => entity.ExternalId, Guid.NewGuid, JsonOptions);
    private IHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        (_host, _client) = await new TestHostBuilder<AlternateKeyEntity, Guid>(
                _repository,
                "/api/alternate-key-items")
            .WithServices(services =>
            {
                services.AddSingleton<IBatchRepository<AlternateKeyEntity, Guid>>(_repository);
            })
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.KeySelector = entity => entity.ExternalId;
                config.EnableBatch(BatchAction.Update, BatchAction.Patch);
            })
            .BuildAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task Update_AlternateBodyKeyDiffersFromRouteKey_UsesRouteIdentity()
    {
        // Arrange
        var routeKey = Guid.NewGuid();
        var bodyKey = Guid.NewGuid();
        Seed(routeKey, "Original");
        var replacement = new AlternateKeyEntity
        {
            InternalId = 99,
            ExternalId = bodyKey,
            Name = "Updated"
        };

        // Act
        var response = await _client.PutAsJsonAsync(
            $"/api/alternate-key-items/{routeKey}",
            replacement,
            JsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var responseEntity = await response.Content.ReadFromJsonAsync<AlternateKeyEntity>(JsonOptions);
        responseEntity.Should().NotBeNull();
        responseEntity!.ExternalId.Should().Be(routeKey);
        (await _repository.GetByIdAsync(routeKey))!.ExternalId.Should().Be(routeKey);
        (await _repository.GetByIdAsync(bodyKey)).Should().BeNull();
    }

    [Fact]
    public async Task Patch_AlternateKeyFieldIsPresent_Returns400AndPreservesEntity()
    {
        // Arrange
        var routeKey = Guid.NewGuid();
        Seed(routeKey, "Original");
        var patch = new
        {
            external_id = Guid.NewGuid(),
            name = "Should not persist"
        };

        // Act
        var response = await _client.PatchAsJsonAsync(
            $"/api/alternate-key-items/{routeKey}",
            patch,
            JsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<RestLibProblemDetails>(JsonOptions);
        problem.Should().NotBeNull();
        problem!.Type.Should().Be(ProblemTypes.BadRequest);
        problem.Detail.Should().Contain("external_id");
        var persisted = await _repository.GetByIdAsync(routeKey);
        persisted.Should().NotBeNull();
        persisted!.ExternalId.Should().Be(routeKey);
        persisted.Name.Should().Be("Original");
    }

    [Fact]
    public async Task BatchUpdateAndPatch_AlternateKeys_UsesEnvelopeIdentityAndRejectsKeyPatch()
    {
        // Arrange
        var routeKey = Guid.NewGuid();
        var bodyKey = Guid.NewGuid();
        Seed(routeKey, "Original");
        var updatePayload = new
        {
            action = "update",
            items = new[]
            {
                new
                {
                    id = routeKey,
                    body = new
                    {
                        internal_id = 42,
                        external_id = bodyKey,
                        name = "Batch updated"
                    }
                }
            }
        };

        // Act
        var updateResponse = await _client.PostAsync(
            "/api/alternate-key-items/batch",
            BatchJson(updatePayload));

        // Assert
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await _repository.GetByIdAsync(routeKey);
        updated.Should().NotBeNull();
        updated!.ExternalId.Should().Be(routeKey);
        updated.Name.Should().Be("Batch updated");
        (await _repository.GetByIdAsync(bodyKey)).Should().BeNull();

        var patchPayload = new
        {
            action = "patch",
            items = new[]
            {
                new
                {
                    id = routeKey,
                    body = new
                    {
                        external_id = Guid.NewGuid(),
                        name = "Should not persist"
                    }
                }
            }
        };

        // Act
        var patchResponse = await _client.PostAsync(
            "/api/alternate-key-items/batch",
            BatchJson(patchPayload));

        // Assert
        patchResponse.StatusCode.Should().Be(HttpStatusCode.MultiStatus);
        var json = await patchResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        json.GetProperty("items")[0].GetProperty("status").GetInt32().Should().Be(400);
        json.GetProperty("items")[0].GetProperty("error").GetProperty("detail").GetString()
            .Should().Contain("external_id");
        var persisted = await _repository.GetByIdAsync(routeKey);
        persisted.Should().NotBeNull();
        persisted!.ExternalId.Should().Be(routeKey);
        persisted.Name.Should().Be("Batch updated");
    }

    private static StringContent BatchJson(object payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private void Seed(Guid externalId, string name)
    {
        _repository.Clear();
        _repository.Seed(
        [
            new AlternateKeyEntity
            {
                InternalId = 1,
                ExternalId = externalId,
                Name = name
            }
        ]);
    }
}

internal sealed class AlternateKeyEntity
{
    public int InternalId { get; set; }

    public Guid ExternalId { get; set; }

    public string Name { get; set; } = string.Empty;
}
