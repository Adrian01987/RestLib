using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RestLib.Abstractions;
using RestLib.Configuration;
using RestLib.Hooks;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Tests for Story 1.3: Service Registration Extensions.
/// </summary>
public class ServiceRegistrationTests
{
    #region AddRestLib Tests

    [Fact]
    public void AddRestLib_WithoutConfiguration_RegistersDefaultOptions()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddRestLib();
        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetService<RestLibOptions>();
        options.Should().NotBeNull();
        options!.DefaultPageSize.Should().Be(20);
        options.MaxPageSize.Should().Be(100);
        options.RequireAuthorizationByDefault.Should().BeTrue();
        options.OmitNullValues.Should().BeTrue();
        options.UseProblemDetails.Should().BeTrue();
    }

    [Fact]
    public void AddRestLib_WithConfiguration_AppliesCustomOptions()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddRestLib(options =>
        {
            options.DefaultPageSize = 50;
            options.MaxPageSize = 200;
            options.RequireAuthorizationByDefault = false;
        });
        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetService<RestLibOptions>();
        options.Should().NotBeNull();
        options!.DefaultPageSize.Should().Be(50);
        options.MaxPageSize.Should().Be(200);
        options.RequireAuthorizationByDefault.Should().BeFalse();
    }

    [Fact]
    public void AddRestLib_CalledTwice_KeepsFirstRegistration()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddRestLib(options => options.DefaultPageSize = 10);
        services.AddRestLib(options => options.DefaultPageSize = 99);
        var provider = services.BuildServiceProvider();

        // Assert - TryAddSingleton means first registration wins
        var options = provider.GetService<RestLibOptions>();
        options.Should().NotBeNull();
        options!.DefaultPageSize.Should().Be(10);
    }

    [Fact]
    public void AddRestLib_ReturnsServiceCollection_ForChaining()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddRestLib();

        // Assert
        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddRestLib_WithNullServices_ThrowsArgumentNullException()
    {
        // Arrange
        IServiceCollection services = null!;

        // Act
        var act = () => services.AddRestLib();

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("services");
    }

    #endregion

    #region JSON Resource Registration Tests

    [Fact]
    public void AddJsonResource_ReturnsServiceCollection_ForChaining()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddRestLib()
            .AddJsonResource<FakeEntity, Guid>(new RestLibJsonResourceConfiguration
            {
                Name = "fake-entities",
                Route = "/api/fake-entities"
            });

        // Assert
        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddJsonResource_FromConfigurationSection_ReturnsServiceCollection_ForChaining()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RestLib:Resources:0:Name"] = "fake-entities",
                ["RestLib:Resources:0:Route"] = "/api/fake-entities"
            })
            .Build();

        // Act
        var result = services.AddRestLib()
            .AddJsonResource<FakeEntity, Guid>(configuration.GetSection("RestLib:Resources:0"));

        // Assert
        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddNamedHook_RegistersResolver()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddRestLib();
        services.AddNamedHook<FakeEntity, Guid>(HookNames.TagEntity, _ => Task.CompletedTask);

        var provider = services.BuildServiceProvider();
        var resolver = provider.GetService<IRestLibNamedHookResolver<FakeEntity, Guid>>();

        // Assert
        resolver.Should().NotBeNull();
        resolver!.Resolve(HookNames.TagEntity).Should().NotBeNull();
    }

    [Fact]
    public void AddNamedErrorHook_RegistersResolver()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddRestLib();
        services.AddNamedErrorHook<FakeEntity, Guid>(HookNames.HandleError, _ => Task.CompletedTask);

        var provider = services.BuildServiceProvider();
        var resolver = provider.GetService<IRestLibNamedHookResolver<FakeEntity, Guid>>();

        // Assert
        resolver.Should().NotBeNull();
        resolver!.ResolveError(HookNames.HandleError).Should().NotBeNull();
    }

    #endregion

    #region AddRepository<TEntity, TKey, TRepository> Tests

    [Fact]
    public void AddRepository_WithType_RegistersRepositoryAsScoped()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddRepository<FakeEntity, Guid, FakeRepository>();
        var provider = services.BuildServiceProvider();

        // Assert
        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        var repo1 = scope1.ServiceProvider.GetService<IRepository<FakeEntity, Guid>>();
        var repo2 = scope1.ServiceProvider.GetService<IRepository<FakeEntity, Guid>>();
        var repo3 = scope2.ServiceProvider.GetService<IRepository<FakeEntity, Guid>>();

        repo1.Should().NotBeNull();
        repo1.Should().BeOfType<FakeRepository>();
        repo1.Should().BeSameAs(repo2); // Same scope
        repo1.Should().NotBeSameAs(repo3); // Different scope
    }

    [Fact]
    public void AddRepository_WithSingletonLifetime_RegistersAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddRepository<FakeEntity, Guid, FakeRepository>(ServiceLifetime.Singleton);
        var provider = services.BuildServiceProvider();

        // Assert
        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        var repo1 = scope1.ServiceProvider.GetService<IRepository<FakeEntity, Guid>>();
        var repo2 = scope2.ServiceProvider.GetService<IRepository<FakeEntity, Guid>>();

        repo1.Should().BeSameAs(repo2); // Singleton across scopes
    }

    [Fact]
    public void AddRepository_WithTransientLifetime_CreatesNewInstances()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddRepository<FakeEntity, Guid, FakeRepository>(ServiceLifetime.Transient);
        var provider = services.BuildServiceProvider();

        // Assert
        var repo1 = provider.GetService<IRepository<FakeEntity, Guid>>();
        var repo2 = provider.GetService<IRepository<FakeEntity, Guid>>();

        repo1.Should().NotBeNull();
        repo1.Should().NotBeSameAs(repo2); // Transient creates new each time
    }

    [Fact]
    public void AddRepository_ReturnsServiceCollection_ForChaining()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddRepository<FakeEntity, Guid, FakeRepository>();

        // Assert
        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddRepository_WithNullServices_ThrowsArgumentNullException()
    {
        // Arrange
        IServiceCollection services = null!;

        // Act
        var act = () => services.AddRepository<FakeEntity, Guid, FakeRepository>();

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("services");
    }

    #endregion

    #region AddRepository<TEntity, TKey> Factory Overload Tests

    [Fact]
    public void AddRepository_WithFactory_RegistersUsingFactory()
    {
        // Arrange
        var services = new ServiceCollection();
        var specificInstance = new FakeRepository();

        // Act
        services.AddRepository<FakeEntity, Guid>(_ => specificInstance);
        var provider = services.BuildServiceProvider();

        // Assert
        var repo = provider.GetService<IRepository<FakeEntity, Guid>>();
        repo.Should().BeSameAs(specificInstance);
    }

    [Fact]
    public void AddRepository_WithFactory_CanAccessServiceProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton("test-value");

        // Act
        services.AddRepository<FakeEntity, Guid>(sp =>
        {
            var value = sp.GetRequiredService<string>();
            return new FakeRepository { Tag = value };
        });
        var provider = services.BuildServiceProvider();

        // Assert
        var repo = provider.GetService<IRepository<FakeEntity, Guid>>() as FakeRepository;
        repo.Should().NotBeNull();
        repo!.Tag.Should().Be("test-value");
    }

    [Fact]
    public void AddRepository_WithFactory_ReturnsServiceCollection_ForChaining()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddRepository<FakeEntity, Guid>(_ => new FakeRepository());

        // Assert
        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddRepository_WithNullFactory_ThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var act = () => services.AddRepository<FakeEntity, Guid>(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("implementationFactory");
    }

    #endregion

    #region Options Validation Tests

    [Fact]
    [Trait("Category", "Story1.3")]
    public void AddRestLib_NegativeDefaultPageSize_ThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var act = () => services.AddRestLib(o => o.DefaultPageSize = -1);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DefaultPageSize*greater than 0*-1*");
    }

    [Fact]
    [Trait("Category", "Story1.3")]
    public void AddRestLib_ZeroDefaultPageSize_ThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var act = () => services.AddRestLib(o => o.DefaultPageSize = 0);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DefaultPageSize*greater than 0*0*");
    }

    [Fact]
    [Trait("Category", "Story1.3")]
    public void AddRestLib_NegativeMaxPageSize_ThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var act = () => services.AddRestLib(o => o.MaxPageSize = -1);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*MaxPageSize*greater than 0*-1*");
    }

    [Fact]
    [Trait("Category", "Story1.3")]
    public void AddRestLib_ZeroMaxPageSize_ThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var act = () => services.AddRestLib(o => o.MaxPageSize = 0);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*MaxPageSize*greater than 0*0*");
    }

    [Fact]
    [Trait("Category", "Story1.3")]
    public void AddRestLib_DefaultPageSizeExceedsMaxPageSize_ThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var act = () => services.AddRestLib(o =>
        {
            o.DefaultPageSize = 50;
            o.MaxPageSize = 10;
        });

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DefaultPageSize*50*must not exceed*MaxPageSize*10*");
    }

    [Fact]
    [Trait("Category", "Story1.3")]
    public void AddRestLib_NegativeMaxBatchSize_ThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var act = () => services.AddRestLib(o => o.MaxBatchSize = -1);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*MaxBatchSize*0 or greater*-1*");
    }

    [Fact]
    [Trait("Category", "Story1.3")]
    public void AddRestLib_ZeroMaxBatchSize_DoesNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var act = () => services.AddRestLib(o => o.MaxBatchSize = 0);

        // Assert — 0 is valid (disables the limit)
        act.Should().NotThrow();
    }

    [Fact]
    [Trait("Category", "Story1.3")]
    public void AddRestLib_NegativeMaxFilterInListSize_ThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var act = () => services.AddRestLib(o => o.MaxFilterInListSize = -1);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*MaxFilterInListSize*greater than 0*-1*");
    }

    [Fact]
    [Trait("Category", "Story1.3")]
    public void AddRestLib_ZeroMaxFilterInListSize_ThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var act = () => services.AddRestLib(o => o.MaxFilterInListSize = 0);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*MaxFilterInListSize*greater than 0*0*");
    }

    [Fact]
    [Trait("Category", "Story1.3")]
    public void AddRestLib_ZeroMaxCursorLength_ThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var act = () => services.AddRestLib(o => o.MaxCursorLength = 0);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*MaxCursorLength*greater than 0*0*");
    }

    [Fact]
    [Trait("Category", "Story1.3")]
    public void AddRestLib_NegativeMaxCursorLength_ThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var act = () => services.AddRestLib(o => o.MaxCursorLength = -1);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*MaxCursorLength*greater than 0*-1*");
    }

    [Fact]
    [Trait("Category", "Story1.3")]
    public void AddRestLib_ValidCustomOptions_DoesNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var act = () => services.AddRestLib(o =>
        {
            o.DefaultPageSize = 10;
            o.MaxPageSize = 50;
            o.MaxBatchSize = 200;
        });

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    [Trait("Category", "Story1.3")]
    public void AddRestLib_DefaultPageSizeEqualsMaxPageSize_DoesNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var act = () => services.AddRestLib(o =>
        {
            o.DefaultPageSize = 50;
            o.MaxPageSize = 50;
        });

        // Assert — equal is valid (boundary)
        act.Should().NotThrow();
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void AddRestLib_AndAddRepository_WorkTogether()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services
            .AddRestLib(options => options.DefaultPageSize = 25)
            .AddRepository<FakeEntity, Guid, FakeRepository>();

        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetService<RestLibOptions>();
        var repo = provider.GetService<IRepository<FakeEntity, Guid>>();

        options.Should().NotBeNull();
        options!.DefaultPageSize.Should().Be(25);

        repo.Should().NotBeNull();
        repo.Should().BeOfType<FakeRepository>();
    }

    [Fact]
    public void AddRepository_ForMultipleEntityTypes_RegistersAll()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services
            .AddRepository<FakeEntity, Guid, FakeRepository>()
            .AddRepository<AnotherEntity, int, AnotherRepository>();

        var provider = services.BuildServiceProvider();

        // Assert
        var repo1 = provider.GetService<IRepository<FakeEntity, Guid>>();
        var repo2 = provider.GetService<IRepository<AnotherEntity, int>>();

        repo1.Should().NotBeNull().And.BeOfType<FakeRepository>();
        repo2.Should().NotBeNull().And.BeOfType<AnotherRepository>();
    }

    #endregion
}

// Additional test types
public class AnotherEntity
{
    public int Id { get; set; }
    public string? Value { get; set; }
}

public class AnotherRepository : FakeRepositoryBase<AnotherEntity, int>
{
    protected override int GetId(AnotherEntity entity) => entity.Id;
}
