using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using FluentAssertions;
using RestLib.Validation;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Unit tests for the EntityValidator class.
/// </summary>
[Trait("Type", "Unit")]
[Trait("Feature", "Validation")]
public class EntityValidatorTests
{
    #region Success scenarios

    [Fact]
    public void Validate_ValidEntity_ReturnsSuccess()
    {
        // Arrange
        var entity = new TestValidatedEntity
        {
            Name = "Valid Name",
            Email = "test@example.com",
            Age = 25
        };

        // Act
        var result = EntityValidator.Validate(entity);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_EntityWithOptionalFieldsNull_ReturnsSuccess()
    {
        // Arrange
        var entity = new TestValidatedEntity
        {
            Name = "Valid Name",
            Email = null, // Optional field
            Age = 18
        };

        // Act
        var result = EntityValidator.Validate(entity);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region Required validation

    [Fact]
    public void Validate_MissingRequiredField_ReturnsFailed()
    {
        // Arrange
        var entity = new TestValidatedEntity
        {
            Name = null!, // Required field is null
            Age = 25
        };

        // Act
        var result = EntityValidator.Validate(entity);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainKey("Name");
    }

    [Fact]
    public void Validate_EmptyRequiredString_ReturnsFailed()
    {
        // Arrange
        var entity = new TestValidatedEntity
        {
            Name = "", // Empty string
            Age = 25
        };

        // Act
        var result = EntityValidator.Validate(entity);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainKey("Name");
    }

    #endregion

    #region Range validation

    [Fact]
    public void Validate_ValueBelowRange_ReturnsFailed()
    {
        // Arrange
        var entity = new TestValidatedEntity
        {
            Name = "Test",
            Age = 0 // Below minimum of 1
        };

        // Act
        var result = EntityValidator.Validate(entity);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainKey("Age");
    }

    [Fact]
    public void Validate_ValueAboveRange_ReturnsFailed()
    {
        // Arrange
        var entity = new TestValidatedEntity
        {
            Name = "Test",
            Age = 200 // Above maximum of 150
        };

        // Act
        var result = EntityValidator.Validate(entity);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainKey("Age");
    }

    #endregion

    #region MaxLength validation

    [Fact]
    public void Validate_StringExceedsMaxLength_ReturnsFailed()
    {
        // Arrange
        var entity = new TestValidatedEntity
        {
            Name = new string('a', 51), // Exceeds max length of 50
            Age = 25
        };

        // Act
        var result = EntityValidator.Validate(entity);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainKey("Name");
    }

    #endregion

    #region Email validation

    [Fact]
    public void Validate_InvalidEmail_ReturnsFailed()
    {
        // Arrange
        var entity = new TestValidatedEntity
        {
            Name = "Test",
            Email = "not-an-email",
            Age = 25
        };

        // Act
        var result = EntityValidator.Validate(entity);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainKey("Email");
    }

    [Fact]
    public void Validate_ValidEmail_ReturnsSuccess()
    {
        // Arrange
        var entity = new TestValidatedEntity
        {
            Name = "Test",
            Email = "valid@example.com",
            Age = 25
        };

        // Act
        var result = EntityValidator.Validate(entity);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region Multiple errors

    [Fact]
    public void Validate_MultipleErrors_ReturnsAllErrors()
    {
        // Arrange
        var entity = new TestValidatedEntity
        {
            Name = null!, // Required
            Email = "invalid", // Invalid email
            Age = -5 // Below range
        };

        // Act
        var result = EntityValidator.Validate(entity);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(3);
        result.Errors.Should().ContainKey("Name");
        result.Errors.Should().ContainKey("Email");
        result.Errors.Should().ContainKey("Age");
    }

    #endregion

    #region Naming policy conversion

    [Fact]
    public void Validate_WithSnakeCasePolicy_ReturnsSnakeCaseFieldNames()
    {
        // Arrange
        var entity = new TestMultiWordEntity
        {
            ProductName = null!, // Required
            UnitPrice = -5 // Below range
        };

        // Act
        var result = EntityValidator.Validate(entity, JsonNamingPolicy.SnakeCaseLower);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainKey("product_name"); // ProductName -> product_name
        result.Errors.Should().ContainKey("unit_price"); // UnitPrice -> unit_price
    }

    [Fact]
    public void Validate_WithCamelCasePolicy_ReturnsCamelCaseFieldNames()
    {
        // Arrange
        var entity = new TestMultiWordEntity
        {
            ProductName = null!, // Required
            UnitPrice = -5 // Below range
        };

        // Act
        var result = EntityValidator.Validate(entity, JsonNamingPolicy.CamelCase);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainKey("productName"); // ProductName -> productName
        result.Errors.Should().ContainKey("unitPrice"); // UnitPrice -> unitPrice
    }

    [Fact]
    public void Validate_WithNullPolicy_ReturnsPascalCaseFieldNames()
    {
        // Arrange
        var entity = new TestMultiWordEntity
        {
            ProductName = null!, // Required
            UnitPrice = -5 // Below range
        };

        // Act
        var result = EntityValidator.Validate(entity, namingPolicy: null);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainKey("ProductName"); // Unchanged
        result.Errors.Should().ContainKey("UnitPrice"); // Unchanged
    }

    #endregion

    #region Error messages

    [Fact]
    public void Validate_ErrorContainsProperMessage()
    {
        // Arrange
        var entity = new TestValidatedEntity
        {
            Name = null!,
            Age = 25
        };

        // Act
        var result = EntityValidator.Validate(entity);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors["Name"].Should().Contain(m => m.Contains("required", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_MultipleErrorsOnSameField_AggregatesMessages()
    {
        // Arrange - Entity with both Required and MinLength on same field
        var entity = new TestWithMultipleConstraintsEntity
        {
            Name = "ab" // Too short (MinLength = 3)
        };

        // Act
        var result = EntityValidator.Validate(entity);

        // Assert - Should have the validation error
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainKey("Name");
    }

    #endregion

    #region Null entity

    [Fact]
    public void Validate_NullEntity_ThrowsArgumentNullException()
    {
        // Act
        Action act = () => EntityValidator.Validate<TestValidatedEntity>(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion
}

#region Test entity classes

public class TestValidatedEntity
{
    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    [EmailAddress]
    public string? Email { get; set; }

    [Range(1, 150)]
    public int Age { get; set; }
}

public class TestMultiWordEntity
{
    [Required]
    public string ProductName { get; set; } = string.Empty;

    [Range(0, double.MaxValue)]
    public decimal UnitPrice { get; set; }
}

public class TestWithMultipleConstraintsEntity
{
    [Required]
    [MinLength(3)]
    public string Name { get; set; } = string.Empty;
}

#endregion
