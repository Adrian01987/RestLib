using System.Linq.Expressions;
using FluentAssertions;
using RestLib.Internal;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Unit tests for <see cref="NamingUtils"/> internal utility methods.
/// </summary>
public class NamingUtilsTests
{
    #region ConvertToSnakeCase

    [Theory]
    [Trait("Category", "Story6.2")]
    [InlineData("Id", "id")]
    [InlineData("Name", "name")]
    [InlineData("Price", "price")]
    public void ConvertToSnakeCase_SingleWord_ReturnsLowercase(string input, string expected)
    {
        // Act & Assert
        NamingUtils.ConvertToSnakeCase(input).Should().Be(expected);
    }

    [Theory]
    [Trait("Category", "Story6.2")]
    [InlineData("ProductName", "product_name")]
    [InlineData("UnitPrice", "unit_price")]
    [InlineData("StockQuantity", "stock_quantity")]
    [InlineData("CreatedAt", "created_at")]
    [InlineData("LastModifiedAt", "last_modified_at")]
    [InlineData("OptionalDescription", "optional_description")]
    [InlineData("IsActive", "is_active")]
    public void ConvertToSnakeCase_MultiWord_InsertsUnderscores(string input, string expected)
    {
        // Act & Assert
        NamingUtils.ConvertToSnakeCase(input).Should().Be(expected);
    }

    [Theory]
    [Trait("Category", "Story6.2")]
    [InlineData("CategoryId", "category_id")]
    [InlineData("CustomerEmail", "customer_email")]
    public void ConvertToSnakeCase_TwoWordWithId_ConvertsCorrectly(string input, string expected)
    {
        // Act & Assert
        NamingUtils.ConvertToSnakeCase(input).Should().Be(expected);
    }

    [Theory]
    [Trait("Category", "Story6.2")]
    [InlineData("HTTPSUrl", "https_url")]
    [InlineData("XMLParser", "xml_parser")]
    [InlineData("IOStream", "io_stream")]
    public void ConvertToSnakeCase_ConsecutiveUppercase_HandlesAcronyms(string input, string expected)
    {
        // Act & Assert
        NamingUtils.ConvertToSnakeCase(input).Should().Be(expected);
    }

    [Fact]
    [Trait("Category", "Story6.2")]
    public void ConvertToSnakeCase_AlreadyLowercase_ReturnsAsIs()
    {
        // Act & Assert
        NamingUtils.ConvertToSnakeCase("name").Should().Be("name");
    }

    [Fact]
    [Trait("Category", "Story6.2")]
    public void ConvertToSnakeCase_SingleCharacter_ReturnsLowercase()
    {
        // Act & Assert
        NamingUtils.ConvertToSnakeCase("X").Should().Be("x");
    }

    [Fact]
    [Trait("Category", "Story6.2")]
    public void ConvertToSnakeCase_EmptyString_ReturnsEmpty()
    {
        // Act & Assert
        NamingUtils.ConvertToSnakeCase(string.Empty).Should().BeEmpty();
    }

    #endregion

    #region GetMemberExpression

    [Fact]
    [Trait("Category", "Story6.2")]
    public void GetMemberExpression_DirectMemberAccess_ReturnsMemberExpression()
    {
        // Arrange — reference-type property, no Convert wrapper
        Expression<Func<TestEntity, string>> expr = e => e.Name;

        // Act
        var member = NamingUtils.GetMemberExpression(expr.Body, "test");

        // Assert
        member.Should().NotBeNull();
        member.Member.Name.Should().Be("Name");
    }

    [Fact]
    [Trait("Category", "Story6.2")]
    public void GetMemberExpression_UnaryConvertWrapper_UnwrapsAndReturnsMemberExpression()
    {
        // Arrange — value-type property in Func<T, object?> causes a Convert wrapper
        Expression<Func<TestEntity, object?>> expr = e => e.Price;

        // Act
        var member = NamingUtils.GetMemberExpression(expr.Body, "test");

        // Assert
        member.Should().NotBeNull();
        member.Member.Name.Should().Be("Price");
    }

    [Fact]
    [Trait("Category", "Story6.2")]
    public void GetMemberExpression_GuidProperty_UnwrapsConvert()
    {
        // Arrange — Guid is a value type, also wrapped in Convert
        Expression<Func<TestEntity, object?>> expr = e => e.Id;

        // Act
        var member = NamingUtils.GetMemberExpression(expr.Body, "test");

        // Assert
        member.Member.Name.Should().Be("Id");
    }

    [Fact]
    [Trait("Category", "Story6.2")]
    public void GetMemberExpression_NonMemberExpression_ThrowsArgumentException()
    {
        // Arrange — a constant expression, not a property access
        Expression<Func<TestEntity, int>> expr = _ => 42;

        // Act
        var act = () => NamingUtils.GetMemberExpression(expr.Body, "testParam");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("testParam")
            .WithMessage("*property access expression*");
    }

    [Fact]
    [Trait("Category", "Story6.2")]
    public void GetMemberExpression_MethodCallExpression_ThrowsArgumentException()
    {
        // Arrange — calling a method, not accessing a property
        Expression<Func<TestEntity, string>> expr = e => e.Name.ToUpper();

        // Act
        var act = () => NamingUtils.GetMemberExpression(expr.Body, "param");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("param")
            .WithMessage("*property access expression*");
    }

    #endregion

    #region ResolveProperty

    [Fact]
    [Trait("Category", "Story6.2")]
    public void ResolveProperty_ExistingProperty_ReturnsPropertyInfo()
    {
        // Act
        var prop = NamingUtils.ResolveProperty<TestEntity>("Name", "test");

        // Assert
        prop.Should().NotBeNull();
        prop.Name.Should().Be("Name");
        prop.PropertyType.Should().Be(typeof(string));
    }

    [Fact]
    [Trait("Category", "Story6.2")]
    public void ResolveProperty_GuidProperty_ReturnsPropertyInfo()
    {
        // Act
        var prop = NamingUtils.ResolveProperty<TestEntity>("Id", "test");

        // Assert
        prop.Name.Should().Be("Id");
        prop.PropertyType.Should().Be(typeof(Guid));
    }

    [Fact]
    [Trait("Category", "Story6.2")]
    public void ResolveProperty_DecimalProperty_ReturnsPropertyInfo()
    {
        // Act
        var prop = NamingUtils.ResolveProperty<TestEntity>("Price", "test");

        // Assert
        prop.Name.Should().Be("Price");
        prop.PropertyType.Should().Be(typeof(decimal));
    }

    [Fact]
    [Trait("Category", "Story6.2")]
    public void ResolveProperty_NonExistentProperty_ThrowsArgumentException()
    {
        // Act
        var act = () => NamingUtils.ResolveProperty<TestEntity>("NonExistent", "propParam");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("propParam")
            .WithMessage("*'NonExistent'*not found*'TestEntity'*");
    }

    [Fact]
    [Trait("Category", "Story6.2")]
    public void ResolveProperty_CaseSensitive_WrongCase_ThrowsArgumentException()
    {
        // Act — property lookup is case-sensitive by default
        var act = () => NamingUtils.ResolveProperty<TestEntity>("name", "param");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*'name'*not found*'TestEntity'*");
    }

    [Fact]
    [Trait("Category", "Story6.2")]
    public void ResolveProperty_NullableProperty_ReturnsPropertyInfo()
    {
        // Act
        var prop = NamingUtils.ResolveProperty<ProductEntity>("LastModifiedAt", "test");

        // Assert
        prop.Name.Should().Be("LastModifiedAt");
        prop.PropertyType.Should().Be(typeof(DateTime?));
    }

    [Fact]
    [Trait("Category", "Story6.2")]
    public void ResolveProperty_BoolProperty_ReturnsPropertyInfo()
    {
        // Act
        var prop = NamingUtils.ResolveProperty<ProductEntity>("IsActive", "test");

        // Assert
        prop.Name.Should().Be("IsActive");
        prop.PropertyType.Should().Be(typeof(bool));
    }

    #endregion
}
