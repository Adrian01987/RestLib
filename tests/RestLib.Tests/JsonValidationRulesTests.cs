using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestLib.Abstractions;
using RestLib.Batch;
using RestLib.Configuration;
using RestLib.Hooks;
using RestLib.InMemory;
using RestLib.Responses;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

[Trait("Type", "Integration")]
[Trait("Feature", "Validation")]
public class JsonValidationRulesTests : IAsyncLifetime
{
    private IHost? _host;
    private HttpClient? _client;
    private InMemoryRepository<ValidatedEntity, Guid>? _repository;
    private ValidationHookCounter? _hookCounter;

    public Task InitializeAsync() => Task.CompletedTask;

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
    public async Task Create_WithJsonRequiredRule_ReturnsValidationProblem()
    {
        // Arrange
        await SetupHostAsync(config =>
        {
            config.Validation[nameof(ValidatedEntity.Description)] = new RestLibJsonValidationRuleConfiguration
            {
                Required = true,
            };
        });

        // Act
        var response = await _client!.PostAsJsonAsync("/api/validated", new
        {
            name = "Valid",
            unit_price = 10.0,
        });

        // Assert
        var problem = await response.ShouldBeProblemDetails(HttpStatusCode.BadRequest, ProblemTypes.ValidationFailed);
        problem.Errors.Should().ContainKey("description");
    }

    [Fact]
    public async Task Create_WithJsonMinRule_ReturnsValidationProblem()
    {
        // Arrange
        await SetupHostAsync(config =>
        {
            config.Validation[nameof(ValidatedEntity.UnitPrice)] = new RestLibJsonValidationRuleConfiguration
            {
                Min = 5m,
            };
        });

        // Act
        var response = await _client!.PostAsJsonAsync("/api/validated", new
        {
            name = "Valid",
            unit_price = 1.0,
        });

        // Assert
        var problem = await response.ShouldBeProblemDetails(HttpStatusCode.BadRequest, ProblemTypes.ValidationFailed);
        problem.Errors.Should().ContainKey("unit_price");
    }

    [Fact]
    public async Task Create_WithJsonMaxRule_ReturnsValidationProblem()
    {
        // Arrange
        await SetupHostAsync(config =>
        {
            config.Validation[nameof(ValidatedEntity.UnitPrice)] = new RestLibJsonValidationRuleConfiguration
            {
                Max = 5m,
            };
        });

        // Act
        var response = await _client!.PostAsJsonAsync("/api/validated", new
        {
            name = "Valid",
            unit_price = 10.0,
        });

        // Assert
        var problem = await response.ShouldBeProblemDetails(HttpStatusCode.BadRequest, ProblemTypes.ValidationFailed);
        problem.Errors.Should().ContainKey("unit_price");
    }

    [Fact]
    public async Task Create_WithJsonLengthRule_ReturnsValidationProblem()
    {
        // Arrange
        await SetupHostAsync(config =>
        {
            config.Validation[nameof(ValidatedEntity.Description)] = new RestLibJsonValidationRuleConfiguration
            {
                Length = new RestLibJsonLengthValidationConfiguration
                {
                    Min = 5,
                    Max = 10,
                },
            };
        });

        // Act
        var response = await _client!.PostAsJsonAsync("/api/validated", new
        {
            name = "Valid",
            unit_price = 10.0,
            description = "abc",
        });

        // Assert
        var problem = await response.ShouldBeProblemDetails(HttpStatusCode.BadRequest, ProblemTypes.ValidationFailed);
        problem.Errors.Should().ContainKey("description");
    }

    [Fact]
    public async Task Create_WithJsonPatternRule_ReturnsValidationProblem()
    {
        // Arrange
        await SetupHostAsync(config =>
        {
            config.Validation[nameof(ValidatedEntity.Description)] = new RestLibJsonValidationRuleConfiguration
            {
                Pattern = "^[A-Z]{3}$",
            };
        });

        // Act
        var response = await _client!.PostAsJsonAsync("/api/validated", new
        {
            name = "Valid",
            unit_price = 10.0,
            description = "bad",
        });

        // Assert
        var problem = await response.ShouldBeProblemDetails(HttpStatusCode.BadRequest, ProblemTypes.ValidationFailed);
        problem.Errors.Should().ContainKey("description");
    }

    [Fact]
    public async Task Create_WithJsonEmailRule_ReturnsValidationProblem()
    {
        // Arrange
        await SetupHostAsync(config =>
        {
            config.Validation[nameof(ValidatedEntity.ContactEmail)] = new RestLibJsonValidationRuleConfiguration
            {
                Email = true,
            };
        });

        // Act
        var response = await _client!.PostAsJsonAsync("/api/validated", new
        {
            name = "Valid",
            unit_price = 10.0,
            contact_email = "not-an-email",
        });

        // Assert
        var problem = await response.ShouldBeProblemDetails(HttpStatusCode.BadRequest, ProblemTypes.ValidationFailed);
        problem.Errors.Should().ContainKey("contact_email");
    }

    [Fact]
    public async Task Create_WithDataAnnotationAndJsonRulesFailing_ReturnsMergedErrors()
    {
        // Arrange
        await SetupHostAsync(config =>
        {
            config.Validation[nameof(ValidatedEntity.Name)] = new RestLibJsonValidationRuleConfiguration
            {
                Length = new RestLibJsonLengthValidationConfiguration
                {
                    Min = 5,
                },
            };
        });

        // Act
        var response = await _client!.PostAsJsonAsync("/api/validated", new
        {
            name = "",
            unit_price = 10.0,
        });

        // Assert
        var problem = await response.ShouldBeProblemDetails(HttpStatusCode.BadRequest, ProblemTypes.ValidationFailed);
        problem.Errors.Should().ContainKey("name");
        problem.Errors!["name"].Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public async Task Create_WithJsonValidationFailure_DoesNotRunBeforePersistHook()
    {
        // Arrange
        await SetupHostAsync(
            config =>
            {
                config.Validation[nameof(ValidatedEntity.Description)] = new RestLibJsonValidationRuleConfiguration
                {
                    Required = true,
                };
            },
            registerHook: true);

        // Act
        var response = await _client!.PostAsJsonAsync("/api/validated", new
        {
            name = "Valid",
            unit_price = 10.0,
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _hookCounter!.BeforePersistCalls.Should().Be(0);
    }

    [Fact]
    public async Task Update_WithJsonValidationFailure_DoesNotRunBeforePersistHook()
    {
        // Arrange
        await SetupHostAsync(
            config =>
            {
                config.Validation[nameof(ValidatedEntity.Description)] = new RestLibJsonValidationRuleConfiguration
                {
                    Required = true,
                };
            },
            registerHook: true);

        var id = Guid.NewGuid();
        _repository!.Seed([
            new ValidatedEntity
            {
                Id = id,
                Name = "Existing",
                UnitPrice = 10m,
                Description = "present",
            }
        ]);

        // Act
        var response = await _client!.PutAsJsonAsync($"/api/validated/{id}", new
        {
            name = "Updated",
            unit_price = 12.0,
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _hookCounter!.BeforePersistCalls.Should().Be(0);
    }

    [Fact]
    public async Task Patch_WithJsonValidationFailure_DoesNotPersistInvalidData()
    {
        // Arrange
        await SetupHostAsync(config =>
        {
            config.Validation[nameof(ValidatedEntity.Description)] = new RestLibJsonValidationRuleConfiguration
            {
                Length = new RestLibJsonLengthValidationConfiguration
                {
                    Min = 5,
                },
            };
        });

        var id = Guid.NewGuid();
        _repository!.Seed([
            new ValidatedEntity
            {
                Id = id,
                Name = "Existing",
                UnitPrice = 10m,
                Description = "valid-description",
            }
        ]);

        // Act
        var response = await _client!.PatchAsJsonAsync($"/api/validated/{id}", new
        {
            description = "bad",
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var persisted = await _repository.GetByIdAsync(id);
        persisted.Should().NotBeNull();
        persisted!.Description.Should().Be("valid-description");
    }

    [Fact]
    public async Task Patch_WithJsonValidationFailure_DoesNotRunBeforePersistHook()
    {
        // Arrange
        await SetupHostAsync(
            config =>
            {
                config.Validation[nameof(ValidatedEntity.Description)] = new RestLibJsonValidationRuleConfiguration
                {
                    Length = new RestLibJsonLengthValidationConfiguration
                    {
                        Min = 5,
                    },
                };
            },
            registerHook: true);

        var id = Guid.NewGuid();
        _repository!.Seed([
            new ValidatedEntity
            {
                Id = id,
                Name = "Existing",
                UnitPrice = 10m,
                Description = "valid-description",
            }
        ]);

        // Act
        var response = await _client!.PatchAsJsonAsync($"/api/validated/{id}", new
        {
            description = "bad",
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _hookCounter!.BeforePersistCalls.Should().Be(0);
    }

    [Fact]
    public async Task BatchCreate_WithJsonValidationFailure_ReturnsItemValidationProblem()
    {
        // Arrange
        await SetupHostAsync(config =>
        {
            config.Batch = new RestLibJsonBatchConfiguration
            {
                Actions = [BatchAction.Create],
            };

            config.Validation[nameof(ValidatedEntity.Description)] = new RestLibJsonValidationRuleConfiguration
            {
                Required = true,
            };
        });

        var payload = new
        {
            action = "create",
            items = new object[]
            {
                new { name = "Valid", unit_price = 10m, description = "present" },
                new { name = "Invalid", unit_price = 12m },
            },
        };

        // Act
        var response = await _client!.PostAsJsonAsync("/api/validated/batch", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("items")[1].GetProperty("status").GetInt32().Should().Be(400);
        json.GetProperty("items")[1].GetProperty("error").GetProperty("type").GetString()
            .Should().Be(ProblemTypes.ValidationFailed);
    }

    [Fact]
    public async Task BatchUpdate_WithJsonValidationFailure_DoesNotRunBeforePersistHook()
    {
        // Arrange
        await SetupHostAsync(
            config =>
            {
                config.Batch = new RestLibJsonBatchConfiguration
                {
                    Actions = [BatchAction.Update],
                };

                config.Validation[nameof(ValidatedEntity.Description)] = new RestLibJsonValidationRuleConfiguration
                {
                    Required = true,
                };
            },
            registerHook: true);

        var id = Guid.NewGuid();
        _repository!.Seed([
            new ValidatedEntity
            {
                Id = id,
                Name = "Existing",
                UnitPrice = 10m,
                Description = "present",
            }
        ]);

        var payload = new
        {
            action = "update",
            items = new object[]
            {
                new
                {
                    id,
                    body = new
                    {
                        name = "Updated",
                        unit_price = 12m,
                    }
                },
            },
        };

        // Act
        var response = await _client!.PostAsJsonAsync("/api/validated/batch", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("items")[0].GetProperty("status").GetInt32().Should().Be(400);
        _hookCounter!.BeforePersistCalls.Should().Be(0);
    }

    [Fact]
    public async Task BatchPatch_WithJsonValidationFailure_DoesNotRunBeforePersistHook()
    {
        // Arrange
        await SetupHostAsync(
            config =>
            {
                config.Batch = new RestLibJsonBatchConfiguration
                {
                    Actions = [BatchAction.Patch],
                };

                config.Validation[nameof(ValidatedEntity.Description)] = new RestLibJsonValidationRuleConfiguration
                {
                    Length = new RestLibJsonLengthValidationConfiguration
                    {
                        Min = 5,
                    },
                };
            },
            registerHook: true);

        var id = Guid.NewGuid();
        _repository!.Seed([
            new ValidatedEntity
            {
                Id = id,
                Name = "Existing",
                UnitPrice = 10m,
                Description = "valid-description",
            }
        ]);

        var payload = new
        {
            action = "patch",
            items = new object[]
            {
                new
                {
                    id,
                    body = new
                    {
                        description = "bad",
                    }
                },
            },
        };

        // Act
        var response = await _client!.PostAsJsonAsync("/api/validated/batch", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("items")[0].GetProperty("status").GetInt32().Should().Be(400);
        _hookCounter!.BeforePersistCalls.Should().Be(0);

        var persisted = await _repository.GetByIdAsync(id);
        persisted.Should().NotBeNull();
        persisted!.Description.Should().Be("valid-description");
    }

    [Fact]
    public async Task AddJsonResource_WithInvalidJsonValidationProperty_ThrowsOnMapping()
    {
        // Arrange
        var act = async () =>
        {
            await SetupHostAsync(config =>
            {
                config.Validation["DoesNotExist"] = new RestLibJsonValidationRuleConfiguration
                {
                    Required = true,
                };
            });
        };

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*validated*DoesNotExist*");
    }

    [Fact]
    public async Task AddJsonResource_WithInvalidJsonValidationRuleType_ThrowsOnMapping()
    {
        // Arrange
        var act = async () =>
        {
            await SetupHostAsync(config =>
            {
                config.Validation[nameof(ValidatedEntity.UnitPrice)] = new RestLibJsonValidationRuleConfiguration
                {
                    Pattern = "^[0-9]+$",
                };
            });
        };

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*UnitPrice*Pattern*");
    }

    private async Task SetupHostAsync(
        Action<RestLibJsonResourceConfiguration> configureJson,
        bool registerHook = false)
    {
        _hookCounter = new ValidationHookCounter();
        _repository = new InMemoryRepository<ValidatedEntity, Guid>(e => e.Id, Guid.NewGuid, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        });

        var jsonConfiguration = new RestLibJsonResourceConfiguration
        {
            Name = "validated",
            Route = "/api/validated",
            AllowAnonymousAll = true,
        };

        if (registerHook)
        {
            jsonConfiguration.Hooks = new RestLibJsonHookConfiguration
            {
                BeforePersist = new RestLibJsonHookStage
                {
                    Default = [HookNames.CountValidationHook],
                },
            };
        }

        configureJson(jsonConfiguration);

        var builder = new TestJsonHostBuilder()
            .WithServices(services =>
            {
                services.AddSingleton(_repository);
                services.AddSingleton<IRepository<ValidatedEntity, Guid>>(_repository);
                services.AddSingleton<IBatchRepository<ValidatedEntity, Guid>>(_repository);
                services.AddSingleton(_hookCounter);

                if (registerHook)
                {
                    services.AddNamedHook<ValidatedEntity, Guid>(HookNames.CountValidationHook, context =>
                    {
                        _hookCounter.BeforePersistCalls++;
                        return Task.CompletedTask;
                    });
                }

                services.AddJsonResource<ValidatedEntity, Guid>(jsonConfiguration);
            });

        (_host, _client) = await builder.BuildAsync();
    }

    private sealed class ValidationHookCounter
    {
        public int BeforePersistCalls { get; set; }
    }
}
