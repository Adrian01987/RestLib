using FluentAssertions;
using RestLib.FieldSelection;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Dedicated unit tests for <see cref="FieldSelectionParser"/> covering edge cases
/// in parsing, whitespace handling, error reporting, and mixed valid/invalid input.
/// </summary>
[Trait("Type", "Unit")]
[Trait("Feature", "FieldSelection")]
public class FieldSelectionParserUnitTests
{
    #region Helpers

    private static FieldSelectionConfiguration<FieldSelectableEntity> CreateConfig(
        params string[] propertyNames)
    {
        var config = new FieldSelectionConfiguration<FieldSelectableEntity>();
        foreach (var name in propertyNames)
        {
            config.AddProperty(name, RestLib.Internal.NamingUtils.ConvertToSnakeCase(name));
        }

        return config;
    }

    private static FieldSelectionConfiguration<FieldSelectableEntity> CreateDefaultConfig()
    {
        var config = new FieldSelectionConfiguration<FieldSelectableEntity>();
        config.AddProperty(e => e.Id);
        config.AddProperty(e => e.Name);
        config.AddProperty(e => e.Price);
        config.AddProperty(e => e.Category);
        config.AddProperty(e => e.CreatedAt);
        return config;
    }

    #endregion

    #region Parse — Happy Paths

    [Fact]
    [Trait("Category", "Story7.1")]
    public void Parse_SingleField_ReturnsSingleSelectedField()
    {
        // Arrange
        var config = CreateDefaultConfig();

        // Act
        var result = FieldSelectionParser.Parse("name", config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Fields.Should().HaveCount(1);
        result.Fields[0].PropertyName.Should().Be("Name");
        result.Fields[0].QueryFieldName.Should().Be("name");
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public void Parse_MultipleFields_ReturnsAllInOrder()
    {
        // Arrange
        var config = CreateDefaultConfig();

        // Act
        var result = FieldSelectionParser.Parse("price,name,id", config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Fields.Should().HaveCount(3);
        result.Fields[0].PropertyName.Should().Be("Price");
        result.Fields[1].PropertyName.Should().Be("Name");
        result.Fields[2].PropertyName.Should().Be("Id");
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public void Parse_SnakeCaseField_MatchesCorrectProperty()
    {
        // Arrange
        var config = CreateDefaultConfig();

        // Act
        var result = FieldSelectionParser.Parse("created_at", config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Fields.Should().HaveCount(1);
        result.Fields[0].PropertyName.Should().Be("CreatedAt");
        result.Fields[0].QueryFieldName.Should().Be("created_at");
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public void Parse_AllConfiguredFields_ReturnsAll()
    {
        // Arrange
        var config = CreateDefaultConfig();

        // Act
        var result = FieldSelectionParser.Parse("id,name,price,category,created_at", config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Fields.Should().HaveCount(5);
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public void Parse_CaseInsensitiveFieldNames_MatchesCorrectly()
    {
        // Arrange
        var config = CreateDefaultConfig();

        // Act
        var result = FieldSelectionParser.Parse("ID,NAME,Created_At", config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Fields.Should().HaveCount(3);
        result.Fields[0].QueryFieldName.Should().Be("id");
        result.Fields[1].QueryFieldName.Should().Be("name");
        result.Fields[2].QueryFieldName.Should().Be("created_at");
    }

    #endregion

    #region Parse — Empty / Null / Whitespace Input

    [Fact]
    [Trait("Category", "Story7.1")]
    public void Parse_NullValue_ReturnsEmptyValidResult()
    {
        // Arrange
        var config = CreateDefaultConfig();

        // Act
        var result = FieldSelectionParser.Parse(null, config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Fields.Should().BeEmpty();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public void Parse_EmptyString_ReturnsEmptyValidResult()
    {
        // Arrange
        var config = CreateDefaultConfig();

        // Act
        var result = FieldSelectionParser.Parse("", config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Fields.Should().BeEmpty();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public void Parse_WhitespaceOnly_ReturnsEmptyValidResult()
    {
        // Arrange
        var config = CreateDefaultConfig();

        // Act
        var result = FieldSelectionParser.Parse("   ", config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Fields.Should().BeEmpty();
    }

    #endregion

    #region Parse — Whitespace / Comma Edge Cases

    [Fact]
    [Trait("Category", "Story7.1")]
    public void Parse_FieldsWithSurroundingSpaces_TrimsAndMatches()
    {
        // Arrange
        var config = CreateDefaultConfig();

        // Act
        var result = FieldSelectionParser.Parse(" id , name , price ", config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Fields.Should().HaveCount(3);
        result.Fields[0].PropertyName.Should().Be("Id");
        result.Fields[1].PropertyName.Should().Be("Name");
        result.Fields[2].PropertyName.Should().Be("Price");
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public void Parse_TrailingComma_IgnoresEmptySegment()
    {
        // Arrange
        var config = CreateDefaultConfig();

        // Act
        var result = FieldSelectionParser.Parse("id,name,", config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Fields.Should().HaveCount(2);
        result.Fields[0].PropertyName.Should().Be("Id");
        result.Fields[1].PropertyName.Should().Be("Name");
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public void Parse_LeadingComma_IgnoresEmptySegment()
    {
        // Arrange
        var config = CreateDefaultConfig();

        // Act
        var result = FieldSelectionParser.Parse(",id,name", config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Fields.Should().HaveCount(2);
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public void Parse_MultipleConsecutiveCommas_IgnoresEmptySegments()
    {
        // Arrange
        var config = CreateDefaultConfig();

        // Act
        var result = FieldSelectionParser.Parse("id,,,name", config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Fields.Should().HaveCount(2);
        result.Fields[0].PropertyName.Should().Be("Id");
        result.Fields[1].PropertyName.Should().Be("Name");
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public void Parse_OnlyCommas_ReturnsEmptyValidResult()
    {
        // Arrange
        var config = CreateDefaultConfig();

        // Act
        var result = FieldSelectionParser.Parse(",,,", config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Fields.Should().BeEmpty();
    }

    #endregion

    #region Parse — Invalid Field Errors

    [Fact]
    [Trait("Category", "Story7.1")]
    public void Parse_SingleInvalidField_ReturnsErrorWithAllowedFields()
    {
        // Arrange
        var config = CreateDefaultConfig();

        // Act
        var result = FieldSelectionParser.Parse("unknown_field", config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Field.Should().Be("unknown_field");
        result.Errors[0].Message.Should().Contain("not a selectable field");
        result.Errors[0].Message.Should().Contain("id");
        result.Errors[0].Message.Should().Contain("name");
        result.Errors[0].Message.Should().Contain("price");
        result.Fields.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public void Parse_MultipleInvalidFields_ReturnsMultipleErrors()
    {
        // Arrange
        var config = CreateDefaultConfig();

        // Act
        var result = FieldSelectionParser.Parse("foo,bar,baz", config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(3);
        result.Errors[0].Field.Should().Be("foo");
        result.Errors[1].Field.Should().Be("bar");
        result.Errors[2].Field.Should().Be("baz");
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public void Parse_MixedValidAndInvalid_ReturnsValidFieldsAndErrors()
    {
        // Arrange
        var config = CreateDefaultConfig();

        // Act
        var result = FieldSelectionParser.Parse("id,unknown,name,bad_field", config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Fields.Should().HaveCount(2);
        result.Fields[0].PropertyName.Should().Be("Id");
        result.Fields[1].PropertyName.Should().Be("Name");
        result.Errors.Should().HaveCount(2);
        result.Errors[0].Field.Should().Be("unknown");
        result.Errors[1].Field.Should().Be("bad_field");
    }

    #endregion

    #region Parse — Duplicate Fields

    [Fact]
    [Trait("Category", "Story7.1")]
    public void Parse_DuplicateField_ReturnsErrorAndDeduplicates()
    {
        // Arrange
        var config = CreateDefaultConfig();

        // Act
        var result = FieldSelectionParser.Parse("id,name,id", config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Field.Should().Be("id");
        result.Errors[0].Message.Should().Be("Duplicate field.");
        result.Fields.Should().HaveCount(2);
        result.Fields[0].PropertyName.Should().Be("Id");
        result.Fields[1].PropertyName.Should().Be("Name");
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public void Parse_DuplicateCaseInsensitive_ReturnsError()
    {
        // Arrange
        var config = CreateDefaultConfig();

        // Act — "id" and "ID" should be considered duplicates
        var result = FieldSelectionParser.Parse("id,ID", config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Field.Should().Be("ID");
        result.Errors[0].Message.Should().Be("Duplicate field.");
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public void Parse_MultipleDuplicates_ReturnsErrorForEach()
    {
        // Arrange
        var config = CreateDefaultConfig();

        // Act
        var result = FieldSelectionParser.Parse("id,name,id,name,price", config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(2);
        result.Errors.Should().Contain(e => e.Field == "id");
        result.Errors.Should().Contain(e => e.Field == "name");
        result.Fields.Should().HaveCount(3);
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public void Parse_DuplicateAndInvalid_ReturnsBothErrorTypes()
    {
        // Arrange
        var config = CreateDefaultConfig();

        // Act
        var result = FieldSelectionParser.Parse("id,unknown,id", config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(2);
        result.Errors.Should().Contain(e => e.Field == "unknown" && e.Message.Contains("not a selectable field"));
        result.Errors.Should().Contain(e => e.Field == "id" && e.Message == "Duplicate field.");
        result.Fields.Should().HaveCount(1);
        result.Fields[0].PropertyName.Should().Be("Id");
    }

    #endregion

    #region Parse — Allowed Names in Error Message

    [Fact]
    [Trait("Category", "Story7.1")]
    public void Parse_ErrorMessage_ListsAllAllowedFieldsCommaSeparated()
    {
        // Arrange
        var config = CreateConfig("Id", "Name");

        // Act
        var result = FieldSelectionParser.Parse("bad", config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors[0].Message.Should().Be("'bad' is not a selectable field. Allowed fields: id, name.");
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public void Parse_SingleConfiguredField_ErrorListsSingleAllowed()
    {
        // Arrange
        var config = CreateConfig("Id");

        // Act
        var result = FieldSelectionParser.Parse("unknown", config);

        // Assert
        result.Errors[0].Message.Should().Be("'unknown' is not a selectable field. Allowed fields: id.");
    }

    #endregion
}

/// <summary>
/// Dedicated unit tests for <see cref="FieldSelectionConfiguration{TEntity}"/>
/// covering AddProperty overloads, FindByQueryName, and edge cases.
/// </summary>
[Trait("Type", "Unit")]
[Trait("Feature", "FieldSelection")]
public class FieldSelectionConfigurationUnitTests
{
    #region AddProperty (expression-based)

    [Fact]
    [Trait("Category", "Story7.1")]
    public void AddProperty_Expression_ConvertsToSnakeCase()
    {
        // Arrange
        var config = new FieldSelectionConfiguration<FieldSelectableEntity>();

        // Act
        config.AddProperty(e => e.CreatedAt);

        // Assert
        config.Properties.Should().HaveCount(1);
        config.Properties[0].PropertyName.Should().Be("CreatedAt");
        config.Properties[0].QueryFieldName.Should().Be("created_at");
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public void AddProperty_Expression_MultipleProperties_PreservesOrder()
    {
        // Arrange
        var config = new FieldSelectionConfiguration<FieldSelectableEntity>();

        // Act
        config.AddProperty(e => e.Price);
        config.AddProperty(e => e.Id);
        config.AddProperty(e => e.Name);

        // Assert
        config.Properties.Should().HaveCount(3);
        config.Properties[0].PropertyName.Should().Be("Price");
        config.Properties[1].PropertyName.Should().Be("Id");
        config.Properties[2].PropertyName.Should().Be("Name");
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public void AddProperty_Expression_SingleWordProperty_LowercaseQueryName()
    {
        // Arrange
        var config = new FieldSelectionConfiguration<FieldSelectableEntity>();

        // Act
        config.AddProperty(e => e.Name);

        // Assert
        config.Properties[0].QueryFieldName.Should().Be("name");
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public void AddProperty_Expression_DuplicateProperty_Throws()
    {
        // Arrange
        var config = new FieldSelectionConfiguration<FieldSelectableEntity>();
        config.AddProperty(e => e.Id);

        // Act
        var act = () => config.AddProperty(e => e.Id);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*'Id'*already configured*field selection*");
    }

    #endregion

    #region AddProperty (internal string-based)

    [Fact]
    [Trait("Category", "Story7.1")]
    public void AddProperty_String_StoresExactNames()
    {
        // Arrange
        var config = new FieldSelectionConfiguration<FieldSelectableEntity>();

        // Act
        config.AddProperty("MyCustomProp", "my_custom_prop");

        // Assert
        config.Properties.Should().HaveCount(1);
        config.Properties[0].PropertyName.Should().Be("MyCustomProp");
        config.Properties[0].QueryFieldName.Should().Be("my_custom_prop");
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public void AddProperty_String_DuplicatePropertyName_Throws()
    {
        // Arrange
        var config = new FieldSelectionConfiguration<FieldSelectableEntity>();
        config.AddProperty("Price", "price");

        // Act
        var act = () => config.AddProperty("Price", "another_name");

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*'Price'*already configured*field selection*");
    }

    #endregion

    #region FindByQueryName

    [Fact]
    [Trait("Category", "Story7.1")]
    public void FindByQueryName_ExistingField_ReturnsConfiguration()
    {
        // Arrange
        var config = new FieldSelectionConfiguration<FieldSelectableEntity>();
        config.AddProperty(e => e.CreatedAt);

        // Act
        var found = config.FindByQueryName("created_at");

        // Assert
        found.Should().NotBeNull();
        found!.PropertyName.Should().Be("CreatedAt");
        found.QueryFieldName.Should().Be("created_at");
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public void FindByQueryName_CaseInsensitive_ReturnsConfiguration()
    {
        // Arrange
        var config = new FieldSelectionConfiguration<FieldSelectableEntity>();
        config.AddProperty(e => e.Name);

        // Act
        var found = config.FindByQueryName("NAME");

        // Assert
        found.Should().NotBeNull();
        found!.PropertyName.Should().Be("Name");
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public void FindByQueryName_NonExistentField_ReturnsNull()
    {
        // Arrange
        var config = new FieldSelectionConfiguration<FieldSelectableEntity>();
        config.AddProperty(e => e.Name);

        // Act
        var found = config.FindByQueryName("unknown_field");

        // Assert
        found.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Story7.1")]
    public void FindByQueryName_EmptyConfiguration_ReturnsNull()
    {
        // Arrange
        var config = new FieldSelectionConfiguration<FieldSelectableEntity>();

        // Act
        var found = config.FindByQueryName("id");

        // Assert
        found.Should().BeNull();
    }

    #endregion

    #region Properties Collection

    [Fact]
    [Trait("Category", "Story7.1")]
    public void Properties_EmptyConfiguration_ReturnsEmptyList()
    {
        // Arrange
        var config = new FieldSelectionConfiguration<FieldSelectableEntity>();

        // Assert
        config.Properties.Should().BeEmpty();
    }

    #endregion
}
