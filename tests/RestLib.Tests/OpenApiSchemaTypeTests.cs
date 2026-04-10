using FluentAssertions;
using Microsoft.OpenApi;
using RestLib.Endpoints;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Unit tests for <see cref="OpenApiEndpointConfiguration.GetOpenApiSchema"/>.
/// Verifies that every type branch produces the correct OpenAPI schema type, format, and nullable flag.
/// </summary>
[Trait("Type", "Unit")]
[Trait("Feature", "OpenApi")]
public class OpenApiSchemaTypeTests
{
    #region String

    [Fact]
    public void GetOpenApiSchema_String_ReturnsStringType()
    {
        // Act
        var schema = OpenApiEndpointConfiguration.GetOpenApiSchema(typeof(string));

        // Assert
        schema.Type.Should().Be(JsonSchemaType.String);
        schema.Format.Should().BeNull();
    }

    #endregion

    #region Boolean

    [Fact]
    public void GetOpenApiSchema_Bool_ReturnsBooleanType()
    {
        // Act
        var schema = OpenApiEndpointConfiguration.GetOpenApiSchema(typeof(bool));

        // Assert
        schema.Type.Should().Be(JsonSchemaType.Boolean);
        schema.Format.Should().BeNull();
    }

    #endregion

    #region Integer types

    [Theory]
    [MemberData(nameof(IntegerTypes))]
    public void GetOpenApiSchema_IntegerTypes_ReturnsIntegerType(Type type)
    {
        // Act
        var schema = OpenApiEndpointConfiguration.GetOpenApiSchema(type);

        // Assert
        schema.Type.Should().Be(JsonSchemaType.Integer);
        schema.Format.Should().BeNull();
    }

    public static TheoryData<Type> IntegerTypes => new()
    {
        typeof(int),
        typeof(long),
        typeof(short),
        typeof(byte),
    };

    #endregion

    #region Number types

    [Theory]
    [MemberData(nameof(NumberTypes))]
    public void GetOpenApiSchema_NumberTypes_ReturnsNumberType(Type type)
    {
        // Act
        var schema = OpenApiEndpointConfiguration.GetOpenApiSchema(type);

        // Assert
        schema.Type.Should().Be(JsonSchemaType.Number);
        schema.Format.Should().BeNull();
    }

    public static TheoryData<Type> NumberTypes => new()
    {
        typeof(decimal),
        typeof(double),
        typeof(float),
    };

    #endregion

    #region Guid

    [Fact]
    public void GetOpenApiSchema_Guid_ReturnsStringWithUuidFormat()
    {
        // Act
        var schema = OpenApiEndpointConfiguration.GetOpenApiSchema(typeof(Guid));

        // Assert
        schema.Type.Should().Be(JsonSchemaType.String);
        schema.Format.Should().Be("uuid");
    }

    #endregion

    #region Date/time types

    [Fact]
    public void GetOpenApiSchema_DateTime_ReturnsStringWithDateTimeFormat()
    {
        // Act
        var schema = OpenApiEndpointConfiguration.GetOpenApiSchema(typeof(DateTime));

        // Assert
        schema.Type.Should().Be(JsonSchemaType.String);
        schema.Format.Should().Be("date-time");
    }

    [Fact]
    public void GetOpenApiSchema_DateTimeOffset_ReturnsStringWithDateTimeFormat()
    {
        // Act
        var schema = OpenApiEndpointConfiguration.GetOpenApiSchema(typeof(DateTimeOffset));

        // Assert
        schema.Type.Should().Be(JsonSchemaType.String);
        schema.Format.Should().Be("date-time");
    }

    #endregion

    #region Enum

    [Fact]
    public void GetOpenApiSchema_Enum_ReturnsStringWithEnumValues()
    {
        // Act
        var schema = OpenApiEndpointConfiguration.GetOpenApiSchema(typeof(SampleStatus));

        // Assert
        schema.Type.Should().Be(JsonSchemaType.String);
        schema.Enum.Should().NotBeNull();
        var enumValues = schema.Enum!.Select(v => v!.GetValue<string>()).ToList();
        enumValues.Should().BeEquivalentTo("Active", "Inactive", "Pending");
    }

    #endregion

    #region Fallback (unknown type)

    [Fact]
    public void GetOpenApiSchema_UnknownType_FallsBackToString()
    {
        // Act — TimeSpan is not explicitly handled by any branch
        var schema = OpenApiEndpointConfiguration.GetOpenApiSchema(typeof(TimeSpan));

        // Assert
        schema.Type.Should().Be(JsonSchemaType.String);
        schema.Format.Should().BeNull();
    }

    #endregion

    #region Nullable wrappers

    [Fact]
    public void GetOpenApiSchema_NullableInt_ReturnsIntegerOrNull()
    {
        // Act
        var schema = OpenApiEndpointConfiguration.GetOpenApiSchema(typeof(int?));

        // Assert
        schema.Type.Should().Be(JsonSchemaType.Integer | JsonSchemaType.Null);
    }

    [Fact]
    public void GetOpenApiSchema_NullableBool_ReturnsBooleanOrNull()
    {
        // Act
        var schema = OpenApiEndpointConfiguration.GetOpenApiSchema(typeof(bool?));

        // Assert
        schema.Type.Should().Be(JsonSchemaType.Boolean | JsonSchemaType.Null);
    }

    [Fact]
    public void GetOpenApiSchema_NullableGuid_ReturnsStringUuidOrNull()
    {
        // Act
        var schema = OpenApiEndpointConfiguration.GetOpenApiSchema(typeof(Guid?));

        // Assert
        schema.Type.Should().Be(JsonSchemaType.String | JsonSchemaType.Null);
        schema.Format.Should().Be("uuid");
    }

    [Fact]
    public void GetOpenApiSchema_NullableDateTime_ReturnsStringDateTimeOrNull()
    {
        // Act
        var schema = OpenApiEndpointConfiguration.GetOpenApiSchema(typeof(DateTime?));

        // Assert
        schema.Type.Should().Be(JsonSchemaType.String | JsonSchemaType.Null);
        schema.Format.Should().Be("date-time");
    }

    [Fact]
    public void GetOpenApiSchema_NullableDecimal_ReturnsNumberOrNull()
    {
        // Act
        var schema = OpenApiEndpointConfiguration.GetOpenApiSchema(typeof(decimal?));

        // Assert
        schema.Type.Should().Be(JsonSchemaType.Number | JsonSchemaType.Null);
    }

    [Fact]
    public void GetOpenApiSchema_NullableEnum_ReturnsStringEnumOrNull()
    {
        // Act
        var schema = OpenApiEndpointConfiguration.GetOpenApiSchema(typeof(SampleStatus?));

        // Assert
        schema.Type.Should().Be(JsonSchemaType.String | JsonSchemaType.Null);
        schema.Enum.Should().NotBeNull();
        var enumValues = schema.Enum!.Select(v => v!.GetValue<string>()).ToList();
        enumValues.Should().BeEquivalentTo("Active", "Inactive", "Pending");
    }

    #endregion

    #region Non-nullable types must NOT include Null flag

    [Theory]
    [MemberData(nameof(NonNullableTypes))]
    public void GetOpenApiSchema_NonNullableType_DoesNotIncludeNullFlag(Type type)
    {
        // Act
        var schema = OpenApiEndpointConfiguration.GetOpenApiSchema(type);

        // Assert
        (schema.Type & JsonSchemaType.Null).Should().Be((JsonSchemaType)0,
            $"non-nullable {type.Name} should not include the Null flag");
    }

    public static TheoryData<Type> NonNullableTypes => new()
    {
        typeof(string),
        typeof(bool),
        typeof(int),
        typeof(long),
        typeof(short),
        typeof(byte),
        typeof(decimal),
        typeof(double),
        typeof(float),
        typeof(Guid),
        typeof(DateTime),
        typeof(DateTimeOffset),
    };

    #endregion

    #region Test enum

    /// <summary>
    /// Sample enum used for schema type tests.
    /// </summary>
    private enum SampleStatus
    {
        /// <summary>Active status.</summary>
        Active,

        /// <summary>Inactive status.</summary>
        Inactive,

        /// <summary>Pending status.</summary>
        Pending,
    }

    #endregion
}
