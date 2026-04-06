using FluentAssertions;
using RestLib.Sorting;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Unit tests for <see cref="SortParser"/>.
/// </summary>
public class SortParserTests
{
    private static SortConfiguration<FilterableEntity> CreateConfiguration()
    {
        var config = new SortConfiguration<FilterableEntity>();
        config.AddProperty(p => p.Price);
        config.AddProperty(p => p.Name);
        config.AddProperty(p => p.Quantity);
        return config;
    }

    [Fact]
    [Trait("Category", "Story5.1")]
    public void Parse_SingleFieldAsc_ReturnsCorrectSortField()
    {
        // Arrange
        var config = CreateConfiguration();

        // Act
        var result = SortParser.Parse("price:asc", config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Fields.Should().HaveCount(1);
        result.Fields[0].PropertyName.Should().Be("Price");
        result.Fields[0].QueryParameterName.Should().Be("price");
        result.Fields[0].Direction.Should().Be(SortDirection.Asc);
    }

    [Fact]
    [Trait("Category", "Story5.1")]
    public void Parse_SingleFieldDesc_ReturnsCorrectSortField()
    {
        // Arrange
        var config = CreateConfiguration();

        // Act
        var result = SortParser.Parse("price:desc", config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Fields.Should().HaveCount(1);
        result.Fields[0].PropertyName.Should().Be("Price");
        result.Fields[0].Direction.Should().Be(SortDirection.Desc);
    }

    [Fact]
    [Trait("Category", "Story5.1")]
    public void Parse_SingleFieldNoDirection_DefaultsToAsc()
    {
        // Arrange
        var config = CreateConfiguration();

        // Act
        var result = SortParser.Parse("price", config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Fields.Should().HaveCount(1);
        result.Fields[0].Direction.Should().Be(SortDirection.Asc);
    }

    [Fact]
    [Trait("Category", "Story5.1")]
    public void Parse_MultipleFields_ReturnsInOrder()
    {
        // Arrange
        var config = CreateConfiguration();

        // Act
        var result = SortParser.Parse("price:asc,name:desc", config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Fields.Should().HaveCount(2);
        result.Fields[0].PropertyName.Should().Be("Price");
        result.Fields[0].Direction.Should().Be(SortDirection.Asc);
        result.Fields[1].PropertyName.Should().Be("Name");
        result.Fields[1].Direction.Should().Be(SortDirection.Desc);
    }

    [Fact]
    [Trait("Category", "Story5.1")]
    public void Parse_UnknownField_ReturnsError()
    {
        // Arrange
        var config = CreateConfiguration();

        // Act
        var result = SortParser.Parse("unknown_field:asc", config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Field.Should().Be("unknown_field");
        result.Errors[0].Message.Should().Contain("price");
        result.Errors[0].Message.Should().Contain("name");
        result.Errors[0].Message.Should().Contain("quantity");
    }

    [Fact]
    [Trait("Category", "Story5.1")]
    public void Parse_InvalidDirection_ReturnsError()
    {
        // Arrange
        var config = CreateConfiguration();

        // Act
        var result = SortParser.Parse("price:sideways", config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Field.Should().Be("price");
        result.Errors[0].Message.Should().Contain("asc").And.Contain("desc");
    }

    [Fact]
    [Trait("Category", "Story5.1")]
    public void Parse_DuplicateField_ReturnsError()
    {
        // Arrange
        var config = CreateConfiguration();

        // Act
        var result = SortParser.Parse("price:asc,price:desc", config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Field.Should().Be("price");
        result.Errors[0].Message.Should().Contain("Duplicate");
    }

    [Fact]
    [Trait("Category", "Story5.1")]
    public void Parse_EmptyString_ReturnsEmptyResult()
    {
        // Arrange
        var config = CreateConfiguration();

        // Act
        var result = SortParser.Parse("", config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Fields.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Story5.1")]
    public void Parse_NullString_ReturnsEmptyResult()
    {
        // Arrange
        var config = CreateConfiguration();

        // Act
        var result = SortParser.Parse(null, config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Fields.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Story5.1")]
    public void Parse_TrailingComma_IgnoresEmpty()
    {
        // Arrange
        var config = CreateConfiguration();

        // Act
        var result = SortParser.Parse("price:asc,", config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Fields.Should().HaveCount(1);
        result.Fields[0].PropertyName.Should().Be("Price");
    }

    [Fact]
    [Trait("Category", "Story5.1")]
    public void Parse_CaseInsensitiveDirection_Parses()
    {
        // Arrange
        var config = CreateConfiguration();

        // Act
        var result = SortParser.Parse("price:ASC", config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Fields.Should().HaveCount(1);
        result.Fields[0].Direction.Should().Be(SortDirection.Asc);
    }

    [Fact]
    [Trait("Category", "Story5.1")]
    public void Parse_CaseInsensitiveFieldName_Parses()
    {
        // Arrange
        var config = CreateConfiguration();

        // Act
        var result = SortParser.Parse("Price:asc", config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Fields.Should().HaveCount(1);
        result.Fields[0].PropertyName.Should().Be("Price");
        result.Fields[0].QueryParameterName.Should().Be("price");
    }
}

/// <summary>
/// Tests for <see cref="SortConfiguration{TEntity}"/>.
/// </summary>
public class SortConfigurationTests
{
    [Fact]
    [Trait("Category", "Story5.1")]
    public void SortConfiguration_ConvertsPropertyNamesToSnakeCase()
    {
        // Arrange
        var config = new SortConfiguration<FilterableEntity>();

        // Act
        config.AddProperty(p => p.Price);
        config.AddProperty(p => p.CreatedAt);
        config.AddProperty(p => p.IsActive);

        // Assert
        config.Properties.Should().HaveCount(3);
        config.Properties[0].QueryParameterName.Should().Be("price");
        config.Properties[1].QueryParameterName.Should().Be("created_at");
        config.Properties[2].QueryParameterName.Should().Be("is_active");
    }

    [Fact]
    [Trait("Category", "Story5.1")]
    public void SortConfiguration_StoresSortableProperties()
    {
        // Arrange
        var config = new SortConfiguration<FilterableEntity>();

        // Act
        config.AddProperty(p => p.Price);
        config.AddProperty(p => p.Name);

        // Assert
        config.Properties.Should().HaveCount(2);
        config.Properties[0].PropertyName.Should().Be("Price");
        config.Properties[0].PropertyType.Should().Be(typeof(decimal));
        config.Properties[1].PropertyName.Should().Be("Name");
        config.Properties[1].PropertyType.Should().Be(typeof(string));
    }

    [Fact]
    [Trait("Category", "Story5.1")]
    public void FindByQueryName_ExistingProperty_ReturnsConfiguration()
    {
        // Arrange
        var config = new SortConfiguration<FilterableEntity>();
        config.AddProperty(p => p.Price);

        // Act
        var result = config.FindByQueryName("price");

        // Assert
        result.Should().NotBeNull();
        result!.PropertyName.Should().Be("Price");
    }

    [Fact]
    [Trait("Category", "Story5.1")]
    public void FindByQueryName_UnknownProperty_ReturnsNull()
    {
        // Arrange
        var config = new SortConfiguration<FilterableEntity>();
        config.AddProperty(p => p.Price);

        // Act
        var result = config.FindByQueryName("unknown");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Story5.1")]
    public void SortConfiguration_DuplicateProperty_ThrowsInvalidOperationException()
    {
        // Arrange
        var config = new SortConfiguration<FilterableEntity>();
        config.AddProperty(p => p.Price);

        // Act
        var act = () => config.AddProperty(p => p.Price);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*'Price'*already configured*sorting*");
    }
}
