using System.Linq.Expressions;
using FluentAssertions;
using RestLib.EntityFrameworkCore.Tests.Fakes;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

/// <summary>
/// Tests for <see cref="ExpressionBuilder.BuildPropertyAccess{TEntity}(string)"/>.
/// </summary>
[Trait("Category", "Story4.1.1")]
[Trait("Type", "Unit")]
public class ExpressionBuilderTests
{
    [Fact]
    public void BuildPropertyAccess_StringProperty_ReturnsExpression()
    {
        // Arrange

        // Act
        var expression = ExpressionBuilder.BuildPropertyAccess<ProductEntity>("ProductName");

        // Assert
        expression.Should().NotBeNull();
        expression.Body.Should().BeAssignableTo<MemberExpression>();
        var memberExpression = (MemberExpression)expression.Body;
        memberExpression.Member.Name.Should().Be("ProductName");

        var entity = new ProductEntity { ProductName = "Widget" };
        var compiled = expression.Compile();
        compiled(entity).Should().Be("Widget");
    }

    [Fact]
    public void BuildPropertyAccess_IntProperty_ReturnsExpressionWithConvert()
    {
        // Arrange

        // Act
        var expression = ExpressionBuilder.BuildPropertyAccess<ProductEntity>("StockQuantity");

        // Assert
        expression.Should().NotBeNull();
        expression.Body.Should().BeOfType<UnaryExpression>();
        var unary = (UnaryExpression)expression.Body;
        unary.NodeType.Should().Be(ExpressionType.Convert);
        unary.Type.Should().Be(typeof(object));
        var memberExpression = (MemberExpression)unary.Operand;
        memberExpression.Member.Name.Should().Be("StockQuantity");

        var entity = new ProductEntity { StockQuantity = 42 };
        var compiled = expression.Compile();
        compiled(entity).Should().Be(42);
    }

    [Fact]
    public void BuildPropertyAccess_BoolProperty_ReturnsExpressionWithConvert()
    {
        // Arrange

        // Act
        var expression = ExpressionBuilder.BuildPropertyAccess<ProductEntity>("IsActive");

        // Assert
        expression.Body.Should().BeOfType<UnaryExpression>();
        var unary = (UnaryExpression)expression.Body;
        unary.NodeType.Should().Be(ExpressionType.Convert);

        var entity = new ProductEntity { IsActive = true };
        var compiled = expression.Compile();
        compiled(entity).Should().Be(true);
    }

    [Fact]
    public void BuildPropertyAccess_GuidProperty_ReturnsExpressionWithConvert()
    {
        // Arrange

        // Act
        var expression = ExpressionBuilder.BuildPropertyAccess<ProductEntity>("Id");

        // Assert
        expression.Body.Should().BeOfType<UnaryExpression>();
        var unary = (UnaryExpression)expression.Body;
        unary.NodeType.Should().Be(ExpressionType.Convert);

        var id = Guid.NewGuid();
        var entity = new ProductEntity { Id = id };
        var compiled = expression.Compile();
        compiled(entity).Should().Be(id);
    }

    [Fact]
    public void BuildPropertyAccess_DecimalProperty_ReturnsExpressionWithConvert()
    {
        // Arrange

        // Act
        var expression = ExpressionBuilder.BuildPropertyAccess<ProductEntity>("UnitPrice");

        // Assert
        expression.Body.Should().BeOfType<UnaryExpression>();
        var unary = (UnaryExpression)expression.Body;
        unary.NodeType.Should().Be(ExpressionType.Convert);

        var entity = new ProductEntity { UnitPrice = 19.99m };
        var compiled = expression.Compile();
        compiled(entity).Should().Be(19.99m);
    }

    [Fact]
    public void BuildPropertyAccess_DateTimeProperty_ReturnsExpressionWithConvert()
    {
        // Arrange

        // Act
        var expression = ExpressionBuilder.BuildPropertyAccess<ProductEntity>("CreatedAt");

        // Assert
        expression.Body.Should().BeOfType<UnaryExpression>();
        var unary = (UnaryExpression)expression.Body;
        unary.NodeType.Should().Be(ExpressionType.Convert);

        var date = new DateTime(2025, 6, 15, 10, 30, 0, DateTimeKind.Utc);
        var entity = new ProductEntity { CreatedAt = date };
        var compiled = expression.Compile();
        compiled(entity).Should().Be(date);
    }

    [Fact]
    public void BuildPropertyAccess_NullableGuidProperty_ReturnsExpressionWithConvert()
    {
        // Arrange

        // Act
        var expression = ExpressionBuilder.BuildPropertyAccess<ProductEntity>("CategoryId");

        // Assert
        expression.Body.Should().BeOfType<UnaryExpression>();
        var unary = (UnaryExpression)expression.Body;
        unary.NodeType.Should().Be(ExpressionType.Convert);

        var categoryId = Guid.NewGuid();
        var entity = new ProductEntity { CategoryId = categoryId };
        var compiled = expression.Compile();
        compiled(entity).Should().Be(categoryId);

        var nullEntity = new ProductEntity { CategoryId = null };
        compiled(nullEntity).Should().BeNull();
    }

    [Fact]
    public void BuildPropertyAccess_NullableStringProperty_ReturnsExpressionWithoutConvert()
    {
        // Arrange

        // Act
        var expression = ExpressionBuilder.BuildPropertyAccess<ProductEntity>("OptionalDescription");

        // Assert
        expression.Body.Should().BeAssignableTo<MemberExpression>();

        var entity = new ProductEntity { OptionalDescription = "A nice widget" };
        var compiled = expression.Compile();
        compiled(entity).Should().Be("A nice widget");

        var nullEntity = new ProductEntity { OptionalDescription = null };
        compiled(nullEntity).Should().BeNull();
    }

    [Fact]
    public void BuildPropertyAccess_NonExistentProperty_ThrowsInvalidOperationException()
    {
        // Arrange
        var act = () => ExpressionBuilder.BuildPropertyAccess<ProductEntity>("NonExistentProperty");

        // Act / Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*NonExistentProperty*")
            .WithMessage("*ProductEntity*");
    }

    [Fact]
    public void BuildPropertyAccess_CaseInsensitive_ResolvesProperty()
    {
        // Arrange

        // Act
        var expression = ExpressionBuilder.BuildPropertyAccess<ProductEntity>("productname");

        // Assert
        expression.Should().NotBeNull();
        var member = expression.Body as MemberExpression
            ?? (expression.Body as UnaryExpression)?.Operand as MemberExpression;
        member.Should().NotBeNull();
        member!.Member.Name.Should().Be("ProductName");

        var entity = new ProductEntity { ProductName = "Widget" };
        var compiled = expression.Compile();
        compiled(entity).Should().Be("Widget");
    }

    [Fact]
    public void BuildPropertyAccess_CachesExpression_ReturnsSameInstance()
    {
        // Arrange

        // Act
        var first = ExpressionBuilder.BuildPropertyAccess<ProductEntity>("ProductName");
        var second = ExpressionBuilder.BuildPropertyAccess<ProductEntity>("ProductName");

        // Assert
        ReferenceEquals(first, second).Should().BeTrue(
            "repeated calls should return the cached expression instance");
    }

    [Fact]
    public void BuildPropertyAccess_CaseVariants_ReturnSameCachedInstance()
    {
        // Arrange

        // Act
        var lower = ExpressionBuilder.BuildPropertyAccess<ProductEntity>("productname");
        var pascal = ExpressionBuilder.BuildPropertyAccess<ProductEntity>("ProductName");

        // Assert
        ReferenceEquals(lower, pascal).Should().BeTrue(
            "case variants should resolve to the same cached instance");
    }

    [Fact]
    public void BuildPropertyAccess_DifferentEntities_ReturnsDifferentExpressions()
    {
        // Arrange

        // Act
        var productId = ExpressionBuilder.BuildPropertyAccess<ProductEntity>("Id");
        var categoryId = ExpressionBuilder.BuildPropertyAccess<CategoryEntity>("Id");

        // Assert
        ReferenceEquals(productId, categoryId).Should().BeFalse(
            "different entity types should produce separate cache entries");

        var product = new ProductEntity { Id = Guid.NewGuid() };
        productId.Compile()(product).Should().Be(product.Id);

        var category = new CategoryEntity { Id = Guid.NewGuid() };
        categoryId.Compile()(category).Should().Be(category.Id);
    }
}
