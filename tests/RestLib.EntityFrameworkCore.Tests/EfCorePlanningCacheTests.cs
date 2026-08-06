using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RestLib.FieldSelection;
using RestLib.Pagination;
using RestLib.Sorting;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

/// <summary>
/// Verifies EF Core planning-cache identity, boundedness, and live option behavior.
/// </summary>
public class EfCorePlanningCacheTests
{
    [Fact]
    public void GetOrCreate_SameModelAndOptions_ReusesImmutablePlanningBundle()
    {
        // Arrange
        var contextOptions = CreateOptions<PrimaryKeyPlanningContext>();
        using var firstContext = new PrimaryKeyPlanningContext(contextOptions);
        using var secondContext = new PrimaryKeyPlanningContext(contextOptions);
        var repositoryOptions = new EfCoreRepositoryOptions<PlanningEntity, Guid>();

        // Act
        var first = EfCoreRepositoryPlanCache<PlanningEntity, Guid>.GetOrCreate(
            firstContext.Model,
            repositoryOptions);
        var second = EfCoreRepositoryPlanCache<PlanningEntity, Guid>.GetOrCreate(
            secondContext.Model,
            repositoryOptions);

        // Assert
        secondContext.Model.Should().BeSameAs(firstContext.Model);
        second.Should().BeSameAs(first);
        second.KeyMetadata.Should().BeSameAs(first.KeyMetadata);
        second.PagePlanningCache.Should().BeSameAs(first.PagePlanningCache);
        second.ProjectionPlanningCache.Should().BeSameAs(first.ProjectionPlanningCache);
    }

    [Fact]
    public void GetOrCreate_DifferentModelsForSameClrTypes_IsolatesKeyMetadata()
    {
        // Arrange
        using var primaryContext = new PrimaryKeyPlanningContext(
            CreateOptions<PrimaryKeyPlanningContext>());
        using var alternateContext = new AlternateKeyPlanningContext(
            CreateOptions<AlternateKeyPlanningContext>());
        var repositoryOptions = new EfCoreRepositoryOptions<PlanningEntity, Guid>();
        var entity = new PlanningEntity
        {
            Id = Guid.NewGuid(),
            ExternalId = Guid.NewGuid()
        };

        // Act
        var primary = EfCoreRepositoryPlanCache<PlanningEntity, Guid>.GetOrCreate(
            primaryContext.Model,
            repositoryOptions);
        var alternate = EfCoreRepositoryPlanCache<PlanningEntity, Guid>.GetOrCreate(
            alternateContext.Model,
            repositoryOptions);

        // Assert
        alternateContext.Model.Should().NotBeSameAs(primaryContext.Model);
        alternate.Should().NotBeSameAs(primary);
        primary.KeyMetadata.KeyAccessor(entity).Should().Be(entity.Id);
        alternate.KeyMetadata.KeyAccessor(entity).Should().Be(entity.ExternalId);
    }

    [Fact]
    public void GetOrCreate_DifferentOptionsInstances_IsolatesResourceKeySelections()
    {
        // Arrange
        using var context = new PrimaryKeyPlanningContext(
            CreateOptions<PrimaryKeyPlanningContext>());
        var primaryOptions = new EfCoreRepositoryOptions<PlanningEntity, Guid>
        {
            KeySelector = entity => entity.Id
        };
        var alternateOptions = new EfCoreRepositoryOptions<PlanningEntity, Guid>
        {
            KeySelector = entity => entity.ExternalId
        };
        var entity = new PlanningEntity
        {
            Id = Guid.NewGuid(),
            ExternalId = Guid.NewGuid()
        };

        // Act
        var primary = EfCoreRepositoryPlanCache<PlanningEntity, Guid>.GetOrCreate(
            context.Model,
            primaryOptions);
        var alternate = EfCoreRepositoryPlanCache<PlanningEntity, Guid>.GetOrCreate(
            context.Model,
            alternateOptions);

        // Assert
        alternate.Should().NotBeSameAs(primary);
        primary.KeyMetadata.KeyAccessor(entity).Should().Be(entity.Id);
        alternate.KeyMetadata.KeyAccessor(entity).Should().Be(entity.ExternalId);
    }

    [Fact]
    public async Task RepositoryConstruction_KeySelectorChanged_RebuildsPlanningBundleAndUsesNewKey()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var contextOptions = new DbContextOptionsBuilder<PrimaryKeyPlanningContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new PrimaryKeyPlanningContext(contextOptions);
        await context.Database.EnsureCreatedAsync();
        var entity = new PlanningEntity
        {
            Id = Guid.NewGuid(),
            ExternalId = Guid.NewGuid(),
            Name = "mutable selector"
        };
        context.Entities.Add(entity);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repositoryOptions = new EfCoreRepositoryOptions<PlanningEntity, Guid>
        {
            KeySelector = candidate => candidate.Id
        };
        var originalBundle = EfCoreRepositoryPlanCache<PlanningEntity, Guid>.GetOrCreate(
            context.Model,
            repositoryOptions);
        var primaryRepository = new EfCoreRepository<PrimaryKeyPlanningContext, PlanningEntity, Guid>(
            context,
            repositoryOptions);
        var primaryResult = await primaryRepository.GetByIdAsync(entity.Id);

        // Act
        repositoryOptions.KeySelector = candidate => candidate.ExternalId;
        var replacementBundle = EfCoreRepositoryPlanCache<PlanningEntity, Guid>.GetOrCreate(
            context.Model,
            repositoryOptions);
        var alternateRepository = new EfCoreRepository<PrimaryKeyPlanningContext, PlanningEntity, Guid>(
            context,
            repositoryOptions);
        var alternateResult = await alternateRepository.GetByIdAsync(entity.ExternalId);

        // Assert
        primaryResult.Should().NotBeNull();
        alternateResult.Should().NotBeNull();
        replacementBundle.Should().NotBeSameAs(originalBundle);
        replacementBundle.KeyMetadata.KeyAccessor(entity).Should().Be(entity.ExternalId);
    }

    [Fact]
    public async Task GetAll_RepeatedSortShapeAcrossRepositories_ReusesKeysetPlan()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var contextOptions = new DbContextOptionsBuilder<PrimaryKeyPlanningContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new PrimaryKeyPlanningContext(contextOptions);
        await context.Database.EnsureCreatedAsync();
        var repositoryOptions = new EfCoreRepositoryOptions<PlanningEntity, Guid>
        {
            KeySelector = entity => entity.Id
        };
        var bundle = EfCoreRepositoryPlanCache<PlanningEntity, Guid>.GetOrCreate(
            context.Model,
            repositoryOptions);
        var firstRepository = new EfCoreRepository<PrimaryKeyPlanningContext, PlanningEntity, Guid>(
            context,
            repositoryOptions);
        var secondRepository = new EfCoreRepository<PrimaryKeyPlanningContext, PlanningEntity, Guid>(
            context,
            repositoryOptions);
        var request = CreatePaginationRequest(nameof(PlanningEntity.Name), "name", SortDirection.Asc);

        // Act
        await firstRepository.GetAllAsync(request);
        await secondRepository.GetAllAsync(request);

        // Assert
        bundle.PagePlanningCache.Count.Should().Be(1);
    }

    [Fact]
    public async Task PagePlanningCache_MoreShapesThanCapacity_DoesNotGrowPastCapacity()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var contextOptions = new DbContextOptionsBuilder<PrimaryKeyPlanningContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new PrimaryKeyPlanningContext(contextOptions);
        await context.Database.EnsureCreatedAsync();
        var keyMetadata = new EfCoreKeyMetadata<PlanningEntity, Guid>(
            context.Model,
            entity => entity.Id);
        var planningCache = new EfCorePageQueryExecutor<PlanningEntity>.PlanningCache(capacity: 2);
        var executor = new EfCorePageQueryExecutor<PlanningEntity>(
            context.Model,
            keyMetadata.SortKeyParts,
            static () => null,
            planningCache);
        var query = context.Entities.AsNoTracking();
        var first = CreatePaginationRequest(nameof(PlanningEntity.Name), "name", SortDirection.Asc);
        var second = CreatePaginationRequest(nameof(PlanningEntity.Quantity), "quantity", SortDirection.Asc);
        var third = CreatePaginationRequest(nameof(PlanningEntity.ExternalId), "external_id", SortDirection.Desc);

        // Act
        await executor.ExecuteAsync(query, first, CancellationToken.None);
        await executor.ExecuteAsync(query, second, CancellationToken.None);
        await executor.ExecuteAsync(query, third, CancellationToken.None);

        // Assert
        planningCache.Count.Should().Be(2);
    }

    [Fact]
    public void TryBuild_EquivalentProjectionPropertySets_ReusesNormalizedPlan()
    {
        // Arrange
        var planningCache = new EfCoreProjectionPlanner<PlanningEntity>.PlanningCache();
        var planner = new EfCoreProjectionPlanner<PlanningEntity>(
            static () => true,
            [nameof(PlanningEntity.Id)],
            planningCache);
        var firstFields = new SelectedField[]
        {
            CreateSelectedField(nameof(PlanningEntity.Name), "name"),
            CreateSelectedField(nameof(PlanningEntity.Quantity), "quantity")
        };
        var reversedFields = firstFields.Reverse().ToArray();

        // Act
        var firstSupported = planner.TryBuild(firstFields, [], [], null, out var firstPlan);
        var secondSupported = planner.TryBuild(reversedFields, [], [], null, out var secondPlan);

        // Assert
        firstSupported.Should().BeTrue();
        secondSupported.Should().BeTrue();
        secondPlan.Should().BeSameAs(firstPlan);
        planningCache.Count.Should().Be(1);
    }

    [Fact]
    public void TryBuild_ProjectionOptionChangedAfterConstruction_RemainsLateBound()
    {
        // Arrange
        var enabled = false;
        var planningCache = new EfCoreProjectionPlanner<PlanningEntity>.PlanningCache();
        var planner = new EfCoreProjectionPlanner<PlanningEntity>(
            () => enabled,
            [nameof(PlanningEntity.Id)],
            planningCache);
        var selectedFields = new[]
        {
            CreateSelectedField(nameof(PlanningEntity.Name), "name")
        };
        var initiallySupported = planner.TryBuild(selectedFields, [], [], null, out _);

        // Act
        enabled = true;
        var subsequentlySupported = planner.TryBuild(selectedFields, [], [], null, out var plan);

        // Assert
        initiallySupported.Should().BeFalse();
        subsequentlySupported.Should().BeTrue();
        plan.Should().NotBeNull();
        planningCache.Count.Should().Be(1);
    }

    [Fact]
    public void ProjectionPlanningCache_MoreShapesThanCapacity_DoesNotGrowPastCapacity()
    {
        // Arrange
        var planningCache = new EfCoreProjectionPlanner<PlanningEntity>.PlanningCache(capacity: 2);
        var planner = new EfCoreProjectionPlanner<PlanningEntity>(
            static () => true,
            [nameof(PlanningEntity.Id)],
            planningCache);

        // Act
        planner.TryBuild(
            [CreateSelectedField(nameof(PlanningEntity.Name), "name")],
            [],
            [],
            null,
            out _);
        planner.TryBuild(
            [CreateSelectedField(nameof(PlanningEntity.Quantity), "quantity")],
            [],
            [],
            null,
            out _);
        planner.TryBuild(
            [CreateSelectedField(nameof(PlanningEntity.ExternalId), "external_id")],
            [],
            [],
            null,
            out _);

        // Assert
        planningCache.Count.Should().Be(2);
    }

    private static DbContextOptions<TContext> CreateOptions<TContext>()
        where TContext : DbContext
    {
        return new DbContextOptionsBuilder<TContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
    }

    private static PaginationRequest CreatePaginationRequest(
        string propertyName,
        string queryParameterName,
        SortDirection direction)
    {
        return new PaginationRequest
        {
            Limit = 10,
            SortFields =
            [
                new SortField
                {
                    PropertyName = propertyName,
                    QueryParameterName = queryParameterName,
                    Direction = direction
                }
            ]
        };
    }

    private static SelectedField CreateSelectedField(string propertyName, string queryParameterName)
    {
        return new SelectedField
        {
            PropertyName = propertyName,
            QueryParameterName = queryParameterName
        };
    }

    private sealed class PrimaryKeyPlanningContext : DbContext
    {
        internal PrimaryKeyPlanningContext(DbContextOptions<PrimaryKeyPlanningContext> options)
            : base(options)
        {
        }

        internal DbSet<PlanningEntity> Entities => Set<PlanningEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PlanningEntity>(entity =>
            {
                entity.HasKey(candidate => candidate.Id);
                entity.Property(candidate => candidate.Name).IsRequired();
            });
        }
    }

    private sealed class AlternateKeyPlanningContext : DbContext
    {
        internal AlternateKeyPlanningContext(DbContextOptions<AlternateKeyPlanningContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PlanningEntity>().HasKey(candidate => candidate.ExternalId);
        }
    }

    private sealed class PlanningEntity
    {
        public Guid Id { get; set; }

        public Guid ExternalId { get; set; }

        public string Name { get; set; } = string.Empty;

        public int Quantity { get; set; }
    }
}
