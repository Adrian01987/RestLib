using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestLib.Abstractions;
using RestLib.Batch;
using RestLib.Configuration;
using RestLib.Pagination;
using RestLib.Responses;
using RestLib.Serialization;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Verifies the adapter-neutral exception boundary used by endpoint handlers.
/// </summary>
public class AdapterExceptionContractTests
{
    private const int KnownId = 42;
    private const string ValidationMessage = "The adapter rejected the patch document.";
    private static readonly JsonSerializerOptions JsonOptions = RestLibJsonOptions.CreateDefault();

    [Fact]
    public async Task Patch_DerivedAdapterPatchValidationException_ReturnsBadRequest()
    {
        // Arrange
        var exception = new RenamedAdapterPatchValidationException(ValidationMessage);
        var (host, client) = await CreateHostAsync(exception);

        using (host)
        using (client)
        {
            // Act
            var response = await client.PatchAsJsonAsync(
                $"/api/items/{KnownId}",
                new { name = "Updated" });

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var problem = await response.Content.ReadFromJsonAsync<RestLibProblemDetails>(JsonOptions);
            problem.Should().NotBeNull();
            problem!.Type.Should().Be(ProblemTypes.BadRequest);
            problem.Detail.Should().Be(ValidationMessage);
        }
    }

    [Fact]
    public async Task BatchPatch_DerivedAdapterPatchValidationException_ReturnsBadRequestItem()
    {
        // Arrange
        var exception = new RenamedAdapterPatchValidationException(ValidationMessage);
        var (host, client) = await CreateHostAsync(
            exception,
            config => config.EnableBatch(BatchAction.Patch));
        var payload = new
        {
            action = "patch",
            items = new[]
            {
                new { id = KnownId, body = new { name = "Updated" } }
            }
        };

        using (host)
        using (client)
        {
            // Act
            var response = await client.PostAsJsonAsync("/api/items/batch", payload);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
            var item = json.GetProperty("items")[0];
            item.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status400BadRequest);
            item.GetProperty("error").GetProperty("type").GetString().Should().Be(ProblemTypes.BadRequest);
            item.GetProperty("error").GetProperty("detail").GetString().Should().Be(ValidationMessage);
        }
    }

    [Fact]
    public async Task MappedPatch_DerivedAdapterPatchValidationException_ReturnsBadRequest()
    {
        // Arrange
        var exception = new RenamedAdapterPatchValidationException(ValidationMessage);
        var (host, client) = await new TestTwoModelHostBuilder<
                AdapterContractEntity,
                AdapterContractEntity,
                int>(new AdapterContractRepository(exception), "/api/mapped-items")
            .WithServices(services =>
            {
                services.AddRestLibMapper<AdapterContractEntity, AdapterContractEntity>(
                    _ => new AdapterContractIdentityMapper());
            })
            .WithEndpoint(config => config.AllowAnonymous())
            .BuildAsync();

        using (host)
        using (client)
        {
            // Act
            var response = await client.PatchAsJsonAsync(
                $"/api/mapped-items/{KnownId}",
                new { name = "Updated" });

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var problem = await response.Content.ReadFromJsonAsync<RestLibProblemDetails>(JsonOptions);
            problem.Should().NotBeNull();
            problem!.Type.Should().Be(ProblemTypes.BadRequest);
            problem.Detail.Should().Be(ValidationMessage);
        }
    }

    [Fact]
    public async Task Patch_ExceptionWithEfCoreValidationTypeNameWithoutContract_Propagates()
    {
        // Arrange
        var exception = new global::RestLib.EntityFrameworkCore.EfCorePatchValidationException(
            "A type name is not an exception contract.");
        var (host, client) = await CreateHostAsync(exception);

        using (host)
        using (client)
        {
            // Act
            var act = () => client.PatchAsJsonAsync(
                $"/api/items/{KnownId}",
                new { name = "Updated" });

            // Assert
            await act.Should().ThrowAsync<global::RestLib.EntityFrameworkCore.EfCorePatchValidationException>()
                .WithMessage("A type name is not an exception contract.");
        }
    }

    [Fact]
    public async Task BatchCreate_RequestHookThrowsInvalidOperationException_UsesHostErrorHandling()
    {
        // Arrange
        const string Secret = "internal hook implementation detail";
        const string GenericError = "generic server failure";
        var (host, client) = await CreateHostAsync(
            patchException: null,
            configureEndpoint: config =>
            {
                config.EnableBatch(BatchAction.Create);
                config.UseHooks(hooks =>
                {
                    hooks.OnRequestReceived = _ => throw new InvalidOperationException(Secret);
                });
            },
            configureMiddleware: ConfigureGenericErrorHandling(GenericError));
        var payload = new
        {
            action = "create",
            items = new[]
            {
                new { id = 7, name = "Created" }
            }
        };

        using (host)
        using (client)
        {
            // Act
            var response = await client.PostAsJsonAsync("/api/items/batch", payload);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            var body = await response.Content.ReadAsStringAsync();
            body.Should().Be(GenericError);
            body.Should().NotContain(Secret);
        }
    }

    [Fact]
    public async Task BatchCreate_RequestHookThrowsJsonException_UsesHostErrorHandling()
    {
        // Arrange
        const string Secret = "internal JSON hook implementation detail";
        const string GenericError = "generic server failure";
        var (host, client) = await CreateHostAsync(
            patchException: null,
            configureEndpoint: config =>
            {
                config.EnableBatch(BatchAction.Create);
                config.UseHooks(hooks =>
                {
                    hooks.OnRequestReceived = _ => throw new JsonException(Secret);
                });
            },
            configureMiddleware: ConfigureGenericErrorHandling(GenericError));
        var payload = new
        {
            action = "create",
            items = new[]
            {
                new { id = 7, name = "Created" }
            }
        };

        using (host)
        using (client)
        {
            // Act
            var response = await client.PostAsJsonAsync("/api/items/batch", payload);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            var body = await response.Content.ReadAsStringAsync();
            body.Should().Be(GenericError);
            body.Should().NotContain(Secret);
        }
    }

    [Fact]
    public async Task MappedBatchCreate_RequestHookThrowsInvalidOperationException_UsesHostErrorHandling()
    {
        // Arrange
        const string Secret = "internal mapped hook implementation detail";
        const string GenericError = "generic server failure";
        var (host, client) = await new TestTwoModelHostBuilder<
                AdapterContractEntity,
                AdapterContractEntity,
                int>(new AdapterContractRepository(patchException: null), "/api/mapped-items")
            .WithServices(services =>
            {
                services.AddRestLibMapper<AdapterContractEntity, AdapterContractEntity>(
                    _ => new AdapterContractIdentityMapper());
            })
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                config.EnableBatch(BatchAction.Create);
                config.UseHooks(hooks =>
                {
                    hooks.OnRequestReceived = _ => throw new InvalidOperationException(Secret);
                });
            })
            .WithMiddleware(ConfigureGenericErrorHandling(GenericError))
            .BuildAsync();
        var payload = new
        {
            action = "create",
            items = new[]
            {
                new { id = 7, name = "Created" }
            }
        };

        using (host)
        using (client)
        {
            // Act
            var response = await client.PostAsJsonAsync("/api/mapped-items/batch", payload);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            var body = await response.Content.ReadAsStringAsync();
            body.Should().Be(GenericError);
            body.Should().NotContain(Secret);
        }
    }

    [Fact]
    public async Task Batch_UndefinedNumericAction_ReturnsInvalidBatchRequest()
    {
        // Arrange
        var (host, client) = await CreateHostAsync(
            patchException: null,
            configureEndpoint: config => config.EnableBatch(BatchAction.Create));
        var payload = new
        {
            action = "999",
            items = new[]
            {
                new { id = 7, name = "Ignored" }
            }
        };

        using (host)
        using (client)
        {
            // Act
            var response = await client.PostAsJsonAsync("/api/items/batch", payload);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var problem = await response.Content.ReadFromJsonAsync<RestLibProblemDetails>(JsonOptions);
            problem.Should().NotBeNull();
            problem!.Type.Should().Be(ProblemTypes.InvalidBatchRequest);
            problem.Detail.Should().Contain("999");
            problem.Detail.Should().Contain("not a valid batch action");
        }
    }

    private static Task<(IHost Host, HttpClient Client)> CreateHostAsync(
        Exception? patchException,
        Action<RestLibEndpointConfiguration<AdapterContractEntity, int>>? configureEndpoint = null,
        Action<IApplicationBuilder>? configureMiddleware = null)
    {
        var builder = new TestHostBuilder<AdapterContractEntity, int>(
                new AdapterContractRepository(patchException),
                "/api/items")
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
                configureEndpoint?.Invoke(config);
            });

        if (configureMiddleware is not null)
        {
            builder.WithMiddleware(configureMiddleware);
        }

        return builder.BuildAsync();
    }

    private static Action<IApplicationBuilder> ConfigureGenericErrorHandling(string responseBody)
    {
        return app => app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (Exception)
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsync(responseBody);
            }
        });
    }

    private sealed class AdapterContractRepository : IRepository<AdapterContractEntity, int>
    {
        private readonly Exception? _patchException;
        private readonly AdapterContractEntity _knownEntity = new() { Id = KnownId, Name = "Original" };

        internal AdapterContractRepository(Exception? patchException)
        {
            _patchException = patchException;
        }

        public Task<AdapterContractEntity?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            return Task.FromResult<AdapterContractEntity?>(id == KnownId ? _knownEntity : null);
        }

        public Task<PagedResult<AdapterContractEntity>> GetAllAsync(
            PaginationRequest pagination,
            CancellationToken ct = default)
        {
            return Task.FromResult(new PagedResult<AdapterContractEntity>
            {
                Items = [_knownEntity]
            });
        }

        public Task<AdapterContractEntity> CreateAsync(
            AdapterContractEntity entity,
            CancellationToken ct = default)
        {
            return Task.FromResult(entity);
        }

        public Task<AdapterContractEntity?> UpdateAsync(
            int id,
            AdapterContractEntity entity,
            CancellationToken ct = default)
        {
            if (_patchException is not null)
            {
                throw _patchException;
            }

            return Task.FromResult<AdapterContractEntity?>(id == KnownId ? entity : null);
        }

        public Task<AdapterContractEntity?> PatchAsync(
            int id,
            JsonElement patchDocument,
            CancellationToken ct = default)
        {
            if (_patchException is not null)
            {
                throw _patchException;
            }

            return Task.FromResult<AdapterContractEntity?>(id == KnownId ? _knownEntity : null);
        }

        public Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            return Task.FromResult(id == KnownId);
        }
    }

    private sealed class RenamedAdapterPatchValidationException : PatchValidationException
    {
        internal RenamedAdapterPatchValidationException(string message)
            : base(message)
        {
        }
    }

    private sealed class AdapterContractIdentityMapper
        : IRestLibMapper<AdapterContractEntity, AdapterContractEntity>
    {
        public AdapterContractEntity ToApi(AdapterContractEntity dbModel)
        {
            return dbModel;
        }

        public AdapterContractEntity ToDb(AdapterContractEntity apiModel)
        {
            return apiModel;
        }
    }

    private sealed class AdapterContractEntity
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }
}
