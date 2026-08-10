using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestLib.Abstractions;
using RestLib.Batch;
using RestLib.InMemory;
using RestLib.Pagination;
using RestLib.Responses;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Verifies that malformed batch members remain isolated from their valid siblings.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "Batch")]
public class BatchItemDeserializationTests
{
    [Theory]
    [InlineData("update", 200)]
    [InlineData("delete", 204)]
    public async Task ScalarBatch_CustomKeyConverter_DoesNotRoundTripParsedKey(
        string action,
        int itemStatus)
    {
        // Arrange
        var key = new ReadOnlyBatchKey(Guid.NewGuid());
        var converter = new ReadOnlyBatchKeyJsonConverter();
        var repository = new InMemoryRepository<ReadOnlyBatchKeyEntity, ReadOnlyBatchKey>(
            static entity => entity.Id,
            static () => new ReadOnlyBatchKey(Guid.NewGuid()));
        repository.Seed([new ReadOnlyBatchKeyEntity { Id = key, Name = "Original" }]);

        IHost? host = null;
        HttpClient? client = null;
        try
        {
            (host, client) = await new TestHostBuilder<ReadOnlyBatchKeyEntity, ReadOnlyBatchKey>(
                    repository,
                    "/api/converter-keys")
                .WithServices(services => services.ConfigureHttpJsonOptions(options =>
                    options.SerializerOptions.Converters.Add(converter)))
                .WithEndpoint(static config =>
                {
                    config.AllowAnonymous();
                    config.EnableBatch();
                })
                .BuildAsync();
            var requestJson = action switch
            {
                "update" => $$"""
                    {
                      "action": "update",
                      "items": [
                        {
                          "id": "{{key.Value}}",
                          "body": { "name": "Updated" }
                        }
                      ]
                    }
                    """,
                "delete" => $$"""
                    {
                      "action": "delete",
                      "items": ["{{key.Value}}"]
                    }
                    """,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(action),
                    action,
                    "Unsupported batch action."),
            };
            var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            // Act
            var response = await client.PostAsync("/api/converter-keys/batch", content);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var result = json.GetProperty("items").EnumerateArray().Should().ContainSingle().Which;
            result.GetProperty("index").GetInt32().Should().Be(0);
            result.GetProperty("status").GetInt32().Should().Be(itemStatus);
            converter.ReadCount.Should().Be(1);
            converter.WriteCount.Should().Be(0);

            var stored = await repository.GetByIdAsync(key);
            if (action == "update")
            {
                stored.Should().NotBeNull();
                stored!.Name.Should().Be("Updated");
            }
            else
            {
                stored.Should().BeNull();
            }
        }
        finally
        {
            client?.Dispose();
            if (host is not null)
            {
                await host.StopAsync();
                host.Dispose();
            }
        }
    }

    [Theory]
    [InlineData("create", 201)]
    [InlineData("update", 200)]
    [InlineData("patch", 200)]
    [InlineData("delete", 204)]
    public async Task UnmappedBatch_MalformedMiddleItem_ReturnsIndexedErrorAndProcessesValidSiblings(
        string action,
        int successStatus)
    {
        // Arrange
        var ids = new ScenarioIds(Guid.NewGuid(), Guid.NewGuid());
        var repository = new InMemoryRepository<BatchDeserializationEntity, Guid>(
            static entity => entity.Id,
            Guid.NewGuid);
        SeedUnmapped(repository, action, ids);

        IHost? host = null;
        HttpClient? client = null;
        try
        {
            (host, client) = await new TestHostBuilder<BatchDeserializationEntity, Guid>(
                    repository,
                    "/api/items")
                .WithEndpoint(static config =>
                {
                    config.AllowAnonymous();
                    config.EnableBatch();
                })
                .BuildAsync();

            // Act
            var response = await client.PostAsync("/api/items/batch", BuildRequest(action, ids));

            // Assert
            var items = await AssertMixedResponseAsync(response, successStatus);
            await AssertUnmappedStateAsync(repository, action, ids, items);
        }
        finally
        {
            client?.Dispose();
            if (host is not null)
            {
                await host.StopAsync();
                host.Dispose();
            }
        }
    }

    [Theory]
    [InlineData("create", 201)]
    [InlineData("update", 200)]
    [InlineData("patch", 200)]
    [InlineData("delete", 204)]
    public async Task MappedBatch_MalformedMiddleItem_ReturnsIndexedErrorAndProcessesValidSiblings(
        string action,
        int successStatus)
    {
        // Arrange
        var ids = new ScenarioIds(Guid.NewGuid(), Guid.NewGuid());
        var repository = new InMemoryRepository<BatchDeserializationDbEntity, Guid>(
            static entity => entity.Id,
            Guid.NewGuid);
        SeedMapped(repository, action, ids);

        IHost? host = null;
        HttpClient? client = null;
        try
        {
            (host, client) = await new TestTwoModelHostBuilder<
                    BatchDeserializationApiEntity,
                    BatchDeserializationDbEntity,
                    Guid>(repository, "/api/items")
                .WithServices(static services =>
                    services.AddRestLibMapper<BatchDeserializationApiEntity, BatchDeserializationDbEntity>(
                        static _ => new BatchDeserializationMapper()))
                .WithEndpoint(static config =>
                {
                    config.AllowAnonymous();
                    config.EnableBatch();
                })
                .BuildAsync();

            // Act
            var response = await client.PostAsync("/api/items/batch", BuildRequest(action, ids));

            // Assert
            var items = await AssertMixedResponseAsync(response, successStatus);
            await AssertMappedStateAsync(repository, action, ids, items);
        }
        finally
        {
            client?.Dispose();
            if (host is not null)
            {
                await host.StopAsync();
                host.Dispose();
            }
        }
    }

    [Fact]
    public async Task Batch_NonArrayItems_ReturnsEnvelopeErrorWithoutPersistence()
    {
        // Arrange
        var repository = new InMemoryRepository<BatchDeserializationEntity, Guid>(
            static entity => entity.Id,
            Guid.NewGuid);
        IHost? host = null;
        HttpClient? client = null;
        try
        {
            (host, client) = await new TestHostBuilder<BatchDeserializationEntity, Guid>(
                    repository,
                    "/api/items")
                .WithEndpoint(static config =>
                {
                    config.AllowAnonymous();
                    config.EnableBatch();
                })
                .BuildAsync();
            var content = new StringContent(
                """
                {
                  "action": "create",
                  "items": { "name": "not-an-array" }
                }
                """,
                Encoding.UTF8,
                "application/json");

            // Act
            var response = await client.PostAsync("/api/items/batch", content);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            problem.GetProperty("type").GetString().Should().Be(ProblemTypes.InvalidBatchRequest);
            var stored = await repository.GetAllAsync(new PaginationRequest());
            stored.Items.Should().BeEmpty();
        }
        finally
        {
            client?.Dispose();
            if (host is not null)
            {
                await host.StopAsync();
                host.Dispose();
            }
        }
    }

    private static StringContent BuildRequest(string action, ScenarioIds ids)
    {
        object[] items = action switch
        {
            "create" =>
            [
                (object)new { name = "Created First", price = 11m, is_active = true },
                42,
                new { name = "Created Third", price = 33m, is_active = true },
            ],
            "update" =>
            [
                (object)new
                {
                    id = ids.First,
                    body = new { name = "Updated First", price = 11m, is_active = true },
                },
                42,
                new
                {
                    id = ids.Third,
                    body = new { name = "Updated Third", price = 33m, is_active = true },
                },
            ],
            "patch" =>
            [
                (object)new { id = ids.First, body = new { price = 11m } },
                42,
                new { id = ids.Third, body = new { price = 33m } },
            ],
            "delete" => [(object)ids.First, 42, ids.Third],
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unsupported batch action."),
        };
        var json = JsonSerializer.Serialize(
            new { action, items },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static async Task<JsonElement> AssertMixedResponseAsync(
        HttpResponseMessage response,
        int successStatus)
    {
        response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(3);
        items.EnumerateArray().Select(static item => item.GetProperty("index").GetInt32())
            .Should().Equal(0, 1, 2);
        items.EnumerateArray().Select(static item => item.GetProperty("status").GetInt32())
            .Should().Equal(successStatus, 400, successStatus);

        var malformed = items[1];
        malformed.TryGetProperty("entity", out _).Should().BeFalse();
        var error = malformed.GetProperty("error");
        error.GetProperty("type").GetString().Should().Be(ProblemTypes.BadRequest);
        error.GetProperty("status").GetInt32().Should().Be(400);
        error.GetProperty("detail").GetString().Should()
            .Contain("index 1")
            .And.NotContain("System.")
            .And.NotContain("JSON value")
            .And.NotContain("$[");
        return items;
    }

    private static void SeedUnmapped(
        InMemoryRepository<BatchDeserializationEntity, Guid> repository,
        string action,
        ScenarioIds ids)
    {
        if (action == "create")
        {
            return;
        }

        repository.Seed([
            new BatchDeserializationEntity
            {
                Id = ids.First,
                Name = "Original First",
                Price = 1m,
                IsActive = true,
            },
            new BatchDeserializationEntity
            {
                Id = ids.Third,
                Name = "Original Third",
                Price = 3m,
                IsActive = true,
            },
        ]);
    }

    private static void SeedMapped(
        InMemoryRepository<BatchDeserializationDbEntity, Guid> repository,
        string action,
        ScenarioIds ids)
    {
        if (action == "create")
        {
            return;
        }

        repository.Seed([
            new BatchDeserializationDbEntity
            {
                Id = ids.First,
                Name = "Original First",
                Price = 1m,
                IsActive = true,
                InternalValue = "seeded",
            },
            new BatchDeserializationDbEntity
            {
                Id = ids.Third,
                Name = "Original Third",
                Price = 3m,
                IsActive = true,
                InternalValue = "seeded",
            },
        ]);
    }

    private static async Task AssertUnmappedStateAsync(
        InMemoryRepository<BatchDeserializationEntity, Guid> repository,
        string action,
        ScenarioIds ids,
        JsonElement items)
    {
        if (action == "create")
        {
            var firstId = items[0].GetProperty("entity").GetProperty("id").GetGuid();
            var thirdId = items[2].GetProperty("entity").GetProperty("id").GetGuid();
            (await repository.GetByIdAsync(firstId))!.Name.Should().Be("Created First");
            (await repository.GetByIdAsync(thirdId))!.Name.Should().Be("Created Third");
            return;
        }

        var first = await repository.GetByIdAsync(ids.First);
        var third = await repository.GetByIdAsync(ids.Third);
        AssertStoredState(action, first, third);
    }

    private static async Task AssertMappedStateAsync(
        InMemoryRepository<BatchDeserializationDbEntity, Guid> repository,
        string action,
        ScenarioIds ids,
        JsonElement items)
    {
        if (action == "create")
        {
            var firstId = items[0].GetProperty("entity").GetProperty("id").GetGuid();
            var thirdId = items[2].GetProperty("entity").GetProperty("id").GetGuid();
            (await repository.GetByIdAsync(firstId))!.Name.Should().Be("Created First");
            (await repository.GetByIdAsync(thirdId))!.Name.Should().Be("Created Third");
            return;
        }

        var first = await repository.GetByIdAsync(ids.First);
        var third = await repository.GetByIdAsync(ids.Third);
        AssertStoredState(action, first, third);
    }

    private static void AssertStoredState(
        string action,
        BatchDeserializationEntity? first,
        BatchDeserializationEntity? third)
    {
        switch (action)
        {
            case "update":
                first.Should().NotBeNull();
                first!.Name.Should().Be("Updated First");
                first.Price.Should().Be(11m);
                third.Should().NotBeNull();
                third!.Name.Should().Be("Updated Third");
                third.Price.Should().Be(33m);
                break;
            case "patch":
                first.Should().NotBeNull();
                first!.Name.Should().Be("Original First");
                first.Price.Should().Be(11m);
                third.Should().NotBeNull();
                third!.Name.Should().Be("Original Third");
                third.Price.Should().Be(33m);
                break;
            case "delete":
                first.Should().BeNull();
                third.Should().BeNull();
                break;
        }
    }

    private static void AssertStoredState(
        string action,
        BatchDeserializationDbEntity? first,
        BatchDeserializationDbEntity? third)
    {
        switch (action)
        {
            case "update":
                first.Should().NotBeNull();
                first!.Name.Should().Be("Updated First");
                first.Price.Should().Be(11m);
                third.Should().NotBeNull();
                third!.Name.Should().Be("Updated Third");
                third.Price.Should().Be(33m);
                break;
            case "patch":
                first.Should().NotBeNull();
                first!.Name.Should().Be("Original First");
                first.Price.Should().Be(11m);
                third.Should().NotBeNull();
                third!.Name.Should().Be("Original Third");
                third.Price.Should().Be(33m);
                break;
            case "delete":
                first.Should().BeNull();
                third.Should().BeNull();
                break;
        }
    }

    private readonly record struct ScenarioIds(Guid First, Guid Third);

    private sealed class BatchDeserializationEntity
    {
        public Guid Id { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        public decimal Price { get; set; }

        public bool IsActive { get; set; }
    }

    private sealed class BatchDeserializationApiEntity
    {
        public Guid Id { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        public decimal Price { get; set; }

        public bool IsActive { get; set; }
    }

    private sealed class BatchDeserializationDbEntity
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public decimal Price { get; set; }

        public bool IsActive { get; set; }

        public string InternalValue { get; set; } = string.Empty;
    }

    private readonly record struct ReadOnlyBatchKey(Guid Value) : IParsable<ReadOnlyBatchKey>
    {
        public static ReadOnlyBatchKey Parse(string value, IFormatProvider? provider)
        {
            return new ReadOnlyBatchKey(Guid.Parse(value, provider));
        }

        public static bool TryParse(
            string? value,
            IFormatProvider? provider,
            out ReadOnlyBatchKey result)
        {
            if (Guid.TryParse(value, provider, out var parsed))
            {
                result = new ReadOnlyBatchKey(parsed);
                return true;
            }

            result = default;
            return false;
        }

        public override string ToString() => Value.ToString();
    }

    private sealed class ReadOnlyBatchKeyEntity
    {
        [JsonIgnore]
        public ReadOnlyBatchKey Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class ReadOnlyBatchKeyJsonConverter : JsonConverter<ReadOnlyBatchKey>
    {
        public int ReadCount { get; private set; }

        public int WriteCount { get; private set; }

        public override ReadOnlyBatchKey Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            ReadCount++;
            if (!reader.TryGetGuid(out var value))
            {
                throw new JsonException("A converter key must be a JSON GUID string.");
            }

            return new ReadOnlyBatchKey(value);
        }

        public override void Write(
            Utf8JsonWriter writer,
            ReadOnlyBatchKey value,
            JsonSerializerOptions options)
        {
            WriteCount++;
            throw new JsonException("The converter is input-only.");
        }
    }

    private sealed class BatchDeserializationMapper :
        IRestLibMapper<BatchDeserializationApiEntity, BatchDeserializationDbEntity>
    {
        public BatchDeserializationApiEntity ToApi(BatchDeserializationDbEntity dbModel)
        {
            return new BatchDeserializationApiEntity
            {
                Id = dbModel.Id,
                Name = dbModel.Name,
                Price = dbModel.Price,
                IsActive = dbModel.IsActive,
            };
        }

        public BatchDeserializationDbEntity ToDb(BatchDeserializationApiEntity apiModel)
        {
            return new BatchDeserializationDbEntity
            {
                Id = apiModel.Id,
                Name = apiModel.Name,
                Price = apiModel.Price,
                IsActive = apiModel.IsActive,
                InternalValue = "mapped",
            };
        }
    }
}
