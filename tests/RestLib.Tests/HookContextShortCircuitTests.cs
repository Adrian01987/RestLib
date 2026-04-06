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

public partial class HookContextTests
{
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
}
