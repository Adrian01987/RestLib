using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RestLib.EntityFrameworkCore.Tests.Fakes;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

[Trait("Type", "Integration")]
[Trait("Feature", "Identity")]
public sealed class EfCoreAlternateKeyIdentityTests
{
    [Fact]
    public async Task UpdateAsync_AlternateBodyKeyDiffersFromRouteKey_PreservesRouteAndStorageIdentity()
    {
        // Arrange
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDbContext(connection);
        await db.Database.EnsureCreatedAsync();
        var routeKey = Guid.NewGuid();
        var bodyKey = Guid.NewGuid();
        db.IntKeyEntities.Add(new IntKeyEntity
        {
            Id = 42,
            ExternalId = routeKey,
            Name = "Original"
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var repository = CreateRepository(db);
        var replacement = new IntKeyEntity
        {
            Id = 999,
            ExternalId = bodyKey,
            Name = "Updated"
        };

        // Act
        var result = await repository.UpdateAsync(routeKey, replacement);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(42);
        result.ExternalId.Should().Be(routeKey);
        result.Name.Should().Be("Updated");
        db.ChangeTracker.Clear();
        (await repository.GetByIdAsync(routeKey)).Should().BeEquivalentTo(result);
        (await repository.GetByIdAsync(bodyKey)).Should().BeNull();
    }

    [Fact]
    public async Task PatchAsync_AlternateKeyFieldIsPresent_RejectsPatchAndPreservesTrackedEntity()
    {
        // Arrange
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDbContext(connection);
        await db.Database.EnsureCreatedAsync();
        var routeKey = Guid.NewGuid();
        var entity = new IntKeyEntity
        {
            Id = 42,
            ExternalId = routeKey,
            Name = "Original"
        };
        db.IntKeyEntities.Add(entity);
        await db.SaveChangesAsync();
        var repository = CreateRepository(db);
        using var patchDocument = JsonDocument.Parse(
            $$"""{"external_id":"{{Guid.NewGuid()}}","name":"Should not persist"}""");

        // Act
        var act = () => repository.PatchAsync(routeKey, patchDocument.RootElement);

        // Assert
        await act.Should().ThrowAsync<EfCorePatchValidationException>()
            .WithMessage("*immutable resource key field 'external_id'*");
        entity.ExternalId.Should().Be(routeKey);
        entity.Name.Should().Be("Original");
        db.Entry(entity).State.Should().Be(EntityState.Unchanged);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var persisted = await repository.GetByIdAsync(routeKey);
        persisted.Should().NotBeNull();
        persisted!.ExternalId.Should().Be(routeKey);
        persisted.Name.Should().Be("Original");
    }

    private static KeyDetectionTestDbContext CreateDbContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<KeyDetectionTestDbContext>()
            .UseSqlite(connection)
            .Options;
        return new KeyDetectionTestDbContext(options);
    }

    private static EfCoreRepository<KeyDetectionTestDbContext, IntKeyEntity, Guid> CreateRepository(
        KeyDetectionTestDbContext db)
    {
        return new EfCoreRepository<KeyDetectionTestDbContext, IntKeyEntity, Guid>(
            db,
            new EfCoreRepositoryOptions<IntKeyEntity, Guid>
            {
                KeySelector = entity => entity.ExternalId
            });
    }
}
