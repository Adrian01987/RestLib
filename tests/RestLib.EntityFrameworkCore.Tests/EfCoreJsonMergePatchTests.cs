using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RestLib.Serialization;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

/// <summary>
/// Verifies RFC 7396 behavior in the EF Core repository adapter.
/// </summary>
[Trait("Category", "Story3.2.4")]
public class EfCoreJsonMergePatchTests
{
    private static readonly JsonSerializerOptions JsonOptions = RestLibJsonOptions.CreateDefault();

    [Fact]
    public async Task PatchAsync_JsonConvertedObject_RecursivelyMergesMembers()
    {
        // Arrange
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        var repository = CreateRepository(context);
        var entity = CreateEntity();
        await repository.CreateAsync(entity);
        var patch = ParsePatch("""{"details":{"city":"Shelbyville"}}""");

        // Act
        var result = await repository.PatchAsync(entity.Id, patch);

        // Assert
        result.Should().NotBeNull();
        result!.Details.Street.Should().Be("123 Main St");
        result.Details.City.Should().Be("Shelbyville");
        result.Details.Tags.Should().Equal("original", "tags");
    }

    [Fact]
    public async Task PatchManyAsync_JsonConvertedObject_MergesObjectsAndReplacesArrays()
    {
        // Arrange
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        var repository = CreateRepository(context);
        var first = CreateEntity();
        var second = CreateEntity();
        await repository.CreateManyAsync([first, second]);
        var firstPatch = ParsePatch(
            """{"details":{"city":"First City","tags":["replacement"]}}""");
        var secondPatch = ParsePatch(
            """{"details":{"street":null,"city":"Second City"}}""");

        // Act
        var results = await repository.PatchManyAsync(
            [(first.Id, firstPatch), (second.Id, secondPatch)]);

        // Assert
        results.Should().HaveCount(2);
        results[0].Details.Street.Should().Be("123 Main St");
        results[0].Details.City.Should().Be("First City");
        results[0].Details.Tags.Should().Equal("replacement");
        results[1].Details.Street.Should().BeNull();
        results[1].Details.City.Should().Be("Second City");
        results[1].Details.Tags.Should().Equal("original", "tags");
    }

    [Fact]
    public async Task PatchAsync_CustomResolverName_UpdatesMappedProperty()
    {
        // Arrange
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        var repository = CreateContractRepository(context, CreateContractOptions());
        var entity = CreateContractEntity();
        await repository.CreateAsync(entity);
        var patch = ParsePatch("""{"display-label":"Updated"}""");

        // Act
        var result = await repository.PatchAsync(entity.Id, patch);

        // Assert
        result.Should().NotBeNull();
        result!.DisplayName.Should().Be("Updated");
    }

    [Fact]
    public async Task PatchAsync_JsonIgnoredMemberInStrictMode_RejectsPatch()
    {
        // Arrange
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        var repository = CreateContractRepository(
            context,
            CreateContractOptions(),
            EfCorePatchUnknownFieldBehavior.Strict);
        var entity = CreateContractEntity();
        await repository.CreateAsync(entity);
        var patch = ParsePatch("""{"ignored_value":"Changed"}""");

        // Act
        Func<Task> act = () => repository.PatchAsync(entity.Id, patch);

        // Assert
        await act.Should().ThrowAsync<EfCorePatchValidationException>()
            .WithMessage("*ignored_value*unknown*");
        await context.Entry(entity).ReloadAsync();
        entity.IgnoredValue.Should().Be("Unchanged");
    }

    [Fact]
    public async Task PatchAsync_PropertyConverter_UsesMemberConverter()
    {
        // Arrange
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        var repository = CreateContractRepository(context, CreateContractOptions());
        var entity = CreateContractEntity();
        await repository.CreateAsync(entity);
        var patch = ParsePatch("""{"converted_value":"wire:Updated"}""");

        // Act
        var result = await repository.PatchAsync(entity.Id, patch);

        // Assert
        result.Should().NotBeNull();
        result!.ConvertedValue.Should().Be("Updated");
    }

    [Fact]
    public async Task PatchAsync_PropertyNumberHandling_AllowsQuotedNumber()
    {
        // Arrange
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        var repository = CreateContractRepository(context, CreateContractOptions());
        var entity = CreateContractEntity();
        await repository.CreateAsync(entity);
        var patch = ParsePatch("""{"quantity":"42"}""");

        // Act
        var result = await repository.PatchAsync(entity.Id, patch);

        // Assert
        result.Should().NotBeNull();
        result!.Quantity.Should().Be(42);
    }

    [Fact]
    public async Task PatchAsync_CaseSensitiveContract_RejectsLegacyClrAlias()
    {
        // Arrange
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        var repository = CreateContractRepository(
            context,
            CreateContractOptions(propertyNameCaseInsensitive: false),
            EfCorePatchUnknownFieldBehavior.Strict);
        var entity = CreateContractEntity();
        await repository.CreateAsync(entity);
        var patch = ParsePatch("""{"DisplayName":"Changed"}""");

        // Act
        Func<Task> act = () => repository.PatchAsync(entity.Id, patch);

        // Assert
        await act.Should().ThrowAsync<EfCorePatchValidationException>()
            .WithMessage("*DisplayName*unknown*");
        entity.DisplayName.Should().Be("Original");
    }

    [Fact]
    public async Task PatchAsync_CaseInsensitiveContract_AcceptsLegacySnakeAlias()
    {
        // Arrange
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        var repository = CreateContractRepository(context, CreateContractOptions());
        var entity = CreateContractEntity();
        await repository.CreateAsync(entity);
        var patch = ParsePatch("""{"display_name":"Changed"}""");

        // Act
        var result = await repository.PatchAsync(entity.Id, patch);

        // Assert
        result.Should().NotBeNull();
        result!.DisplayName.Should().Be("Changed");
    }

    private static MergePatchDbContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<MergePatchDbContext>()
            .UseSqlite(connection)
            .Options;
        return new MergePatchDbContext(options);
    }

    private static EfCoreRepository<MergePatchDbContext, MergePatchEntity, Guid> CreateRepository(
        MergePatchDbContext context) =>
        new(context, new EfCoreRepositoryOptions<MergePatchEntity, Guid>(), JsonOptions);

    private static EfCoreRepository<MergePatchDbContext, ContractPatchEntity, Guid>
        CreateContractRepository(
            MergePatchDbContext context,
            JsonSerializerOptions jsonOptions,
            EfCorePatchUnknownFieldBehavior unknownFieldBehavior =
                EfCorePatchUnknownFieldBehavior.Permissive) =>
        new(
            context,
            new EfCoreRepositoryOptions<ContractPatchEntity, Guid>
            {
                PatchUnknownFieldBehavior = unknownFieldBehavior
            },
            jsonOptions);

    private static JsonSerializerOptions CreateContractOptions(
        bool propertyNameCaseInsensitive = true)
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(typeInfo =>
        {
            if (typeInfo.Type != typeof(ContractPatchEntity))
            {
                return;
            }

            var property = typeInfo.Properties.Single(candidate =>
                candidate.AttributeProvider is PropertyInfo propertyInfo
                && propertyInfo.Name == nameof(ContractPatchEntity.DisplayName));
            property.Name = "display-label";
        });

        var options = RestLibJsonOptions.CreateDefault();
        options.PropertyNameCaseInsensitive = propertyNameCaseInsensitive;
        options.TypeInfoResolver = resolver;
        return options;
    }

    private static MergePatchEntity CreateEntity() =>
        new()
        {
            Id = Guid.NewGuid(),
            Details = new MergePatchDetails
            {
                Street = "123 Main St",
                City = "Springfield",
                Tags = ["original", "tags"]
            }
        };

    private static ContractPatchEntity CreateContractEntity() =>
        new()
        {
            Id = Guid.NewGuid(),
            DisplayName = "Original",
            IgnoredValue = "Unchanged",
            ConvertedValue = "Original",
            Quantity = 1
        };

    private static JsonElement ParsePatch(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class MergePatchDbContext(DbContextOptions<MergePatchDbContext> options)
        : DbContext(options)
    {
        public DbSet<MergePatchEntity> Entities => Set<MergePatchEntity>();

        public DbSet<ContractPatchEntity> ContractEntities => Set<ContractPatchEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<MergePatchEntity>()
                .Property(entity => entity.Details)
                .HasConversion(
                    details => JsonSerializer.Serialize(details, JsonOptions),
                    json => JsonSerializer.Deserialize<MergePatchDetails>(json, JsonOptions)!);
        }
    }

    private sealed class MergePatchEntity
    {
        public Guid Id { get; set; }

        public MergePatchDetails Details { get; set; } = new();
    }

    private sealed class MergePatchDetails
    {
        public string? Street { get; set; }

        public string? City { get; set; }

        public string[] Tags { get; set; } = [];
    }

    private sealed class ContractPatchEntity
    {
        public Guid Id { get; set; }

        public string DisplayName { get; set; } = string.Empty;

        [JsonIgnore]
        public string IgnoredValue { get; set; } = string.Empty;

        [JsonConverter(typeof(WireStringConverter))]
        public string ConvertedValue { get; set; } = string.Empty;

        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int Quantity { get; set; }
    }

    /// <summary>
    /// Converts strings to and from the wire-prefixed representation used by the PATCH tests.
    /// </summary>
    public sealed class WireStringConverter : JsonConverter<string>
    {
        /// <inheritdoc />
        public override string? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            var value = reader.GetString();
            if (value is null || !value.StartsWith("wire:", StringComparison.Ordinal))
            {
                throw new JsonException("Expected a wire-prefixed string.");
            }

            return value["wire:".Length..];
        }

        /// <inheritdoc />
        public override void Write(
            Utf8JsonWriter writer,
            string value,
            JsonSerializerOptions options) =>
            writer.WriteStringValue($"wire:{value}");
    }
}
