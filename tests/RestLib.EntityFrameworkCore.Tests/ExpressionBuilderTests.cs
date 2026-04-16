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
        expression.ReturnType.Should().Be(typeof(string));
        expression.Body.NodeType.Should().Be(ExpressionType.MemberAccess);
        var memberExpression = (MemberExpression)expression.Body;
        memberExpression.Member.Name.Should().Be("ProductName");

        var entity = new ProductEntity { ProductName = "Widget" };
        var compiled = expression.Compile();
        compiled.DynamicInvoke(entity).Should().Be("Widget");
    }

    [Fact]
    public void BuildPropertyAccess_IntProperty_ReturnsTypedExpression()
    {
        // Arrange

        // Act
        var expression = ExpressionBuilder.BuildPropertyAccess<ProductEntity>("StockQuantity");

        // Assert
        expression.Should().NotBeNull();
        expression.ReturnType.Should().Be(typeof(int));
        expression.Body.NodeType.Should().Be(ExpressionType.MemberAccess);
        var memberExpression = (MemberExpression)expression.Body;
        memberExpression.Member.Name.Should().Be("StockQuantity");

        var entity = new ProductEntity { StockQuantity = 42 };
        var compiled = expression.Compile();
        compiled.DynamicInvoke(entity).Should().Be(42);
    }

    [Fact]
    public void BuildPropertyAccess_BoolProperty_ReturnsTypedExpression()
    {
        // Arrange

        // Act
        var expression = ExpressionBuilder.BuildPropertyAccess<ProductEntity>("IsActive");

        // Assert
        expression.ReturnType.Should().Be(typeof(bool));
        expression.Body.NodeType.Should().Be(ExpressionType.MemberAccess);

        var entity = new ProductEntity { IsActive = true };
        var compiled = expression.Compile();
        compiled.DynamicInvoke(entity).Should().Be(true);
    }

    [Fact]
    public void BuildPropertyAccess_GuidProperty_ReturnsTypedExpression()
    {
        // Arrange

        // Act
        var expression = ExpressionBuilder.BuildPropertyAccess<ProductEntity>("Id");

        // Assert
        expression.ReturnType.Should().Be(typeof(Guid));
        expression.Body.NodeType.Should().Be(ExpressionType.MemberAccess);

        var id = Guid.NewGuid();
        var entity = new ProductEntity { Id = id };
        var compiled = expression.Compile();
        compiled.DynamicInvoke(entity).Should().Be(id);
    }

    [Fact]
    public void BuildPropertyAccess_DecimalProperty_ReturnsTypedExpression()
    {
        // Arrange

        // Act
        var expression = ExpressionBuilder.BuildPropertyAccess<ProductEntity>("UnitPrice");

        // Assert
        expression.ReturnType.Should().Be(typeof(decimal));
        expression.Body.NodeType.Should().Be(ExpressionType.MemberAccess);

        var entity = new ProductEntity { UnitPrice = 19.99m };
        var compiled = expression.Compile();
        compiled.DynamicInvoke(entity).Should().Be(19.99m);
    }

    [Fact]
    public void BuildPropertyAccess_DateTimeProperty_ReturnsTypedExpression()
    {
        // Arrange

        // Act
        var expression = ExpressionBuilder.BuildPropertyAccess<ProductEntity>("CreatedAt");

        // Assert
        expression.ReturnType.Should().Be(typeof(DateTime));
        expression.Body.NodeType.Should().Be(ExpressionType.MemberAccess);

        var date = new DateTime(2025, 6, 15, 10, 30, 0, DateTimeKind.Utc);
        var entity = new ProductEntity { CreatedAt = date };
        var compiled = expression.Compile();
        compiled.DynamicInvoke(entity).Should().Be(date);
    }

    [Fact]
    public void BuildPropertyAccess_NullableGuidProperty_ReturnsTypedExpression()
    {
        // Arrange

        // Act
        var expression = ExpressionBuilder.BuildPropertyAccess<ProductEntity>("CategoryId");

        // Assert
        expression.ReturnType.Should().Be(typeof(Guid?));
        expression.Body.NodeType.Should().Be(ExpressionType.MemberAccess);

        var categoryId = Guid.NewGuid();
        var entity = new ProductEntity { CategoryId = categoryId };
        var compiled = expression.Compile();
        compiled.DynamicInvoke(entity).Should().Be(categoryId);

        var nullEntity = new ProductEntity { CategoryId = null };
        compiled.DynamicInvoke(nullEntity).Should().BeNull();
    }

    [Fact]
    public void BuildPropertyAccess_NullableStringProperty_ReturnsTypedExpression()
    {
        // Arrange

        // Act
        var expression = ExpressionBuilder.BuildPropertyAccess<ProductEntity>("OptionalDescription");

        // Assert
        expression.ReturnType.Should().Be(typeof(string));
        expression.Body.NodeType.Should().Be(ExpressionType.MemberAccess);

        var entity = new ProductEntity { OptionalDescription = "A nice widget" };
        var compiled = expression.Compile();
        compiled.DynamicInvoke(entity).Should().Be("A nice widget");

        var nullEntity = new ProductEntity { OptionalDescription = null };
        compiled.DynamicInvoke(nullEntity).Should().BeNull();
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
        compiled.DynamicInvoke(entity).Should().Be("Widget");
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
        productId.Compile().DynamicInvoke(product).Should().Be(product.Id);

        var category = new CategoryEntity { Id = Guid.NewGuid() };
        categoryId.Compile().DynamicInvoke(category).Should().Be(category.Id);
    }
}
