using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestLib.Abstractions;
using RestLib.Configuration;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Tests for Story 6.1: Rate Limiting Integration
/// Verifies that rate limiting policies are correctly applied to RestLib endpoints.
/// </summary>
public class RateLimitingTests : IDisposable
{
  private IHost? _host;
  private HttpClient? _client;
  private TestEntityRepository? _repository;

  private void CreateHost(Action<RestLibEndpointConfiguration<TestEntity, Guid>> configure)
  {
    _repository = new TestEntityRepository();

    _host = new HostBuilder()
        .ConfigureWebHost(webBuilder =>
        {
          webBuilder
                  .UseTestServer()
                  .ConfigureServices(services =>
                  {
                    services.AddRestLib();
                    services.AddSingleton<IRepository<TestEntity, Guid>>(_repository);
                    services.AddRouting();
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
                  .Configure(app =>
                  {
                    app.UseRouting();
                    app.UseRateLimiter();
                    app.UseEndpoints(endpoints =>
                    {
                      endpoints.MapRestLib<TestEntity, Guid>("/api/limited", cfg =>
                      {
                        cfg.AllowAnonymous();
                        configure(cfg);
                      });
                    });
                  });
        })
        .Build();

    _host.Start();
    _client = _host.GetTestClient();
  }

  public void Dispose()
  {
    _client?.Dispose();
    _host?.Dispose();
  }

  [Fact]
  [Trait("Category", "Story6.1")]
  public async Task NoRateLimitConfig_RequestsSucceed()
  {
    // Arrange — no rate limiting configured
    CreateHost(_ => { });

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
    CreateHost(cfg => cfg.UseRateLimiting("strict"));

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
    CreateHost(cfg => cfg.UseRateLimiting("strict"));

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
    CreateHost(cfg => cfg.UseRateLimiting("strict", RestLibOperation.GetAll));
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
    CreateHost(cfg =>
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

  [Fact]
  [Trait("Category", "Story6.1")]
  public async Task DisableRateLimiting_ExemptsOperation()
  {
    // Arrange — strict default, but GetById is exempt
    var entityId = Guid.NewGuid();
    CreateHost(cfg =>
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
    CreateHost(cfg =>
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
    CreateHost(cfg => cfg.UseRateLimiting("strict"));

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
    CreateHost(cfg =>
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
}
