using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestLib.Abstractions;
using RestLib.Endpoints;
using RestLib.Mapping;
using Xunit;

namespace RestLib.Tests;

[Trait("Type", "Unit")]
[Trait("Feature", "Mapping")]
public class MapperTests
{
    [Fact]
    public void IdentityMapper_ToApi_ReturnsSameInstance()
    {
        // Arrange
        var mapper = new IdentityMapper<MapperProductDto>();
        var model = new MapperProductDto { Id = Guid.NewGuid(), Name = "Widget", Price = 10m };

        // Act
        var result = mapper.ToApi(model);

        // Assert
        result.Should().BeSameAs(model);
    }

    [Fact]
    public void IdentityMapper_ToDb_ReturnsSameInstance()
    {
        // Arrange
        var mapper = new IdentityMapper<MapperProductDto>();
        var model = new MapperProductDto { Id = Guid.NewGuid(), Name = "Widget", Price = 10m };

        // Act
        var result = mapper.ToDb(model);

        // Assert
        result.Should().BeSameAs(model);
    }

    [Fact]
    public void AddRestLibMapper_WithImplementationType_ResolvesMapperFromDi()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddRestLibMapper<MapperProductDto, MapperProductEntity, MapperProductMapper>();
        using var provider = services.BuildServiceProvider();

        // Act
        var mapper = provider.GetRequiredService<IRestLibMapper<MapperProductDto, MapperProductEntity>>();
        var implementation = provider.GetRequiredService<MapperProductMapper>();

        // Assert
        mapper.Should().BeSameAs(implementation);
    }

    [Fact]
    public void AddRestLibMapper_WithFactory_ResolvesMapperFromDi()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton("factory-tag");
        services.AddRestLibMapper<MapperProductDto, MapperProductEntity>(sp =>
            new MapperFactoryMapper(sp.GetRequiredService<string>()));
        using var provider = services.BuildServiceProvider();

        // Act
        var mapper = provider.GetRequiredService<IRestLibMapper<MapperProductDto, MapperProductEntity>>();

        // Assert
        mapper.Should().BeOfType<MapperFactoryMapper>();
        ((MapperFactoryMapper)mapper).Tag.Should().Be("factory-tag");
    }

    [Fact]
    public void SampleMapper_ToApiAndToDb_MapsBothDirections()
    {
        // Arrange
        var mapper = new MapperProductMapper();
        var entity = new MapperProductEntity
        {
            Id = Guid.NewGuid(),
            Name = "Mapped",
            Price = 25m,
            InternalCode = "SKU-1"
        };

        // Act
        var api = mapper.ToApi(entity);
        var db = mapper.ToDb(api);

        // Assert
        api.Id.Should().Be(entity.Id);
        api.Name.Should().Be(entity.Name);
        api.Price.Should().Be(entity.Price);
        db.Id.Should().Be(api.Id);
        db.Name.Should().Be(api.Name);
        db.Price.Should().Be(api.Price);
        db.InternalCode.Should().BeNull();
    }

    [Fact]
    public void Resolve_AutoMapperForSameModelPair_ReusesCompiledMapper()
    {
        // Arrange
        using var provider = new ServiceCollection().BuildServiceProvider();

        // Act
        var first = RestLibMapperResolver.Resolve<AutoMapperDto, AutoMapperEntity>(
            provider,
            useAutoMapper: true);
        var second = RestLibMapperResolver.Resolve<AutoMapperDto, AutoMapperEntity>(
            provider,
            useAutoMapper: true);

        // Assert
        second.Should().BeSameAs(first);
    }

    [Fact]
    public void Resolve_AutoMapper_UsesCompiledMappingsAndCreatesIndependentModels()
    {
        // Arrange
        using var provider = new ServiceCollection().BuildServiceProvider();
        var mapper = RestLibMapperResolver.Resolve<AutoMapperDto, AutoMapperEntity>(
            provider,
            useAutoMapper: true);
        var entity = new AutoMapperEntity
        {
            Id = Guid.NewGuid(),
            Name = "Widget",
            Price = 42m,
        };

        // Act
        var first = mapper.ToApi(entity);
        var second = mapper.ToApi(entity);
        var roundTrip = mapper.ToDb(first);

        // Assert
        first.Should().NotBeSameAs(second);
        first.Id.Should().Be(entity.Id);
        first.Name.Should().Be(entity.Name);
        first.Price.Should().Be(entity.Price);
        roundTrip.Id.Should().Be(entity.Id);
        roundTrip.Name.Should().Be(entity.Name);
        roundTrip.Price.Should().Be(entity.Price);
    }

    [Fact]
    public void Resolve_AutoMapperWithIncompatibleModels_PreservesResourceDiagnostic()
    {
        // Arrange
        using var provider = new ServiceCollection().BuildServiceProvider();

        // Act
        var act = () => RestLibMapperResolver.Resolve<InvalidAutoMapperDto, InvalidAutoMapperEntity>(
            provider,
            useAutoMapper: true,
            resourceName: "products");

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*RestLib resource 'products'*")
            .WithMessage("*requires destination property 'Name'*");
    }

    [Fact]
    public void AutoMapper_DestinationConstructorThrows_PreservesCreationDiagnostic()
    {
        // Arrange
        using var provider = new ServiceCollection().BuildServiceProvider();
        var mapper = RestLibMapperResolver.Resolve<ThrowingAutoMapperDto, ThrowingAutoMapperEntity>(
            provider,
            useAutoMapper: true);
        var entity = new ThrowingAutoMapperEntity { Name = "Widget" };

        // Act
        var act = () => mapper.ToApi(entity);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*could not create destination type*")
            .WithInnerException<InvalidOperationException>();
    }

    [Fact]
    public void Resolve_RegisteredScopedMapper_PreservesConfiguredLifetime()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddRestLibMapper<AutoMapperDto, AutoMapperEntity, ScopedAutoMapper>(
            ServiceLifetime.Scoped);
        using var provider = services.BuildServiceProvider();
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        // Act
        var first = RestLibMapperResolver.Resolve<AutoMapperDto, AutoMapperEntity>(
            firstScope.ServiceProvider);
        var repeated = RestLibMapperResolver.Resolve<AutoMapperDto, AutoMapperEntity>(
            firstScope.ServiceProvider);
        var second = RestLibMapperResolver.Resolve<AutoMapperDto, AutoMapperEntity>(
            secondScope.ServiceProvider);

        // Assert
        repeated.Should().BeSameAs(first);
        second.Should().NotBeSameAs(first);
    }

    [Fact]
    public void IdentityModelAdapter_RepeatedResolution_ReusesStatelessBoundary()
    {
        // Arrange

        // Act
        var first = EndpointModelAdapter<AutoMapperDto, AutoMapperDto>.Identity<AutoMapperDto>();
        var second = EndpointModelAdapter<AutoMapperDto, AutoMapperDto>.Identity<AutoMapperDto>();

        // Assert
        second.Should().BeSameAs(first);
        first.IsIdentity.Should().BeTrue();
    }

    private sealed class MapperProductDto
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public decimal Price { get; set; }
    }

    private sealed class MapperProductEntity
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public decimal Price { get; set; }

        public string? InternalCode { get; set; }
    }

    private sealed class MapperProductMapper : IRestLibMapper<MapperProductDto, MapperProductEntity>
    {
        public MapperProductDto ToApi(MapperProductEntity dbModel)
        {
            return new MapperProductDto
            {
                Id = dbModel.Id,
                Name = dbModel.Name,
                Price = dbModel.Price,
            };
        }

        public MapperProductEntity ToDb(MapperProductDto apiModel)
        {
            return new MapperProductEntity
            {
                Id = apiModel.Id,
                Name = apiModel.Name,
                Price = apiModel.Price,
            };
        }
    }

    private sealed class MapperFactoryMapper : IRestLibMapper<MapperProductDto, MapperProductEntity>
    {
        public MapperFactoryMapper(string tag)
        {
            Tag = tag;
        }

        public string Tag { get; }

        public MapperProductDto ToApi(MapperProductEntity dbModel) => new()
        {
            Id = dbModel.Id,
            Name = dbModel.Name,
            Price = dbModel.Price,
        };

        public MapperProductEntity ToDb(MapperProductDto apiModel) => new()
        {
            Id = apiModel.Id,
            Name = apiModel.Name,
            Price = apiModel.Price,
        };
    }

    private sealed class AutoMapperDto
    {
        public AutoMapperDto()
        {
        }

        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public decimal Price { get; set; }
    }

    private sealed class AutoMapperEntity
    {
        public AutoMapperEntity()
        {
        }

        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public decimal Price { get; set; }
    }

    private sealed class ScopedAutoMapper : IRestLibMapper<AutoMapperDto, AutoMapperEntity>
    {
        public AutoMapperDto ToApi(AutoMapperEntity dbModel) => new()
        {
            Id = dbModel.Id,
            Name = dbModel.Name,
            Price = dbModel.Price,
        };

        public AutoMapperEntity ToDb(AutoMapperDto apiModel) => new()
        {
            Id = apiModel.Id,
            Name = apiModel.Name,
            Price = apiModel.Price,
        };
    }

    private sealed class InvalidAutoMapperDto
    {
        public InvalidAutoMapperDto()
        {
        }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class InvalidAutoMapperEntity
    {
        public InvalidAutoMapperEntity()
        {
        }

        public Guid Id { get; set; }
    }

    private sealed class ThrowingAutoMapperDto
    {
        public ThrowingAutoMapperDto()
        {
            throw new InvalidOperationException("Constructor failure.");
        }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class ThrowingAutoMapperEntity
    {
        public ThrowingAutoMapperEntity()
        {
        }

        public string Name { get; set; } = string.Empty;
    }
}
