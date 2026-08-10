using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestLib.Abstractions;
using RestLib.Batch;
using RestLib.Configuration;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Tests for Story 6.1: Rate Limiting Integration
/// Verifies that rate limiting policies are correctly applied to RestLib endpoints.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "RateLimiting")]
public class RateLimitingTests : IAsyncLifetime
{
    private IHost? _host;
    private HttpClient? _client;
    private TestEntityRepository? _repository;
    private RepositorySpy<TestEntity, Guid>? _repositorySpy;

    /// <inheritdoc />
    public Task InitializeAsync() => Task.CompletedTask;

    private async Task CreateHostAsync(Action<RestLibEndpointConfiguration<TestEntity, Guid>> configure)
    {
        _repository = new TestEntityRepository();
        _repositorySpy = new RepositorySpy<TestEntity, Guid>(_repository);

        (_host, _client) = await new TestHostBuilder<TestEntity, Guid>(_repositorySpy, "/api/limited")
            .WithServices(services =>
            {
                services.AddRateLimiter(options =>
                {
                    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                    options.AddFixedWindowLimiter("strict", limiter =>
                    {
                        limiter.PermitLimit = 1;
                        limiter.Window = TimeSpan.FromMinutes(1);
                    });
                    options.AddFixedWindowLimiter("relaxed", limiter =>
                    {
                        limiter.PermitLimit = 10;
                        limiter.Window = TimeSpan.FromMinutes(1);
                    });
                });
            })
            .WithMiddleware(app => app.UseRateLimiter())
            .WithEndpoint(cfg =>
            {
                cfg.AllowAnonymous();
                configure(cfg);
            })
            .BuildAsync();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_host is not null)
        {
            await _host.StopAsync();
        }

        _host?.Dispose();
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public async Task NoRateLimitConfig_RequestsSucceed()
    {
        // Arrange — no rate limiting configured
        await CreateHostAsync(_ => { });

        // Act — send multiple requests
        var response1 = await _client!.GetAsync("/api/limited");
        var response2 = await _client!.GetAsync("/api/limited");
        var response3 = await _client!.GetAsync("/api/limited");

        // Assert — all succeed
        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
        response3.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public async Task GlobalPolicy_FirstRequestSucceeds()
    {
        // Arrange — strict policy allows only 1 request per window
        await CreateHostAsync(cfg => cfg.UseRateLimiting("strict"));

        // Act
        var response = await _client!.GetAsync("/api/limited");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public async Task GlobalPolicy_Returns429WhenExceeded()
    {
        // Arrange — strict policy allows only 1 request per window
        await CreateHostAsync(cfg => cfg.UseRateLimiting("strict"));

        // Act — first request consumes the limit, second should be rejected
        await _client!.GetAsync("/api/limited");
        var response = await _client!.GetAsync("/api/limited");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public async Task PerOperationPolicy_OnlyAffectsConfiguredOperations()
    {
        // Arrange — strict policy only on GetAll
        var entityId = Guid.NewGuid();
        await CreateHostAsync(cfg => cfg.UseRateLimiting("strict", RestLibOperation.GetAll));
        _repository!.Seed(new TestEntity { Id = entityId, Name = "Test" });

        // Act — exhaust GetAll limit
        await _client!.GetAsync("/api/limited");
        var getAllResponse = await _client!.GetAsync("/api/limited");

        // GetById should still succeed (no policy applied)
        var getByIdResponse = await _client!.GetAsync($"/api/limited/{entityId}");

        // Assert
        getAllResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        getByIdResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public async Task PerOperationOverridesDefault()
    {
        // Arrange — relaxed default, strict override on Create
        await CreateHostAsync(cfg =>
        {
            cfg.UseRateLimiting("relaxed");
            cfg.UseRateLimiting("strict", RestLibOperation.Create);
        });

        // Act — exhaust Create's strict limit
        await _client!.PostAsJsonAsync("/api/limited", new TestEntity { Name = "First" });
        var createResponse = await _client!.PostAsJsonAsync("/api/limited", new TestEntity { Name = "Second" });

        // GetAll should still succeed under relaxed policy
        var getAllResponse = await _client!.GetAsync("/api/limited");

        // Assert
        createResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        getAllResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("create", BatchAction.Create, RestLibOperation.BatchCreate)]
    [InlineData("update", BatchAction.Update, RestLibOperation.BatchUpdate)]
    [InlineData("patch", BatchAction.Patch, RestLibOperation.BatchPatch)]
    [InlineData("delete", BatchAction.Delete, RestLibOperation.BatchDelete)]
    [Trait("Category", "Story6.1")]
    public async Task BatchAction_WithActionPolicy_Returns429WhenExceeded(
        string actionName,
        BatchAction action,
        RestLibOperation operation)
    {
        // Arrange
        await CreateHostAsync(cfg =>
        {
            cfg.EnableBatch(action);
            cfg.UseRateLimiting("strict", operation);
        });
        var id = Guid.NewGuid();
        if (action != BatchAction.Create)
        {
            _repository!.Seed(new TestEntity { Id = id, Name = "Original" });
        }

        object payload = action switch
        {
            BatchAction.Create => new
            {
                action = actionName,
                items = new[] { new { name = "Created" } }
            },
            BatchAction.Update => new
            {
                action = actionName,
                items = new[] { new { id, body = new { name = "Updated" } } }
            },
            BatchAction.Patch => new
            {
                action = actionName,
                items = new[] { new { id, body = new { name = "Patched" } } }
            },
            BatchAction.Delete => new
            {
                action = actionName,
                items = new[] { id }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown batch action.")
        };
        var client = _client ?? throw new InvalidOperationException("The test host did not create an HTTP client.");

        // Act
        var firstResponse = await client.PostAsJsonAsync("/api/limited/batch", payload);
        var secondResponse = await client.PostAsJsonAsync("/api/limited/batch", payload);

        // Assert
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        var writeCallCount = action switch
        {
            BatchAction.Create => _repositorySpy!.CreateAsyncCallCount,
            BatchAction.Update => _repositorySpy!.UpdateAsyncCallCount,
            BatchAction.Patch => _repositorySpy!.PatchAsyncCallCount,
            BatchAction.Delete => _repositorySpy!.DeleteAsyncCallCount,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown batch action.")
        };
        writeCallCount.Should().Be(1, "the rejected request must not reach persistence");
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public async Task BatchActions_WithSamePolicy_MapAndShareRateLimit()
    {
        // Arrange
        await CreateHostAsync(cfg =>
        {
            cfg.EnableBatch(BatchAction.Create, BatchAction.Delete);
            cfg.UseRateLimiting(
                "strict",
                RestLibOperation.BatchCreate,
                RestLibOperation.BatchDelete);
        });
        var createPayload = new
        {
            action = "create",
            items = new[] { new { name = "Created" } }
        };
        var deletePayload = new
        {
            action = "delete",
            items = new[] { Guid.NewGuid() }
        };
        var client = _client ?? throw new InvalidOperationException("The test host did not create an HTTP client.");

        // Act
        var createResponse = await client.PostAsJsonAsync("/api/limited/batch", createPayload);
        var deleteResponse = await client.PostAsJsonAsync("/api/limited/batch", deletePayload);

        // Assert
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public async Task BatchActions_WhenAllRateLimitingDisabled_AreExempt()
    {
        // Arrange
        await CreateHostAsync(cfg =>
        {
            cfg.EnableBatch(BatchAction.Create, BatchAction.Delete);
            cfg.UseRateLimiting("strict");
            cfg.DisableRateLimiting(
                RestLibOperation.BatchCreate,
                RestLibOperation.BatchDelete);
        });
        var createPayload = new
        {
            action = "create",
            items = new[] { new { name = "Created" } }
        };
        var deletePayload = new
        {
            action = "delete",
            items = new[] { Guid.NewGuid() }
        };
        var client = _client ?? throw new InvalidOperationException("The test host did not create an HTTP client.");

        // Act
        var firstCreateResponse = await client.PostAsJsonAsync("/api/limited/batch", createPayload);
        var deleteResponse = await client.PostAsJsonAsync("/api/limited/batch", deletePayload);
        var secondCreateResponse = await client.PostAsJsonAsync("/api/limited/batch", createPayload);

        // Assert
        firstCreateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.MultiStatus);
        secondCreateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public async Task BatchActions_WithDifferentPolicies_ThrowsDuringEndpointMapping()
    {
        // Arrange
        Func<Task> act = () => CreateHostAsync(cfg =>
        {
            cfg.EnableBatch(BatchAction.Create, BatchAction.Delete);
            cfg.UseRateLimiting("strict", RestLibOperation.BatchCreate);
            cfg.UseRateLimiting("relaxed", RestLibOperation.BatchDelete);
        });

        // Act & Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*shared batch endpoint*same effective rate-limit policy*");
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public async Task BatchActions_WithDifferentDisabledStates_ThrowsDuringEndpointMapping()
    {
        // Arrange
        Func<Task> act = () => CreateHostAsync(cfg =>
        {
            cfg.EnableBatch(BatchAction.Create, BatchAction.Delete);
            cfg.UseRateLimiting("strict");
            cfg.DisableRateLimiting(RestLibOperation.BatchDelete);
        });

        // Act & Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*shared batch endpoint*disabled state*");
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public async Task DisableRateLimiting_ExemptsOperation()
    {
        // Arrange — strict default, but GetById is exempt
        var entityId = Guid.NewGuid();
        await CreateHostAsync(cfg =>
        {
            cfg.UseRateLimiting("strict");
            cfg.DisableRateLimiting(RestLibOperation.GetById);
        });
        _repository!.Seed(new TestEntity { Id = entityId, Name = "Test" });

        // Act — exhaust the strict limit on GetAll
        await _client!.GetAsync("/api/limited");
        var getAllResponse = await _client!.GetAsync("/api/limited");

        // GetById should still succeed because it's exempt
        var getById1 = await _client!.GetAsync($"/api/limited/{entityId}");
        var getById2 = await _client!.GetAsync($"/api/limited/{entityId}");

        // Assert
        getAllResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        getById1.StatusCode.Should().Be(HttpStatusCode.OK);
        getById2.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public async Task DisableRateLimiting_TakesPrecedenceOverPerOperation()
    {
        // Arrange — strict per-operation on GetAll, then disable GetAll
        await CreateHostAsync(cfg =>
        {
            cfg.UseRateLimiting("strict", RestLibOperation.GetAll);
            cfg.DisableRateLimiting(RestLibOperation.GetAll);
        });

        // Act — send multiple requests that would exceed the strict limit
        var response1 = await _client!.GetAsync("/api/limited");
        var response2 = await _client!.GetAsync("/api/limited");
        var response3 = await _client!.GetAsync("/api/limited");

        // Assert — all succeed because rate limiting is disabled
        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
        response3.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public async Task Response429_HasCorrectStatusCode()
    {
        // Arrange
        await CreateHostAsync(cfg => cfg.UseRateLimiting("strict"));

        // Act — exhaust the limit
        await _client!.GetAsync("/api/limited");
        var response = await _client!.GetAsync("/api/limited");

        // Assert — rejected request returns 429
        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public async Task DifferentPolicies_ReadVsWrite()
    {
        // Arrange — relaxed for reads, strict for writes
        await CreateHostAsync(cfg =>
        {
            cfg.UseRateLimiting("relaxed", RestLibOperation.GetAll, RestLibOperation.GetById);
            cfg.UseRateLimiting("strict", RestLibOperation.Create, RestLibOperation.Update,
            RestLibOperation.Patch, RestLibOperation.Delete);
        });

        // Act — exhaust the strict write limit
        await _client!.PostAsJsonAsync("/api/limited", new TestEntity { Name = "First" });
        var createResponse = await _client!.PostAsJsonAsync("/api/limited", new TestEntity { Name = "Second" });

        // Reads should still succeed under relaxed policy
        var getAll1 = await _client!.GetAsync("/api/limited");
        var getAll2 = await _client!.GetAsync("/api/limited");

        // Assert
        createResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        getAll1.StatusCode.Should().Be(HttpStatusCode.OK);
        getAll2.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void UseRateLimiting_EmptyPolicyName_ThrowsArgumentException()
    {
        // Arrange
        var cfg = new RestLibEndpointConfiguration<TestEntity, Guid>();

        // Act
        var act = () => cfg.UseRateLimiting("");

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public async Task DisableRateLimiting_NoOperations_IsNoOp()
    {
        // Arrange — strict default, DisableRateLimiting with no args should not disable anything
        await CreateHostAsync(cfg =>
        {
            cfg.UseRateLimiting("strict");
            cfg.DisableRateLimiting();
        });

        // Act — first request consumes the limit, second should be rejected
        await _client!.GetAsync("/api/limited");
        var response = await _client!.GetAsync("/api/limited");

        // Assert — strict policy still applies because DisableRateLimiting was a no-op
        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public async Task UseRateLimiting_CalledMultipleTimes_LastDefaultWins()
    {
        // Arrange — first call sets strict (1 permit), second overwrites with relaxed (10 permits)
        await CreateHostAsync(cfg =>
        {
            cfg.UseRateLimiting("strict");
            cfg.UseRateLimiting("relaxed");
        });

        // Act — send multiple requests that would exceed the strict limit
        var response1 = await _client!.GetAsync("/api/limited");
        var response2 = await _client!.GetAsync("/api/limited");
        var response3 = await _client!.GetAsync("/api/limited");

        // Assert — all succeed because relaxed (last call) wins
        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
        response3.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public async Task JsonConfig_ByOperation_InvalidName_ThrowsInvalidOperationException()
    {
        // Act
        var act = async () =>
        {
            var (host, _) = await new TestJsonHostBuilder()
                .WithServices(services =>
                {
                    services.AddSingleton<IRepository<TestEntity, Guid>>(new TestEntityRepository());
                    services.AddRateLimiter(options =>
                    {
                        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                        options.AddFixedWindowLimiter("some-policy", limiter =>
                        {
                            limiter.PermitLimit = 1;
                            limiter.Window = TimeSpan.FromMinutes(1);
                        });
                    });
                    services.AddJsonResource<TestEntity, Guid>(new RestLibJsonResourceConfiguration
                    {
                        Name = "limited",
                        Route = "/api/limited",
                        AllowAnonymousAll = true,
                        RateLimiting = new RestLibJsonRateLimitingConfiguration
                        {
                            ByOperation = new Dictionary<string, string>
                            {
                                ["NotAnOperation"] = "some-policy"
                            }
                        }
                    });
                })
                .WithMiddleware(app => app.UseRateLimiter())
                .BuildAsync();

            host.Dispose();
        };

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*'NotAnOperation' is not a valid RestLib operation name*");
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public async Task UseRateLimiting_PerOperation_NoOperations_IsNoOp()
    {
        // Arrange — per-operation call with no operations is a no-op; no default set either
        await CreateHostAsync(cfg => cfg.UseRateLimiting("strict", Array.Empty<RestLibOperation>()));

        // Act — send multiple requests
        var response1 = await _client!.GetAsync("/api/limited");
        var response2 = await _client!.GetAsync("/api/limited");
        var response3 = await _client!.GetAsync("/api/limited");

        // Assert — all succeed because no policy was actually applied
        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
        response3.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public async Task JsonConfig_AppliesRateLimiting()
    {
        // Arrange — configure rate limiting via JSON config model
        _repository = new TestEntityRepository();

        var (host, client) = await new TestJsonHostBuilder()
            .WithServices(services =>
            {
                services.AddSingleton<IRepository<TestEntity, Guid>>(_repository);
                services.AddRateLimiter(options =>
                {
                    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                    options.AddFixedWindowLimiter("json-strict", limiter =>
                    {
                        limiter.PermitLimit = 1;
                        limiter.Window = TimeSpan.FromMinutes(1);
                    });
                });
                services.AddJsonResource<TestEntity, Guid>(new RestLibJsonResourceConfiguration
                {
                    Name = "limited",
                    Route = "/api/limited",
                    AllowAnonymousAll = true,
                    RateLimiting = new RestLibJsonRateLimitingConfiguration
                    {
                        Default = "json-strict"
                    }
                });
            })
            .WithMiddleware(app => app.UseRateLimiter())
            .BuildAsync();

        _host = host;
        _client = client;

        // Act — first request consumes the limit, second should be rejected
        var first = await _client.GetAsync("/api/limited");
        var second = await _client.GetAsync("/api/limited");

        // Assert
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}
