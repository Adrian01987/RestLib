using FluentAssertions;
using RestLib.FieldSelection;
using RestLib.Sorting;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Characterization tests for the shared configured comma-list parser used by
/// sorting and field selection.
/// </summary>
[Trait("Type", "Unit")]
[Trait("Feature", "QueryParsing")]
public class ConfiguredQueryListParserTests
{
    [Fact]
    [Trait("Category", "Story5.1")]
    [Trait("Category", "Story7.1")]
    public void Parse_SharedWhitespaceEmptyAndCaseRules_ProduceEquivalentFieldOrder()
    {
        // Arrange
        var sortConfiguration = new SortConfiguration<FilterableEntity>();
        sortConfiguration.AddProperty(entity => entity.Price);
        sortConfiguration.AddProperty(entity => entity.Name);
        var fieldConfiguration = new FieldSelectionConfiguration<FilterableEntity>();
        fieldConfiguration.AddProperty(entity => entity.Price);
        fieldConfiguration.AddProperty(entity => entity.Name);

        // Act
        var sortResult = SortParser.Parse(" , PRICE:desc, , name ,", sortConfiguration);
        var fieldResult = FieldSelectionParser.Parse(" , PRICE, , name ,", fieldConfiguration);

        // Assert
        sortResult.IsValid.Should().BeTrue();
        fieldResult.IsValid.Should().BeTrue();
        sortResult.Fields.Select(field => field.QueryParameterName)
            .Should().Equal("price", "name");
        fieldResult.Fields.Select(field => field.QueryParameterName)
            .Should().Equal("price", "name");
    }

    [Fact]
    [Trait("Category", "Story5.1")]
    [Trait("Category", "Story7.1")]
    public void Parse_SharedCanonicalDuplicateRule_DeduplicatesCaseInsensitively()
    {
        // Arrange
        var sortConfiguration = new SortConfiguration<FilterableEntity>();
        sortConfiguration.AddProperty(entity => entity.Price);
        var fieldConfiguration = new FieldSelectionConfiguration<FilterableEntity>();
        fieldConfiguration.AddProperty(entity => entity.Price);

        // Act
        var sortResult = SortParser.Parse("price:asc,PRICE:desc", sortConfiguration);
        var fieldResult = FieldSelectionParser.Parse("price,PRICE", fieldConfiguration);

        // Assert
        sortResult.Fields.Should().ContainSingle();
        fieldResult.Fields.Should().ContainSingle();
        sortResult.Errors.Should().ContainSingle()
            .Which.Message.Should().Be("Duplicate sort field.");
        fieldResult.Errors.Should().ContainSingle()
            .Which.Message.Should().Be("Duplicate field.");
    }

    [Fact]
    [Trait("Category", "Story5.1")]
    public void Parse_InvalidSortItem_DoesNotReserveFieldForLaterValidItem()
    {
        // Arrange
        var configuration = new SortConfiguration<FilterableEntity>();
        configuration.AddProperty(entity => entity.Price);

        // Act
        var result = SortParser.Parse("price:sideways,price:desc", configuration);

        // Assert
        result.Errors.Should().ContainSingle()
            .Which.Message.Should().Be("Direction must be 'asc' or 'desc'.");
        result.Fields.Should().ContainSingle();
        result.Fields[0].Direction.Should().Be(SortDirection.Desc);
    }
}
