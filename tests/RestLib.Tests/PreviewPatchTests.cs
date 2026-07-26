using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using RestLib.Endpoints;
using RestLib.Serialization;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Unit tests for <see cref="PatchHelper.PreviewPatch{TEntity}"/>.
/// </summary>
[Trait("Type", "Unit")]
[Trait("Feature", "Patch")]
[Trait("Category", "Story31")]
public class PreviewPatchTests
{
    private static readonly JsonSerializerOptions JsonOptions = RestLibJsonOptions.CreateDefault();

    #region Basic merge behavior

    [Fact]
    public void PreviewPatch_SingleProperty_MergesIntoOriginal()
    {
        // Arrange
        var original = new PatchEntity
        {
            Id = 1,
            Name = "Original",
            Price = 9.99m,
            Description = "A product"
        };
        var patch = ParsePatch("""{"name": "Updated"}""");

        // Act
        var result = PatchHelper.PreviewPatch(original, patch, JsonOptions);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.Name.Should().Be("Updated");
        result.Price.Should().Be(9.99m);
        result.Description.Should().Be("A product");
    }

    [Fact]
    public void PreviewPatch_MultipleProperties_MergesAll()
    {
        // Arrange
        var original = new PatchEntity
        {
            Id = 1,
            Name = "Original",
            Price = 9.99m,
            Description = "A product"
        };
        var patch = ParsePatch("""{"name": "Updated", "price": 19.99}""");

        // Act
        var result = PatchHelper.PreviewPatch(original, patch, JsonOptions);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("Updated");
        result.Price.Should().Be(19.99m);
        result.Description.Should().Be("A product");
    }

    [Fact]
    public void PreviewPatch_AllProperties_OverwritesEntireEntity()
    {
        // Arrange
        var original = new PatchEntity
        {
            Id = 1,
            Name = "Original",
            Price = 9.99m,
            Description = "Old desc"
        };
        var patch = ParsePatch("""{"id": 1, "name": "New", "price": 50.0, "description": "New desc"}""");

        // Act
        var result = PatchHelper.PreviewPatch(original, patch, JsonOptions);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("New");
        result.Price.Should().Be(50.0m);
        result.Description.Should().Be("New desc");
    }

    #endregion

    #region Empty patch document

    [Fact]
    public void PreviewPatch_EmptyPatchDocument_ReturnsCopyOfOriginal()
    {
        // Arrange
        var original = new PatchEntity
        {
            Id = 1,
            Name = "Original",
            Price = 9.99m,
            Description = "A product"
        };
        var patch = ParsePatch("{}");

        // Act
        var result = PatchHelper.PreviewPatch(original, patch, JsonOptions);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.Name.Should().Be("Original");
        result.Price.Should().Be(9.99m);
        result.Description.Should().Be("A product");
    }

    [Fact]
    public void PreviewPatch_NonObjectPatchDocument_ReturnsNull()
    {
        // Arrange
        var original = new PatchEntity
        {
            Id = 1,
            Name = "Original",
            Price = 9.99m,
            Description = "A product"
        };
        var patch = ParsePatch("[]");

        // Act
        var result = PatchHelper.PreviewPatch(original, patch, JsonOptions);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Null values in patch

    [Fact]
    public void PreviewPatch_NullValueForNullableProperty_SetsToNull()
    {
        // Arrange
        var original = new PatchEntity
        {
            Id = 1,
            Name = "Original",
            Price = 9.99m,
            Description = "Has a description"
        };
        var patch = ParsePatch("""{"description": null}""");

        // Act
        var result = PatchHelper.PreviewPatch(original, patch, JsonOptions);

        // Assert
        result.Should().NotBeNull();
        result!.Description.Should().BeNull();
        result.Name.Should().Be("Original");
        result.Price.Should().Be(9.99m);
    }

    [Fact]
    public void PreviewPatch_NullValue_RemovesMemberAndSetsClrDefault()
    {
        // Arrange
        var original = new PatchEntity
        {
            Id = 1,
            Name = "Original",
            Price = 9.99m
        };
        var patch = ParsePatch("""{"name": null}""");

        // Act
        var result = PatchHelper.PreviewPatch(original, patch, JsonOptions);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().BeNull();
    }

    #endregion

    #region Unknown properties

    [Fact]
    public void PreviewPatch_UnknownProperty_IsIgnoredOnDeserialization()
    {
        // Arrange
        var original = new PatchEntity
        {
            Id = 1,
            Name = "Original",
            Price = 9.99m
        };
        var patch = ParsePatch("""{"nonexistent_field": "value", "name": "Updated"}""");

        // Act
        var result = PatchHelper.PreviewPatch(original, patch, JsonOptions);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("Updated");
        result.Price.Should().Be(9.99m);
        result.Id.Should().Be(1);
    }

    #endregion

    #region Snake_case naming

    [Fact]
    public void PreviewPatch_SnakeCaseProperty_ResolvesCorrectly()
    {
        // Arrange
        var original = new MultiWordPatchEntity
        {
            Id = 1,
            ProductName = "Widget",
            UnitPrice = 5.00m,
            IsActive = true
        };
        var patch = ParsePatch("""{"product_name": "Gadget", "is_active": false}""");

        // Act
        var result = PatchHelper.PreviewPatch(original, patch, JsonOptions);

        // Assert
        result.Should().NotBeNull();
        result!.ProductName.Should().Be("Gadget");
        result.UnitPrice.Should().Be(5.00m);
        result.IsActive.Should().BeFalse();
    }

    #endregion

    #region Nested objects

    [Fact]
    public void PreviewPatch_NestedObject_RecursivelyMergesMembers()
    {
        // Arrange
        var original = new EntityWithNestedObject
        {
            Id = 1,
            Name = "Parent",
            Address = new AddressValue { Street = "123 Main St", City = "Springfield", Zip = "62701" }
        };

        // Patch only supplies City; RFC 7396 preserves unmentioned nested members.
        var patch = ParsePatch("""{"address": {"city": "Shelbyville"}}""");

        // Act
        var result = PatchHelper.PreviewPatch(original, patch, JsonOptions);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("Parent");
        result.Address.Should().NotBeNull();
        result.Address!.City.Should().Be("Shelbyville");

        result.Address.Street.Should().Be("123 Main St");
        result.Address.Zip.Should().Be("62701");
    }

    [Fact]
    public void PreviewPatch_NestedNull_RemovesOnlyTheNamedMember()
    {
        // Arrange
        var original = new EntityWithNestedObject
        {
            Id = 1,
            Name = "Parent",
            Address = new AddressValue { Street = "123 Main St", City = "Springfield", Zip = "62701" }
        };
        var patch = ParsePatch("""{"address": {"zip": null}}""");

        // Act
        var result = PatchHelper.PreviewPatch(original, patch, JsonOptions);

        // Assert
        result.Should().NotBeNull();
        result!.Address.Should().NotBeNull();
        result.Address!.Street.Should().Be("123 Main St");
        result.Address.City.Should().Be("Springfield");
        result.Address.Zip.Should().BeNull();
    }

    #endregion

    #region Entity with validation attributes

    [Fact]
    public void PreviewPatch_ThatViolatesValidation_StillReturnsEntity()
    {
        // Arrange — PreviewPatch does not run validation; that is the caller's job
        var original = new ValidatedPatchEntity
        {
            Id = 1,
            Name = "Valid",
            Email = "valid@example.com"
        };
        var patch = ParsePatch("""{"name": ""}""");

        // Act
        var result = PatchHelper.PreviewPatch(original, patch, JsonOptions);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be(string.Empty);
        result.Email.Should().Be("valid@example.com");
    }

    #endregion

    #region Type coercion edge cases

    [Fact]
    public void PreviewPatch_BooleanProperty_AsJsonBoolean_Works()
    {
        // Arrange
        var original = new MultiWordPatchEntity
        {
            Id = 1,
            ProductName = "Widget",
            UnitPrice = 5.00m,
            IsActive = true
        };
        var patch = ParsePatch("""{"is_active": false}""");

        // Act
        var result = PatchHelper.PreviewPatch(original, patch, JsonOptions);

        // Assert
        result.Should().NotBeNull();
        result!.IsActive.Should().BeFalse();
    }

    [Fact]
    public void PreviewPatch_IntegerProperty_Works()
    {
        // Arrange
        var original = new PatchEntity
        {
            Id = 1,
            Name = "Original",
            Price = 9.99m
        };
        var patch = ParsePatch("""{"id": 42}""");

        // Act
        var result = PatchHelper.PreviewPatch(original, patch, JsonOptions);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(42);
    }

    [Fact]
    public void PreviewPatch_ArrayAndLargeNumbers_PreservesJsonValues()
    {
        // Arrange
        var original = new EntityWithPreciseValues
        {
            Amount = 1m,
            Sequence = 1,
            Tags = ["old", "values"]
        };
        var patch = ParsePatch(
            """{"amount":79228162514264337593543950335,"sequence":18446744073709551615,"tags":["replacement"]}""");

        // Act
        var result = PatchHelper.PreviewPatch(original, patch, JsonOptions);

        // Assert
        result.Should().NotBeNull();
        result!.Amount.Should().Be(decimal.MaxValue);
        result.Sequence.Should().Be(ulong.MaxValue);
        result.Tags.Should().Equal("replacement");
    }

    [Fact]
    public void PreviewPatch_CustomConverterObject_RecursivelyMergesItsJsonRepresentation()
    {
        // Arrange
        var jsonOptions = RestLibJsonOptions.CreateDefault();
        jsonOptions.Converters.Add(new ConvertedPointJsonConverter());
        var original = new EntityWithConvertedValue
        {
            Id = 1,
            Location = new ConvertedPoint(10, 20)
        };
        var patch = ParsePatch("""{"location":{"x":99}}""");

        // Act
        var result = PatchHelper.PreviewPatch(original, patch, jsonOptions);

        // Assert
        result.Should().NotBeNull();
        result!.Location.Should().Be(new ConvertedPoint(99, 20));
    }

    #endregion

    #region Helpers and test entities

    private static JsonElement ParsePatch(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private class PatchEntity
    {
        public int Id { get; set; }

        public string? Name { get; set; } = string.Empty;

        public decimal Price { get; set; }

        public string? Description { get; set; }
    }

    private class MultiWordPatchEntity
    {
        public int Id { get; set; }

        public string ProductName { get; set; } = string.Empty;

        public decimal UnitPrice { get; set; }

        public bool IsActive { get; set; }
    }

    private class EntityWithNestedObject
    {
        public int Id { get; set; }

        public string? Name { get; set; }

        public AddressValue? Address { get; set; }
    }

    private class AddressValue
    {
        public string? Street { get; set; }

        public string? City { get; set; }

        public string? Zip { get; set; }
    }

    private class ValidatedPatchEntity
    {
        public int Id { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        [EmailAddress]
        public string? Email { get; set; }
    }

    private class EntityWithPreciseValues
    {
        public decimal Amount { get; set; }

        public ulong Sequence { get; set; }

        public string[] Tags { get; set; } = [];
    }

    private class EntityWithConvertedValue
    {
        public int Id { get; set; }

        public ConvertedPoint Location { get; set; } = new(0, 0);
    }

    private sealed record ConvertedPoint(int X, int Y);

    private sealed class ConvertedPointJsonConverter : JsonConverter<ConvertedPoint>
    {
        public override ConvertedPoint Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            return new ConvertedPoint(
                root.GetProperty("x").GetInt32(),
                root.GetProperty("y").GetInt32());
        }

        public override void Write(
            Utf8JsonWriter writer,
            ConvertedPoint value,
            JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("x", value.X);
            writer.WriteNumber("y", value.Y);
            writer.WriteEndObject();
        }
    }

    #endregion
}
