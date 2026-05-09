using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestLib.Abstractions;
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
}
