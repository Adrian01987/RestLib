using System.Text.Json;
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

    private static JsonElement ParsePatch(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class MergePatchDbContext(DbContextOptions<MergePatchDbContext> options)
        : DbContext(options)
    {
        public DbSet<MergePatchEntity> Entities => Set<MergePatchEntity>();

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
}
