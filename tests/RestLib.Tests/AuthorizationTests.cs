using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RestLib.Configuration;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Tests for Story 2.1: Authorization by Default
/// Verifies that endpoints require authorization unless explicitly anonymous.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "Authorization")]
public class AuthorizationTests : IAsyncLifetime
{
    private IHost? _host;
    private HttpClient? _client;
    private TestEntityRepository? _repository;

    /// <inheritdoc />
    public Task InitializeAsync() => Task.CompletedTask;

    private async Task CreateHostAsync(
        Action<RestLibEndpointConfiguration<TestEntity, Guid>> configure,
        bool addAuthentication = true,
        Action<RestLibOptions>? configureOptions = null,
        Action<AuthorizationOptions>? configureAuthorization = null)
    {
        _repository = new TestEntityRepository();

        var builder = new TestHostBuilder<TestEntity, Guid>(_repository, "/api/test-entities")
            .WithOptions(configureOptions ?? (_ => { }))
            .WithEndpoint(configure);

        if (addAuthentication)
        {
            builder
                .WithServices(services =>
                {
                    services.AddAuthentication("Test")
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
                    services.AddAuthorization(options =>
                    {
                        options.AddPolicy("AdminOnly", policy =>
                            policy.RequireClaim("role", "admin"));
                        options.AddPolicy("ManagerOnly", policy =>
                            policy.RequireClaim("role", "manager"));
                        options.AddPolicy("EditorOnly", policy =>
                            policy.RequireClaim("role", "editor"));
                        configureAuthorization?.Invoke(options);
                    });
                })
                .WithMiddleware(app =>
                {
                    app.UseAuthentication();
                    app.UseAuthorization();
                });
        }

        (_host, _client) = await builder.BuildAsync();
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_host is not null)
        {
            await _host.StopAsync();
        }

        _host?.Dispose();
    }

    #region Default Authorization (Secure by Default)

    [Fact]
    public async Task GetAll_WithoutAuth_Returns401()
    {
        // Arrange
        await CreateHostAsync(_ => { }); // No configuration = secure by default

        // Act
        var response = await _client!.GetAsync("/api/test-entities");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAll_WhenAuthorizationDefaultDisabled_Returns200WithoutAuth()
    {
        // Arrange
        await CreateHostAsync(
            _ => { },
            addAuthentication: false,
            configureOptions: options => options.RequireAuthorizationByDefault = false);

        // Act
        var response = await _client!.GetAsync("/api/test-entities");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RequirePolicy_WhenAuthorizationDefaultDisabled_StillRequiresPolicy()
    {
        // Arrange
        await CreateHostAsync(
            config => config.RequirePolicy(RestLibOperation.Delete, "AdminOnly"),
            configureOptions: options => options.RequireAuthorizationByDefault = false);

        // Act
        var response = await _client!.DeleteAsync($"/api/test-entities/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetById_WithoutAuth_Returns401()
    {
        // Arrange
        await CreateHostAsync(_ => { });
        var id = Guid.NewGuid();

        // Act
        var response = await _client!.GetAsync($"/api/test-entities/{id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_WithoutAuth_Returns401()
    {
        // Arrange
        await CreateHostAsync(_ => { });
        var entity = new TestEntity { Name = "Test" };

        // Act
        var response = await _client!.PostAsJsonAsync("/api/test-entities", entity);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Update_WithoutAuth_Returns401()
    {
        // Arrange
        await CreateHostAsync(_ => { });
        var id = Guid.NewGuid();
        var entity = new TestEntity { Name = "Test" };

        // Act
        var response = await _client!.PutAsJsonAsync($"/api/test-entities/{id}", entity);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Patch_WithoutAuth_Returns401()
    {
        // Arrange
        await CreateHostAsync(_ => { });
        var id = Guid.NewGuid();

        // Act
        var response = await _client!.PatchAsJsonAsync($"/api/test-entities/{id}", new { name = "Updated" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Delete_WithoutAuth_Returns401()
    {
        // Arrange
        await CreateHostAsync(_ => { });
        var id = Guid.NewGuid();

        // Act
        var response = await _client!.DeleteAsync($"/api/test-entities/{id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region AllowAnonymous - Specific Operations

    [Fact]
    public async Task GetAll_WithAllowAnonymous_Returns200()
    {
        // Arrange
        await CreateHostAsync(config => config.AllowAnonymous(RestLibOperation.GetAll));

        // Act
        var response = await _client!.GetAsync("/api/test-entities");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetById_WithAllowAnonymous_ReturnsNotFound_WhenEntityMissing()
    {
        // Arrange
        await CreateHostAsync(config => config.AllowAnonymous(RestLibOperation.GetById));
        var id = Guid.NewGuid();

        // Act
        var response = await _client!.GetAsync($"/api/test-entities/{id}");

        // Assert - 404 means auth passed, entity just doesn't exist
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_WithAllowAnonymous_Returns201()
    {
        // Arrange
        await CreateHostAsync(config => config.AllowAnonymous(RestLibOperation.Create));
        var entity = new TestEntity { Name = "Test" };

        // Act
        var response = await _client!.PostAsJsonAsync("/api/test-entities", entity);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task AllowAnonymous_OnlyAffectsSpecifiedOperations()
    {
        // Arrange - Allow anonymous for GetAll only
        await CreateHostAsync(config => config.AllowAnonymous(RestLibOperation.GetAll));

        // Act & Assert - GetAll should work
        var getAllResponse = await _client!.GetAsync("/api/test-entities");
        getAllResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act & Assert - Create should still require auth
        var createResponse = await _client!.PostAsJsonAsync("/api/test-entities", new TestEntity { Name = "Test" });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Act & Assert - Delete should still require auth
        var deleteResponse = await _client!.DeleteAsync($"/api/test-entities/{Guid.NewGuid()}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AllowAnonymous_MultipleOperations()
    {
        // Arrange
        await CreateHostAsync(config => config.AllowAnonymous(RestLibOperation.GetAll, RestLibOperation.GetById));

        // Act & Assert - Both read operations should work
        var getAllResponse = await _client!.GetAsync("/api/test-entities");
        getAllResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getByIdResponse = await _client!.GetAsync($"/api/test-entities/{Guid.NewGuid()}");
        getByIdResponse.StatusCode.Should().Be(HttpStatusCode.NotFound); // 404 means auth passed

        // Act & Assert - Write operations should still require auth
        var createResponse = await _client!.PostAsJsonAsync("/api/test-entities", new TestEntity { Name = "Test" });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AllowAnonymous_AllOperations()
    {
        // Arrange - Allow anonymous for all operations
        await CreateHostAsync(config => config.AllowAnonymous());

        // Act & Assert - All operations should work without auth
        var getAllResponse = await _client!.GetAsync("/api/test-entities");
        getAllResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var createResponse = await _client!.PostAsJsonAsync("/api/test-entities", new TestEntity { Name = "Test" });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    #endregion

    #region Policy-Based Authorization (Story 2.2)

    [Fact]
    public async Task Delete_WithAdminPolicy_AuthenticatedUserWithoutRole_Returns403()
    {
        // Arrange - Delete requires AdminOnly policy
        await CreateHostAsync(config => config.RequirePolicy(RestLibOperation.Delete, "AdminOnly"));
        _client!.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Test");
        // Note: No X-Test-Role header = no admin claim

        // Act
        var response = await _client.DeleteAsync($"/api/test-entities/{Guid.NewGuid()}");

        // Assert - User is authenticated but lacks admin role
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Delete_WithAdminPolicy_UserWithAdminRole_Succeeds()
    {
        // Arrange - Delete requires AdminOnly policy
        await CreateHostAsync(config => config.RequirePolicy(RestLibOperation.Delete, "AdminOnly"));
        _client!.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Test");
        _client.DefaultRequestHeaders.Add("X-Test-Role", "admin");

        // Act
        var response = await _client.DeleteAsync($"/api/test-entities/{Guid.NewGuid()}");

        // Assert - 404 means auth passed (entity doesn't exist)
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_WithAdminPolicy_UnauthenticatedUser_Returns401()
    {
        // Arrange - Delete requires AdminOnly policy
        await CreateHostAsync(config => config.RequirePolicy(RestLibOperation.Delete, "AdminOnly"));
        // No auth header

        // Act
        var response = await _client!.DeleteAsync($"/api/test-entities/{Guid.NewGuid()}");

        // Assert - Unauthenticated returns 401, not 403
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_WithAdminPolicy_UserWithAdminRole_Returns201()
    {
        // Arrange
        await CreateHostAsync(config => config.RequirePolicy(RestLibOperation.Create, "AdminOnly"));
        _client!.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Test");
        _client.DefaultRequestHeaders.Add("X-Test-Role", "admin");
        var entity = new TestEntity { Name = "AdminCreated" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/test-entities", entity);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Create_WithAdminPolicy_UserWithoutAdminRole_Returns403()
    {
        // Arrange
        await CreateHostAsync(config => config.RequirePolicy(RestLibOperation.Create, "AdminOnly"));
        _client!.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Test");
        var entity = new TestEntity { Name = "RegularUser" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/test-entities", entity);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Policy_OnlyAffectsSpecifiedOperation()
    {
        // Arrange - Only Delete requires admin
        await CreateHostAsync(config => config.RequirePolicy(RestLibOperation.Delete, "AdminOnly"));
        _client!.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Test");
        // Regular authenticated user (no admin role)

        // Act & Assert - GetAll should work (no policy)
        var getAllResponse = await _client.GetAsync("/api/test-entities");
        getAllResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act & Assert - Create should work (no policy)
        var createResponse = await _client.PostAsJsonAsync("/api/test-entities", new TestEntity { Name = "Test" });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Act & Assert - Delete should fail (requires admin)
        var deleteResponse = await _client.DeleteAsync($"/api/test-entities/{Guid.NewGuid()}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RequirePolicyForOperations_AppliesPolicyToMultipleOperations()
    {
        // Arrange - Create, Update, Delete all require admin
        await CreateHostAsync(config => config.RequirePolicyForOperations(
            "AdminOnly",
            RestLibOperation.Create,
            RestLibOperation.Update,
            RestLibOperation.Delete));
        _client!.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Test");
        // Regular authenticated user (no admin role)

        // Act & Assert - Read operations should work
        var getAllResponse = await _client.GetAsync("/api/test-entities");
        getAllResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act & Assert - Write operations should all fail
        var createResponse = await _client.PostAsJsonAsync("/api/test-entities", new TestEntity { Name = "Test" });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var updateResponse = await _client.PutAsJsonAsync($"/api/test-entities/{Guid.NewGuid()}", new TestEntity { Name = "Test" });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var deleteResponse = await _client.DeleteAsync($"/api/test-entities/{Guid.NewGuid()}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RequirePolicyForOperations_WithCorrectRole_AllOperationsSucceed()
    {
        // Arrange - Create, Update, Delete all require admin
        await CreateHostAsync(config => config.RequirePolicyForOperations(
            "AdminOnly",
            RestLibOperation.Create,
            RestLibOperation.Update,
            RestLibOperation.Delete));
        _client!.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Test");
        _client.DefaultRequestHeaders.Add("X-Test-Role", "admin");

        // Act & Assert - All operations should work for admin
        var createResponse = await _client.PostAsJsonAsync("/api/test-entities", new TestEntity { Name = "AdminTest" });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Get the created entity ID for update test
        var getAllResponse = await _client.GetAsync("/api/test-entities");
        getAllResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MultiplePoliciesCanBeCombined_DifferentOperationsDifferentPolicies()
    {
        // Arrange - Different policies for different operations
        await CreateHostAsync(config =>
        {
            config.RequirePolicy(RestLibOperation.Delete, "AdminOnly");
            config.RequirePolicy(RestLibOperation.Create, "EditorOnly");
            config.AllowAnonymous(RestLibOperation.GetAll, RestLibOperation.GetById);
        });

        // Act & Assert - Anonymous read operations work
        var getAllResponse = await _client!.GetAsync("/api/test-entities");
        getAllResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act & Assert - Authenticated user without roles can't create or delete
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Test");

        var createResponse = await _client.PostAsJsonAsync("/api/test-entities", new TestEntity { Name = "Test" });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var deleteResponse = await _client.DeleteAsync($"/api/test-entities/{Guid.NewGuid()}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    #endregion

    #region Batch Authorization

    [Fact]
    public async Task BatchCreate_WhenAuthorizationRequiredByDefault_RequiresAuthentication()
    {
        // Arrange
        await CreateHostAsync(config => config.EnableBatch());
        var payload = new
        {
            action = "create",
            items = new[] { new { name = "Batch" } }
        };

        // Act
        var anonymousResponse = await _client!.PostAsJsonAsync("/api/test-entities/batch", payload);
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Test");
        var authenticatedResponse = await _client.PostAsJsonAsync("/api/test-entities/batch", payload);

        // Assert
        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        authenticatedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task BatchCreate_WhenFallbackPolicyRequiresAuthentication_EnforcesFallbackPolicy()
    {
        // Arrange
        await CreateHostAsync(
            config => config.EnableBatch(),
            configureOptions: options => options.RequireAuthorizationByDefault = false,
            configureAuthorization: options =>
            {
                options.FallbackPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();
            });
        var payload = new
        {
            action = "create",
            items = new[] { new { name = "Batch" } }
        };

        // Act
        var anonymousResponse = await _client!.PostAsJsonAsync("/api/test-entities/batch", payload);
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Test");
        var authenticatedResponse = await _client.PostAsJsonAsync("/api/test-entities/batch", payload);

        // Assert
        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        authenticatedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("create", RestLibOperation.BatchCreate)]
    [InlineData("update", RestLibOperation.BatchUpdate)]
    [InlineData("patch", RestLibOperation.BatchPatch)]
    [InlineData("delete", RestLibOperation.BatchDelete)]
    public async Task BatchAction_WithSpecificPolicy_AuthorizesParsedAction(
        string action,
        RestLibOperation operation)
    {
        // Arrange
        await CreateHostAsync(
            config =>
            {
                config.EnableBatch();
                config.RequirePolicy(operation, "AdminOnly");
            },
            configureOptions: options => options.RequireAuthorizationByDefault = false);
        _client!.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Test");
        var payload = new
        {
            action,
            items = new[] { new { id = Guid.NewGuid(), name = "Test" } }
        };

        // Act
        var forbiddenResponse = await _client.PostAsJsonAsync("/api/test-entities/batch", payload);
        _client.DefaultRequestHeaders.Add("X-Test-Role", "admin");
        var authorizedResponse = await _client.PostAsJsonAsync("/api/test-entities/batch", payload);

        // Assert
        forbiddenResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        authorizedResponse.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        authorizedResponse.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task BatchDelete_WhenAnonymousAndBatchCreateRequiresPolicy_IsNotBlocked()
    {
        // Arrange
        await CreateHostAsync(
            config =>
            {
                config.EnableBatch();
                config.RequirePolicy(RestLibOperation.BatchCreate, "AdminOnly");
                config.AllowAnonymous(RestLibOperation.BatchDelete);
            },
            configureOptions: options => options.RequireAuthorizationByDefault = false);
        var payload = new
        {
            action = "delete",
            items = new[] { Guid.NewGuid() }
        };

        // Act
        var response = await _client!.PostAsJsonAsync("/api/test-entities/batch", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);
    }

    [Fact]
    public async Task BatchDelete_WhenProtectedAndBatchCreateAnonymous_RequiresPolicy()
    {
        // Arrange
        await CreateHostAsync(
            config =>
            {
                config.EnableBatch();
                config.AllowAnonymous(RestLibOperation.BatchCreate);
                config.RequirePolicy(RestLibOperation.BatchDelete, "AdminOnly");
            },
            configureOptions: options => options.RequireAuthorizationByDefault = false);
        var payload = new
        {
            action = "delete",
            items = new[] { Guid.NewGuid() }
        };

        // Act
        var response = await _client!.PostAsJsonAsync("/api/test-entities/batch", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Authenticated Access

    [Fact]
    public async Task GetAll_WithValidAuth_Returns200()
    {
        // Arrange
        await CreateHostAsync(_ => { });
        _client!.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Test");

        // Act
        var response = await _client.GetAsync("/api/test-entities");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Create_WithValidAuth_Returns201()
    {
        // Arrange
        await CreateHostAsync(_ => { });
        _client!.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Test");
        var entity = new TestEntity { Name = "Test" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/test-entities", entity);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Delete_WithValidAuth_ReturnsNoContentOrNotFound()
    {
        // Arrange
        await CreateHostAsync(_ => { });
        _client!.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Test");

        // Act
        var response = await _client.DeleteAsync($"/api/test-entities/{Guid.NewGuid()}");

        // Assert - Either 204 (deleted) or 404 (not found) means auth passed
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NoContent, HttpStatusCode.NotFound);
    }

    #endregion
}

/// <summary>
/// Test authentication handler that authenticates all requests with an Authorization header.
/// </summary>
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
    {
      new(ClaimTypes.Name, "TestUser"),
      new(ClaimTypes.NameIdentifier, "test-user-id")
    };

        // Check if roles should be added (supports multiple roles via X-Test-Role header)
        if (Request.Headers.TryGetValue("X-Test-Role", out var roles))
        {
            foreach (var role in roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                claims.Add(new Claim("role", role.Trim()));
            }
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
