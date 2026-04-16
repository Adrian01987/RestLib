using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestLib.Abstractions;
using RestLib.EntityFrameworkCore.Tests.Fakes;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

/// <summary>
/// Tests for EF Core service registration.
/// </summary>
[Trait("Type", "Unit")]
[Trait("Feature", "Configuration")]
[Trait("Category", "Story1.2.1")]
public class EfCoreServiceRegistrationTests
{
    [Fact]
    public void AddRestLibEfCore_RegistersIRepository_AsScoped()
    {
        // Arrange
        var services = new ServiceCollection();
        AddTestDbContext(services);
        services.AddRestLibEfCore<RegistrationTestDbContext, RegistrationTestEntity, Guid>();
        var provider = services.BuildServiceProvider();

        // Act
        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        var repo1 = scope1.ServiceProvider.GetRequiredService<IRepository<RegistrationTestEntity, Guid>>();
        var repo2 = scope1.ServiceProvider.GetRequiredService<IRepository<RegistrationTestEntity, Guid>>();
        var repo3 = scope2.ServiceProvider.GetRequiredService<IRepository<RegistrationTestEntity, Guid>>();

        // Assert
        repo1.Should().BeSameAs(repo2);
        repo1.Should().NotBeSameAs(repo3);
    }

    [Fact]
    public void AddRestLibEfCore_RegistersIBatchRepository_AsScoped()
    {
        // Arrange
        var services = new ServiceCollection();
        AddTestDbContext(services);
        services.AddRestLibEfCore<RegistrationTestDbContext, RegistrationTestEntity, Guid>();
        var provider = services.BuildServiceProvider();

        // Act
        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        var repo1 = scope1.ServiceProvider.GetRequiredService<IBatchRepository<RegistrationTestEntity, Guid>>();
        var repo2 = scope1.ServiceProvider.GetRequiredService<IBatchRepository<RegistrationTestEntity, Guid>>();
        var repo3 = scope2.ServiceProvider.GetRequiredService<IBatchRepository<RegistrationTestEntity, Guid>>();

        // Assert
        repo1.Should().BeSameAs(repo2);
        repo1.Should().NotBeSameAs(repo3);
    }

    [Fact]
    public void AddRestLibEfCore_RegistersICountableRepository_AsScoped()
    {
        // Arrange
        var services = new ServiceCollection();
        AddTestDbContext(services);
        services.AddRestLibEfCore<RegistrationTestDbContext, RegistrationTestEntity, Guid>();
        var provider = services.BuildServiceProvider();

        // Act
        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        var repo1 = scope1.ServiceProvider.GetRequiredService<ICountableRepository<RegistrationTestEntity, Guid>>();
        var repo2 = scope1.ServiceProvider.GetRequiredService<ICountableRepository<RegistrationTestEntity, Guid>>();
        var repo3 = scope2.ServiceProvider.GetRequiredService<ICountableRepository<RegistrationTestEntity, Guid>>();

        // Assert
        repo1.Should().BeSameAs(repo2);
        repo1.Should().NotBeSameAs(repo3);
    }

    [Fact]
    public void AddRestLibEfCore_AllInterfaces_ResolveSameInstance_WithinScope()
    {
        // Arrange
        var services = new ServiceCollection();
        AddTestDbContext(services);
        services.AddRestLibEfCore<RegistrationTestDbContext, RegistrationTestEntity, Guid>();
        var provider = services.BuildServiceProvider();

        // Act
        using var scope = provider.CreateScope();

        var repository = scope.ServiceProvider.GetRequiredService<IRepository<RegistrationTestEntity, Guid>>();
        var batchRepository = scope.ServiceProvider.GetRequiredService<IBatchRepository<RegistrationTestEntity, Guid>>();
        var countableRepository = scope.ServiceProvider.GetRequiredService<ICountableRepository<RegistrationTestEntity, Guid>>();

        // Assert
        batchRepository.Should().BeSameAs(repository);
        countableRepository.Should().BeSameAs(repository);
    }

    [Fact]
    public void AddRestLibEfCore_ThrowsInvalidOperationException_WhenEntityNotInModel()
    {
        // Arrange
        var services = new ServiceCollection();
        AddTestDbContext(services);
        services.AddRestLibEfCore<RegistrationTestDbContext, OrphanEntity, Guid>();
        var provider = services.BuildServiceProvider();

        // Act
        using var scope = provider.CreateScope();
        var act = () => scope.ServiceProvider.GetRequiredService<IRepository<OrphanEntity, Guid>>();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*OrphanEntity*not part of the EF Core model*");
    }

    [Fact]
    public void AddRestLibEfCore_ReturnsSameServiceCollection_ForChaining()
    {
        // Arrange
        var services = new ServiceCollection();
        AddTestDbContext(services);

        // Act
        var result = services.AddRestLibEfCore<RegistrationTestDbContext, RegistrationTestEntity, Guid>();

        // Assert
        result.Should().BeSameAs(services);
    }

    [Fact]
    [Trait("Category", "Story1.2.2")]
    public void AddRestLibEfCore_WithOptions_RegistersRepository_AsScoped()
    {
        // Arrange
        var services = new ServiceCollection();
        AddTestDbContext(services);
        services.AddRestLibEfCore<RegistrationTestDbContext, RegistrationTestEntity, Guid>(options =>
        {
            options.KeySelector = entity => entity.Id;
        });
        var provider = services.BuildServiceProvider();

        // Act
        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        var repo1 = scope1.ServiceProvider.GetRequiredService<IRepository<RegistrationTestEntity, Guid>>();
        var repo2 = scope1.ServiceProvider.GetRequiredService<IRepository<RegistrationTestEntity, Guid>>();
        var repo3 = scope2.ServiceProvider.GetRequiredService<IRepository<RegistrationTestEntity, Guid>>();

        // Assert
        repo1.Should().BeSameAs(repo2);
        repo1.Should().NotBeSameAs(repo3);
    }

    [Fact]
    [Trait("Category", "Story1.2.2")]
    public void AddRestLibEfCore_WithOptions_DefaultUseAsNoTracking_IsTrue()
    {
        // Arrange
        var services = new ServiceCollection();
        AddTestDbContext(services);
        services.AddRestLibEfCore<RegistrationTestDbContext, RegistrationTestEntity, Guid>(_ => { });
        var provider = services.BuildServiceProvider();

        // Act
        var options = provider.GetRequiredService<EfCoreRepositoryOptions<RegistrationTestEntity, Guid>>();

        // Assert
        options.UseAsNoTracking.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Story1.2.2")]
    public void AddRestLibEfCore_WithOptions_CustomUseAsNoTracking_IsApplied()
    {
        // Arrange
        var services = new ServiceCollection();
        AddTestDbContext(services);
        services.AddRestLibEfCore<RegistrationTestDbContext, RegistrationTestEntity, Guid>(options =>
        {
            options.UseAsNoTracking = false;
        });
        var provider = services.BuildServiceProvider();

        // Act
        var options = provider.GetRequiredService<EfCoreRepositoryOptions<RegistrationTestEntity, Guid>>();

        // Assert
        options.UseAsNoTracking.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Story1.2.2")]
    public void AddRestLibEfCore_WithOptions_ExplicitKeySelector_IsApplied()
    {
        // Arrange
        var services = new ServiceCollection();
        AddTestDbContext(services);
        services.AddRestLibEfCore<RegistrationTestDbContext, RegistrationTestEntity, Guid>(options =>
        {
            options.KeySelector = entity => entity.Id;
        });
        var provider = services.BuildServiceProvider();

        // Act
        var options = provider.GetRequiredService<EfCoreRepositoryOptions<RegistrationTestEntity, Guid>>();
        var entity = new RegistrationTestEntity
        {
            Id = Guid.NewGuid(),
            Name = "Test"
        };

        // Assert
        options.KeySelector.Should().NotBeNull();
        options.KeySelector!.Compile().Invoke(entity).Should().Be(entity.Id);
    }

    [Fact]
    [Trait("Category", "Story1.2.2")]
    public void AddRestLibEfCore_WithOptions_ThrowsInvalidOperationException_WhenEntityNotInModel()
    {
        // Arrange
        var services = new ServiceCollection();
        AddTestDbContext(services);
        services.AddRestLibEfCore<RegistrationTestDbContext, OrphanEntity, Guid>(_ => { });
        var provider = services.BuildServiceProvider();

        // Act
        using var scope = provider.CreateScope();
        var act = () => scope.ServiceProvider.GetRequiredService<IRepository<OrphanEntity, Guid>>();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*OrphanEntity*not part of the EF Core model*");
    }

    [Fact]
    [Trait("Category", "Story1.2.2")]
    public void AddRestLibEfCore_WithOptions_ReturnsSameServiceCollection_ForChaining()
    {
        // Arrange
        var services = new ServiceCollection();
        AddTestDbContext(services);

        // Act
        var result = services.AddRestLibEfCore<RegistrationTestDbContext, RegistrationTestEntity, Guid>(_ => { });

        // Assert
        result.Should().BeSameAs(services);
    }

    [Fact]
    [Trait("Category", "Story1.2.2")]
    public void AddRestLibEfCore_ParameterlessOverload_StillWorks_AfterRefactor()
    {
        // Arrange
        var services = new ServiceCollection();
        AddTestDbContext(services);
        services.AddRestLibEfCore<RegistrationTestDbContext, RegistrationTestEntity, Guid>();
        var provider = services.BuildServiceProvider();

        // Act
        using var scope = provider.CreateScope();

        var repository = scope.ServiceProvider.GetRequiredService<IRepository<RegistrationTestEntity, Guid>>();
        var batchRepository = scope.ServiceProvider.GetRequiredService<IBatchRepository<RegistrationTestEntity, Guid>>();
        var countableRepository = scope.ServiceProvider.GetRequiredService<ICountableRepository<RegistrationTestEntity, Guid>>();

        // Assert
        batchRepository.Should().BeSameAs(repository);
        countableRepository.Should().BeSameAs(repository);
    }

    [Fact]
    [Trait("Category", "Story1.2.3")]
    public void AddRestLibEfCore_WithoutKeySelector_AutoDetectsKey()
    {
        // Arrange
        var services = new ServiceCollection();
        AddKeyDetectionDbContext(services);
        services.AddRestLibEfCore<KeyDetectionTestDbContext, RegistrationTestEntity, Guid>();
        var provider = services.BuildServiceProvider();

        // Act
        var options = provider.GetRequiredService<EfCoreRepositoryOptions<RegistrationTestEntity, Guid>>();
        using var scope = provider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<RegistrationTestEntity, Guid>>();

        // Assert
        repository.Should().NotBeNull();
        options.KeySelector.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Story1.2.3")]
    public void AddRestLibEfCore_WithoutKeySelector_CompositeKey_ThrowsWhenRepositoryIsResolved()
    {
        // Arrange
        var services = new ServiceCollection();
        AddKeyDetectionDbContext(services);
        services.AddRestLibEfCore<KeyDetectionTestDbContext, CompositeKeyEntity, Guid>();
        var provider = services.BuildServiceProvider();

        // Act
        using var scope = provider.CreateScope();
        var act = () => scope.ServiceProvider.GetRequiredService<IRepository<CompositeKeyEntity, Guid>>();

        // Assert
        act.Should().Throw<NotSupportedException>()
            .WithMessage("*CompositeKeyEntity*composite primary key*");
    }

    [Fact]
    [Trait("Category", "Story1.2.3")]
    public void AddRestLibEfCore_WithoutKeySelector_TypeMismatch_ThrowsWhenRepositoryIsResolved()
    {
        // Arrange
        var services = new ServiceCollection();
        AddKeyDetectionDbContext(services);
        services.AddRestLibEfCore<KeyDetectionTestDbContext, IntKeyEntity, Guid>();
        var provider = services.BuildServiceProvider();

        // Act
        using var scope = provider.CreateScope();
        var act = () => scope.ServiceProvider.GetRequiredService<IRepository<IntKeyEntity, Guid>>();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IntKeyEntity*Int32*Guid*");
    }

    [Fact]
    [Trait("Category", "Story1.2.3")]
    public void AddRestLibEfCore_WithExplicitKeySelector_SkipsAutoDetection()
    {
        // Arrange
        var services = new ServiceCollection();
        AddKeyDetectionDbContext(services);
        services.AddRestLibEfCore<KeyDetectionTestDbContext, IntKeyEntity, Guid>(options =>
        {
            options.KeySelector = entity => Guid.Empty;
        });
        var provider = services.BuildServiceProvider();

        // Act
        var options = provider.GetRequiredService<EfCoreRepositoryOptions<IntKeyEntity, Guid>>();

        // Assert
        options.KeySelector.Should().NotBeNull();
        options.KeySelector!.Compile().Invoke(new IntKeyEntity { Id = 42, Name = "Test" }).Should().Be(Guid.Empty);
    }

    [Fact]
    [Trait("Category", "Story1.2.3")]
    public void AddRestLibEfCore_AutoDetectedKey_ExtractsCorrectValue()
    {
        // Arrange
        var services = new ServiceCollection();
        AddKeyDetectionDbContext(services);
        services.AddRestLibEfCore<KeyDetectionTestDbContext, RegistrationTestEntity, Guid>();
        var provider = services.BuildServiceProvider();
        var entity = new RegistrationTestEntity
        {
            Id = Guid.NewGuid(),
            Name = "Known"
        };

        // Act
        using var scope = provider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<RegistrationTestEntity, Guid>>();
        var keySelector = ((EfCoreRepository<KeyDetectionTestDbContext, RegistrationTestEntity, Guid>)repository)
            .GetType()
            .GetField("_keySelector", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var key = ((System.Linq.Expressions.Expression<Func<RegistrationTestEntity, Guid>>)keySelector.GetValue(repository)!)
            .Compile()
            .Invoke(entity);

        // Assert
        key.Should().Be(entity.Id);
    }

    [Fact]
    public void AddRestLibEfCore_WhenRegisteredBeforeDbContext_StillResolvesRepository()
    {
        // Arrange
        var services = new ServiceCollection();
        AddTestDbContext(services);
        services.AddRestLibEfCore<RegistrationTestDbContext, RegistrationTestEntity, Guid>();
        var provider = services.BuildServiceProvider();

        // Act
        using var scope = provider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<RegistrationTestEntity, Guid>>();

        // Assert
        repository.Should().NotBeNull();
    }

    [Fact]
    public void AddRestLibEfCore_AllowsEntityConfiguredOnlyInOnModelCreating()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddDbContext<ModelOnlyTestDbContext>(options => options.UseSqlite("DataSource=:memory:"));
        services.AddRestLibEfCore<ModelOnlyTestDbContext, RegistrationTestEntity, Guid>();
        var provider = services.BuildServiceProvider();

        // Act
        using var scope = provider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<RegistrationTestEntity, Guid>>();

        // Assert
        repository.Should().NotBeNull();
    }

    [Fact]
    public void AddRestLibEfCore_WithOptions_AllowsEntityConfiguredOnlyInOnModelCreating()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddDbContext<ModelOnlyTestDbContext>(options => options.UseSqlite("DataSource=:memory:"));
        services.AddRestLibEfCore<ModelOnlyTestDbContext, RegistrationTestEntity, Guid>(options =>
        {
            options.UseAsNoTracking = false;
        });
        var provider = services.BuildServiceProvider();

        // Act
        using var scope = provider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<RegistrationTestEntity, Guid>>();

        // Assert
        repository.Should().NotBeNull();
    }

    [Fact]
    public void AddRestLibEfCore_ThrowsInvalidOperationException_WhenEntityNotInEfModel()
    {
        // Arrange
        var services = new ServiceCollection();
        AddTestDbContext(services);
        services.AddRestLibEfCore<RegistrationTestDbContext, OrphanEntity, Guid>();
        var provider = services.BuildServiceProvider();

        // Act
        using var scope = provider.CreateScope();
        var act = () => scope.ServiceProvider.GetRequiredService<IRepository<OrphanEntity, Guid>>();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*OrphanEntity*not part of the EF Core model*");
    }

    private static void AddTestDbContext(IServiceCollection services)
    {
        services.AddDbContext<RegistrationTestDbContext>(
            options => options.UseSqlite("DataSource=:memory:"));
    }

    private static void AddKeyDetectionDbContext(IServiceCollection services)
    {
        services.AddDbContext<KeyDetectionTestDbContext>(
            options => options.UseSqlite("DataSource=:memory:"));
    }
}
