using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using RestLib.FieldSelection;
using RestLib.Filtering;
using RestLib.Pagination;
using RestLib.Sorting;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Property-based / fuzz tests for parsers and the cursor encoder.
/// Uses FsCheck to generate random inputs and verify parsers never throw
/// and always produce well-formed results.
/// </summary>
[Trait("Category", "PropertyBased")]
[Trait("Type", "Unit")]
[Trait("Feature", "Filtering")]
public class ParserPropertyTests
{
    /// <summary>
    /// SortParser should never throw for any arbitrary string input.
    /// </summary>
    [Property(MaxTest = 200)]
    public bool SortParser_NeverThrows_ForArbitraryInput(string? input)
    {
        var config = new SortConfiguration<FilterableEntity>();
        config.AddProperty(p => p.Price);
        config.AddProperty(p => p.Name);
        config.AddProperty(p => p.CreatedAt);

        var result = SortParser.Parse(input, config);

        return result != null && result.Fields != null && result.Errors != null;
    }

    /// <summary>
    /// FieldSelectionParser should never throw for any arbitrary string input.
    /// </summary>
    [Property(MaxTest = 200)]
    public bool FieldSelectionParser_NeverThrows_ForArbitraryInput(string? input)
    {
        var config = new FieldSelectionConfiguration<FieldSelectableEntity>();
        config.AddProperty(p => p.Id);
        config.AddProperty(p => p.Name);
        config.AddProperty(p => p.Price);

        var result = FieldSelectionParser.Parse(input, config);

        return result != null && result.Fields != null && result.Errors != null;
    }

    /// <summary>
    /// FilterParser should never throw for any arbitrary string value on a string filter.
    /// </summary>
    [Property(MaxTest = 200)]
    public bool FilterParser_NeverThrows_ForArbitraryStringFilter(NonNull<string> input)
    {
        var config = new FilterConfiguration<FilterableEntity>();
        config.AddProperty(p => p.Name);

        var query = new QueryCollection(
            new Dictionary<string, StringValues>
            {
                { "name", input.Get }
            });

        var result = FilterParser.Parse(query, config);

        return result != null && result.Values != null && result.Errors != null;
    }

    /// <summary>
    /// FilterParser should never throw for arbitrary values targeting a numeric filter.
    /// </summary>
    [Property(MaxTest = 200)]
    public bool FilterParser_NeverThrows_ForArbitraryNumericFilter(NonNull<string> input)
    {
        var config = new FilterConfiguration<FilterableEntity>();
        config.AddProperty(p => p.Quantity);

        var query = new QueryCollection(
            new Dictionary<string, StringValues>
            {
                { "quantity", input.Get }
            });

        var result = FilterParser.Parse(query, config);

        return result != null;
    }

    /// <summary>
    /// FilterParser should never throw for arbitrary values targeting a GUID filter.
    /// </summary>
    [Property(MaxTest = 200)]
    public bool FilterParser_NeverThrows_ForArbitraryGuidFilter(NonNull<string> input)
    {
        var config = new FilterConfiguration<FilterableEntity>();
        config.AddProperty(p => p.CategoryId);

        var query = new QueryCollection(
            new Dictionary<string, StringValues>
            {
                { "category_id", input.Get }
            });

        var result = FilterParser.Parse(query, config);

        return result != null;
    }

    /// <summary>
    /// FilterParser should never throw for arbitrary values targeting a bool filter.
    /// </summary>
    [Property(MaxTest = 200)]
    public bool FilterParser_NeverThrows_ForArbitraryBoolFilter(NonNull<string> input)
    {
        var config = new FilterConfiguration<FilterableEntity>();
        config.AddProperty(p => p.IsActive);

        var query = new QueryCollection(
            new Dictionary<string, StringValues>
            {
                { "is_active", input.Get }
            });

        var result = FilterParser.Parse(query, config);

        return result != null;
    }

    /// <summary>
    /// FilterParser should never throw for arbitrary values targeting a DateTime filter.
    /// </summary>
    [Property(MaxTest = 200)]
    public bool FilterParser_NeverThrows_ForArbitraryDateTimeFilter(NonNull<string> input)
    {
        var config = new FilterConfiguration<FilterableEntity>();
        config.AddProperty(p => p.CreatedAt);

        var query = new QueryCollection(
            new Dictionary<string, StringValues>
            {
                { "created_at", input.Get }
            });

        var result = FilterParser.Parse(query, config);

        return result != null;
    }

    /// <summary>
    /// CursorEncoder.Encode/TryDecode should round-trip for any integer value.
    /// </summary>
    [Property(MaxTest = 200)]
    public bool CursorEncoder_RoundTrips_Integers(int value)
    {
        var encoded = CursorEncoder.Encode(value);
        if (string.IsNullOrEmpty(encoded))
        {
            return false;
        }

        var success = CursorEncoder.TryDecode<int>(encoded, out var decoded);
        return success && decoded == value;
    }

    /// <summary>
    /// CursorEncoder.Encode/TryDecode should round-trip for any string value.
    /// </summary>
    [Property(MaxTest = 200)]
    public bool CursorEncoder_RoundTrips_Strings(NonNull<string> input)
    {
        var value = input.Get;
        var encoded = CursorEncoder.Encode(value);
        if (string.IsNullOrEmpty(encoded))
        {
            return false;
        }

        var success = CursorEncoder.TryDecode<string>(encoded, out var decoded);
        return success && decoded == value;
    }

    /// <summary>
    /// CursorEncoder.TryDecode should never throw for any arbitrary string.
    /// </summary>
    [Property(MaxTest = 200)]
    public bool CursorEncoder_TryDecode_NeverThrows_ForArbitraryInput(string? input)
    {
        // Should not throw, regardless of input
        CursorEncoder.TryDecode<int>(input ?? string.Empty, out _);
        return true; // If we got here, no exception was thrown
    }

    /// <summary>
    /// CursorEncoder.IsValid should never throw for any arbitrary string.
    /// </summary>
    [Property(MaxTest = 200)]
    public bool CursorEncoder_IsValid_NeverThrows_ForArbitraryInput(string? input)
    {
        CursorEncoder.IsValid(input);
        return true;
    }

    /// <summary>
    /// FieldSelectionParser given very long input should not throw.
    /// </summary>
    [Fact]
    public void FieldSelectionParser_VeryLongInput_DoesNotThrow()
    {
        // Arrange
        var config = new FieldSelectionConfiguration<FieldSelectableEntity>();
        config.AddProperty(p => p.Id);
        config.AddProperty(p => p.Name);

        // Generate an extremely long string of comma-separated garbage fields
        var longInput = string.Join(",", Enumerable.Range(0, 10_000).Select(i => $"field_{i}"));

        // Act
        var result = FieldSelectionParser.Parse(longInput, config);

        // Assert
        result.Should().NotBeNull();
        result.Fields.Should().BeEmpty();
        result.Errors.Should().HaveCount(10_000);
    }

    /// <summary>
    /// SortParser with Unicode-heavy random field names should not throw.
    /// </summary>
    [Fact]
    public void SortParser_UnicodeFieldNames_DoesNotThrow()
    {
        // Arrange
        var config = new SortConfiguration<FilterableEntity>();
        config.AddProperty(p => p.Name);

        var unicodeInputs = new[]
        {
            "\u00e9\u00e8\u00ea:asc",
            "\u4e2d\u6587:desc",
            "\ud83d\ude00:asc",
            "\u0000\u0001\u0002:desc",
            "\t\n\r:asc",
            "name\u200b:asc" // zero-width space
        };

        foreach (var input in unicodeInputs)
        {
            // Act & Assert — should never throw
            var result = SortParser.Parse(input, config);
            result.Should().NotBeNull();
        }
    }

    /// <summary>
    /// FilterParser with empty and whitespace-only values should be handled gracefully.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void FilterParser_WhitespaceValues_HandledGracefully(string value)
    {
        // Arrange
        var config = new FilterConfiguration<FilterableEntity>();
        config.AddProperty(p => p.Name);

        var query = new QueryCollection(
            new Dictionary<string, StringValues>
            {
                { "name", value }
            });

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.Should().NotBeNull();
    }

    /// <summary>
    /// FilterParser with bracket-syntax operator query keys should never throw.
    /// </summary>
    [Property(MaxTest = 200)]
    public bool FilterParser_NeverThrows_ForArbitraryBracketSyntax(NonNull<string> input)
    {
        var config = new FilterConfiguration<FilterableEntity>();
        config.AddProperty(p => p.Quantity, FilterOperators.Comparison);

        var query = new QueryCollection(
            new Dictionary<string, StringValues>
            {
            { $"quantity[{input.Get}]", "42" }
            });

        var result = FilterParser.Parse(query, config);

        return result != null && result.Values != null && result.Errors != null;
    }

    /// <summary>
    /// FilterParser bracket syntax with random operator names should never throw.
    /// </summary>
    [Property(MaxTest = 200)]
    public bool FilterParser_NeverThrows_ForArbitraryOperatorAndValue(NonNull<string> op, NonNull<string> value)
    {
        var config = new FilterConfiguration<FilterableEntity>();
        config.AddProperty(p => p.Name, FilterOperators.All);

        var query = new QueryCollection(
            new Dictionary<string, StringValues>
            {
            { $"name[{op.Get}]", value.Get }
            });

        var result = FilterParser.Parse(query, config);

        return result != null;
    }

    /// <summary>
    /// FilterParser in operator with arbitrary comma-separated values should never throw.
    /// </summary>
    [Property(MaxTest = 200)]
    public bool FilterParser_NeverThrows_ForArbitraryInOperatorValues(NonNull<string> input)
    {
        var config = new FilterConfiguration<FilterableEntity>();
        config.AddProperty(p => p.Name, FilterOperator.In);

        var query = new QueryCollection(
            new Dictionary<string, StringValues>
            {
            { "name[in]", input.Get }
            });

        var result = FilterParser.Parse(query, config);

        return result != null;
    }

    /// <summary>
    /// ParseQueryParameterKey should never throw for any arbitrary string.
    /// </summary>
    [Property(MaxTest = 200)]
    public bool FilterParser_ParseQueryParameterKey_NeverThrows(string? input)
    {
        var (paramName, operatorStr) = FilterParser.ParseQueryParameterKey(input ?? string.Empty);

        return paramName != null;
    }
}
