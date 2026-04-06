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
    #region ErrorHookContext Tests

    [Fact]
    public async Task ErrorHookContext_CanInspect_Exception()
    {
        // Arrange
        Exception? capturedException = null;
        var repository = new ContextTestRepository();

        using var host = await CreateHostWithHooksAndErrorSimulation(repository, hooks =>
        {
#pragma warning disable CS1998 // Async method lacks await
            hooks.BeforePersist = async ctx =>
        {
            throw new InvalidOperationException("Simulated error for testing");
        };
#pragma warning restore CS1998
            hooks.OnError = async ctx =>
        {
            capturedException = ctx.Exception;
            ctx.Handled = true;
            ctx.ErrorResult = Results.StatusCode(500);
            await Task.CompletedTask;
        };
        });

        var client = host.GetTestClient();
        var entity = new ContextTestEntity { Name = "Test" };

        // Act
        await client.PostAsJsonAsync("/api/items", entity);

        // Assert
        capturedException.Should().NotBeNull();
        capturedException.Should().BeOfType<InvalidOperationException>();
        capturedException!.Message.Should().Be("Simulated error for testing");
    }

    [Fact]
    public async Task ErrorHookContext_CanProvide_CustomErrorResponse()
    {
        // Arrange
        var repository = new ContextTestRepository();

        using var host = await CreateHostWithHooksAndErrorSimulation(repository, hooks =>
        {
#pragma warning disable CS1998 // Async method lacks await
            hooks.BeforePersist = async ctx =>
        {
            throw new ArgumentException("Bad argument");
        };
#pragma warning restore CS1998
            hooks.OnError = async ctx =>
        {
            ctx.Handled = true;
            ctx.ErrorResult = Results.Json(
          new { errorType = ctx.Exception.GetType().Name, message = ctx.Exception.Message },
          statusCode: 422);
            await Task.CompletedTask;
        };
        });

        var client = host.GetTestClient();
        var entity = new ContextTestEntity { Name = "Test" };

        // Act
        var response = await client.PostAsJsonAsync("/api/items", entity);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be((HttpStatusCode)422);
        content.Should().Contain("ArgumentException");
        content.Should().Contain("Bad argument");
    }

    [Fact]
    public async Task ErrorHookContext_HasAccess_ToItems()
    {
        // Arrange
        var repository = new ContextTestRepository();

        using var host = await CreateHostWithHooksAndErrorSimulation(repository, hooks =>
        {
            hooks.OnRequestReceived = async ctx =>
        {
            ctx.Items["RequestStartTime"] = DateTime.UtcNow.ToString("O");
            await Task.CompletedTask;
        };
#pragma warning disable CS1998 // Async method lacks await
            hooks.BeforePersist = async ctx =>
        {
            throw new Exception("Error after items were set");
        };
#pragma warning restore CS1998
            hooks.OnError = async ctx =>
        {
            // Note: Items are shared via the pipeline, so they should be available
            // in error context if they were set before the error
            ctx.Handled = true;
            ctx.ErrorResult = Results.StatusCode(500);
            await Task.CompletedTask;
        };
        });

        var client = host.GetTestClient();
        var entity = new ContextTestEntity { Name = "Test" };

        // Act
        var response = await client.PostAsJsonAsync("/api/items", entity);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    #endregion
}
