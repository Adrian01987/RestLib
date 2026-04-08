using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using RestLib.Filtering;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Dedicated unit tests for <see cref="FilterParser"/> static helpers
/// (ParseQueryParameterKey, GetOperatorName, GetFriendlyTypeName, TryConvertValue, Parse).
/// </summary>
public class FilterParserUnitTests
{
    #region ParseQueryParameterKey

    [Fact]
    [Trait("Category", "Story6.1")]
    public void ParseQueryParameterKey_BareKey_ReturnsNameAndNullOperator()
    {
        // Act
        var (name, op) = FilterParser.ParseQueryParameterKey("price");

        // Assert
        name.Should().Be("price");
        op.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void ParseQueryParameterKey_BracketSyntax_ReturnsNameAndOperator()
    {
        // Act
        var (name, op) = FilterParser.ParseQueryParameterKey("price[gte]");

        // Assert
        name.Should().Be("price");
        op.Should().Be("gte");
    }

    [Theory]
    [Trait("Category", "Story6.1")]
    [InlineData("name[eq]", "name", "eq")]
    [InlineData("quantity[lt]", "quantity", "lt")]
    [InlineData("status[in]", "status", "in")]
    [InlineData("name[contains]", "name", "contains")]
    [InlineData("name[starts_with]", "name", "starts_with")]
    public void ParseQueryParameterKey_AllOperators_ParsedCorrectly(string key, string expectedName, string expectedOp)
    {
        // Act
        var (name, op) = FilterParser.ParseQueryParameterKey(key);

        // Assert
        name.Should().Be(expectedName);
        op.Should().Be(expectedOp);
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void ParseQueryParameterKey_SnakeCaseProperty_ParsedCorrectly()
    {
        // Act
        var (name, op) = FilterParser.ParseQueryParameterKey("category_id[neq]");

        // Assert
        name.Should().Be("category_id");
        op.Should().Be("neq");
    }

    [Theory]
    [Trait("Category", "Story6.1")]
    [InlineData("")]
    [InlineData("limit")]
    [InlineData("cursor")]
    [InlineData("fields")]
    public void ParseQueryParameterKey_NonFilterKeys_ReturnBareNameWithNullOperator(string key)
    {
        // Act
        var (name, op) = FilterParser.ParseQueryParameterKey(key);

        // Assert
        name.Should().Be(key);
        op.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void ParseQueryParameterKey_EmptyBrackets_ReturnsBareKey()
    {
        // Bracket regex requires [a-z_]+ inside, so [] does not match
        var (name, op) = FilterParser.ParseQueryParameterKey("price[]");

        // Assert — regex does not match, so the whole string is returned as the name
        name.Should().Be("price[]");
        op.Should().BeNull();
    }

    #endregion

    #region GetOperatorName

    [Theory]
    [Trait("Category", "Story6.1")]
    [InlineData(FilterOperator.Eq, "eq")]
    [InlineData(FilterOperator.Neq, "neq")]
    [InlineData(FilterOperator.Gt, "gt")]
    [InlineData(FilterOperator.Lt, "lt")]
    [InlineData(FilterOperator.Gte, "gte")]
    [InlineData(FilterOperator.Lte, "lte")]
    [InlineData(FilterOperator.Contains, "contains")]
    [InlineData(FilterOperator.StartsWith, "starts_with")]
    [InlineData(FilterOperator.In, "in")]
    public void GetOperatorName_AllOperators_ReturnsCorrectString(FilterOperator op, string expected)
    {
        // Act & Assert
        FilterParser.GetOperatorName(op).Should().Be(expected);
    }

    #endregion

    #region GetFriendlyTypeName

    [Theory]
    [Trait("Category", "Story6.1")]
    [InlineData(typeof(int), "integer")]
    [InlineData(typeof(long), "long integer")]
    [InlineData(typeof(decimal), "decimal number")]
    [InlineData(typeof(double), "number")]
    [InlineData(typeof(float), "number")]
    [InlineData(typeof(bool), "boolean (true/false)")]
    [InlineData(typeof(Guid), "GUID")]
    [InlineData(typeof(DateTime), "date/time")]
    [InlineData(typeof(DateTimeOffset), "date/time with timezone")]
    public void GetFriendlyTypeName_CommonTypes_ReturnsReadableName(Type type, string expected)
    {
        // Act & Assert
        FilterParser.GetFriendlyTypeName(type).Should().Be(expected);
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void GetFriendlyTypeName_NullableInt_ReturnsUnderlyingName()
    {
        // Act & Assert
        FilterParser.GetFriendlyTypeName(typeof(int?)).Should().Be("integer");
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void GetFriendlyTypeName_NullableGuid_ReturnsUnderlyingName()
    {
        // Act & Assert
        FilterParser.GetFriendlyTypeName(typeof(Guid?)).Should().Be("GUID");
    }

    #endregion

    #region TryConvertValue

    [Theory]
    [Trait("Category", "Story6.1")]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("True", true)]
    [InlineData("False", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    public void TryConvertValue_Boolean_ConvertsCorrectly(string input, bool expected)
    {
        // Act
        var (success, value, error) = FilterParser.TryConvertValue(input, typeof(bool));

        // Assert
        success.Should().BeTrue();
        value.Should().Be(expected);
        error.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void TryConvertValue_InvalidBoolean_ReturnsError()
    {
        // Act
        var (success, value, error) = FilterParser.TryConvertValue("maybe", typeof(bool));

        // Assert
        success.Should().BeFalse();
        value.Should().BeNull();
        error.Should().Contain("not a valid boolean");
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void TryConvertValue_ValidGuid_Succeeds()
    {
        // Arrange
        var guid = Guid.NewGuid();

        // Act
        var (success, value, error) = FilterParser.TryConvertValue(guid.ToString(), typeof(Guid));

        // Assert
        success.Should().BeTrue();
        value.Should().Be(guid);
        error.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void TryConvertValue_InvalidGuid_ReturnsError()
    {
        // Act
        var (success, _, error) = FilterParser.TryConvertValue("not-a-guid", typeof(Guid));

        // Assert
        success.Should().BeFalse();
        error.Should().Contain("not a valid GUID");
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void TryConvertValue_ValidDateTime_Succeeds()
    {
        // Act
        var (success, value, error) = FilterParser.TryConvertValue("2024-06-15T10:30:00Z", typeof(DateTime));

        // Assert
        success.Should().BeTrue();
        value.Should().BeOfType<DateTime>();
        error.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void TryConvertValue_InvalidDateTime_ReturnsError()
    {
        // Act
        var (success, _, error) = FilterParser.TryConvertValue("not-a-date", typeof(DateTime));

        // Assert
        success.Should().BeFalse();
        error.Should().Contain("not a valid date/time");
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void TryConvertValue_ValidDateTimeOffset_Succeeds()
    {
        // Act
        var (success, value, error) = FilterParser.TryConvertValue("2024-06-15T10:30:00+02:00", typeof(DateTimeOffset));

        // Assert
        success.Should().BeTrue();
        value.Should().BeOfType<DateTimeOffset>();
        error.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void TryConvertValue_InvalidDateTimeOffset_ReturnsError()
    {
        // Act
        var (success, _, error) = FilterParser.TryConvertValue("not-a-date", typeof(DateTimeOffset));

        // Assert
        success.Should().BeFalse();
        error.Should().Contain("not a valid date/time");
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void TryConvertValue_ValidEnum_Succeeds()
    {
        // Act
        var (success, value, error) = FilterParser.TryConvertValue("Active", typeof(ProductStatus));

        // Assert
        success.Should().BeTrue();
        value.Should().Be(ProductStatus.Active);
        error.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void TryConvertValue_EnumCaseInsensitive_Succeeds()
    {
        // Act
        var (success, value, _) = FilterParser.TryConvertValue("active", typeof(ProductStatus));

        // Assert
        success.Should().BeTrue();
        value.Should().Be(ProductStatus.Active);
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void TryConvertValue_InvalidEnum_ReturnsErrorWithValidValues()
    {
        // Act
        var (success, _, error) = FilterParser.TryConvertValue("Unknown", typeof(ProductStatus));

        // Assert
        success.Should().BeFalse();
        error.Should().Contain("Draft").And.Contain("Active").And.Contain("Discontinued");
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void TryConvertValue_Integer_Succeeds()
    {
        // Act
        var (success, value, _) = FilterParser.TryConvertValue("42", typeof(int));

        // Assert
        success.Should().BeTrue();
        value.Should().Be(42);
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void TryConvertValue_Decimal_Succeeds()
    {
        // Act
        var (success, value, _) = FilterParser.TryConvertValue("99.99", typeof(decimal));

        // Assert
        success.Should().BeTrue();
        value.Should().Be(99.99m);
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void TryConvertValue_InvalidInteger_ReturnsError()
    {
        // Act
        var (success, _, error) = FilterParser.TryConvertValue("abc", typeof(int));

        // Assert
        success.Should().BeFalse();
        error.Should().Contain("integer");
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void TryConvertValue_String_ReturnsAsIs()
    {
        // Act
        var (success, value, _) = FilterParser.TryConvertValue("hello world", typeof(string));

        // Assert
        success.Should().BeTrue();
        value.Should().Be("hello world");
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void TryConvertValue_NullableInt_ConvertsSuccessfully()
    {
        // Act
        var (success, value, _) = FilterParser.TryConvertValue("7", typeof(int?));

        // Assert
        success.Should().BeTrue();
        value.Should().Be(7);
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void TryConvertValue_NullableGuid_ConvertsSuccessfully()
    {
        // Arrange
        var guid = Guid.NewGuid();

        // Act
        var (success, value, _) = FilterParser.TryConvertValue(guid.ToString(), typeof(Guid?));

        // Assert
        success.Should().BeTrue();
        value.Should().Be(guid);
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void TryConvertValue_NullReturningConverter_ReturnsError()
    {
        // Act
        var (success, _, error) = FilterParser.TryConvertValue("anything", typeof(NullConvertedType));

        // Assert
        success.Should().BeFalse();
        error.Should().Contain("Cannot convert");
    }

    #endregion

    #region Parse — Happy Paths

    [Fact]
    [Trait("Category", "Story6.1")]
    public void Parse_BareEquality_ReturnsSingleFilterValue()
    {
        // Arrange
        var config = CreateConfig(("Name", "name", typeof(string)));
        var query = BuildQuery(("name", "Alice"));

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Values.Should().HaveCount(1);
        result.Values[0].PropertyName.Should().Be("Name");
        result.Values[0].QueryParameterName.Should().Be("name");
        result.Values[0].RawValue.Should().Be("Alice");
        result.Values[0].TypedValue.Should().Be("Alice");
        result.Values[0].Operator.Should().Be(FilterOperator.Eq);
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void Parse_BracketOperator_ReturnsParsedFilter()
    {
        // Arrange
        var config = CreateConfig(("Price", "price", typeof(decimal), FilterOperators.Comparison));
        var query = BuildQuery(("price[gte]", "10.50"));

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Values.Should().HaveCount(1);
        result.Values[0].PropertyName.Should().Be("Price");
        result.Values[0].RawValue.Should().Be("10.50");
        result.Values[0].Operator.Should().Be(FilterOperator.Gte);
        result.Values[0].TypedValue.Should().Be(10.50m);
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void Parse_MultipleFilters_ReturnsAll()
    {
        // Arrange
        var config = CreateConfig(
            ("Name", "name", typeof(string)),
            ("IsActive", "is_active", typeof(bool)));
        var query = BuildQuery(("name", "Alice"), ("is_active", "true"));

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Values.Should().HaveCount(2);
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void Parse_SamePropertyDifferentOperators_ReturnsAll()
    {
        // Arrange
        var config = CreateConfig(("Price", "price", typeof(decimal), FilterOperators.Comparison));
        var query = BuildQuery(("price[gte]", "10"), ("price[lte]", "100"));

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Values.Should().HaveCount(2);
        result.Values.Should().Contain(v => v.Operator == FilterOperator.Gte);
        result.Values.Should().Contain(v => v.Operator == FilterOperator.Lte);
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void Parse_UnconfiguredParameter_SilentlyIgnored()
    {
        // Arrange
        var config = CreateConfig(("Name", "name", typeof(string)));
        var query = BuildQuery(("unknown", "value"), ("name", "Alice"));

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Values.Should().HaveCount(1);
        result.Values[0].PropertyName.Should().Be("Name");
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void Parse_EmptyQuery_ReturnsEmptyResult()
    {
        // Arrange
        var config = CreateConfig(("Name", "name", typeof(string)));
        var query = new QueryCollection();

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Values.Should().BeEmpty();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void Parse_EmptyValue_IsSkipped()
    {
        // Arrange
        var config = CreateConfig(("Name", "name", typeof(string)));
        var query = BuildQuery(("name", ""));

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Values.Should().BeEmpty();
    }

    #endregion

    #region Parse — In Operator

    [Fact]
    [Trait("Category", "Story6.1")]
    public void Parse_InOperator_ParsesCommaSeparatedValues()
    {
        // Arrange
        var config = CreateConfig(("Status", "status", typeof(string), new[] { FilterOperator.In }));
        var query = BuildQuery(("status[in]", "Draft,Active,Discontinued"));

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Values.Should().HaveCount(1);
        result.Values[0].Operator.Should().Be(FilterOperator.In);
        result.Values[0].TypedValues.Should().HaveCount(3);
        result.Values[0].TypedValues.Should().Contain("Draft");
        result.Values[0].TypedValues.Should().Contain("Active");
        result.Values[0].TypedValues.Should().Contain("Discontinued");
        result.Values[0].TypedValue.Should().BeNull("TypedValue is null for In operator; TypedValues is used instead");
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void Parse_InOperator_EmptyList_ReturnsError()
    {
        // Arrange
        var config = CreateConfig(("Status", "status", typeof(string), new[] { FilterOperator.In }));
        var query = BuildQuery(("status[in]", ",,,"));

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Message.Should().Contain("at least one value");
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void Parse_InOperator_ExceedsMaxListSize_ReturnsError()
    {
        // Arrange
        var config = CreateConfig(("Quantity", "quantity", typeof(int), new[] { FilterOperator.In }));
        var values = string.Join(",", Enumerable.Range(1, 5));
        var query = BuildQuery(("quantity[in]", values));

        // Act — use maxInListSize=3 to trigger limit
        var result = FilterParser.Parse(query, config, maxInListSize: 3);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Message.Should().Contain("5 values").And.Contain("maximum is 3");
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void Parse_InOperator_InvalidValueInList_ReturnsError()
    {
        // Arrange
        var config = CreateConfig(("Quantity", "quantity", typeof(int), new[] { FilterOperator.In }));
        var query = BuildQuery(("quantity[in]", "1,abc,3"));

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].ProvidedValue.Should().Be("abc");
    }

    #endregion

    #region Parse — Validation Errors

    [Fact]
    [Trait("Category", "Story6.1")]
    public void Parse_UnknownOperator_ReturnsError()
    {
        // Arrange
        var config = CreateConfig(("Price", "price", typeof(decimal)));
        var query = BuildQuery(("price[like]", "10"));

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].ParameterName.Should().Be("price[like]");
        result.Errors[0].ProvidedValue.Should().Be("like");
        result.Errors[0].Message.Should().Contain("Unknown filter operator");
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void Parse_OperatorNotAllowed_ReturnsError()
    {
        // Arrange — only eq is allowed (default)
        var config = CreateConfig(("Price", "price", typeof(decimal)));
        var query = BuildQuery(("price[gt]", "10"));

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Message.Should().Contain("not allowed");
        result.Errors[0].Message.Should().Contain("eq");
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void Parse_TypeConversionFailure_ReturnsError()
    {
        // Arrange
        var config = CreateConfig(("Quantity", "quantity", typeof(int)));
        var query = BuildQuery(("quantity", "not_a_number"));

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].ParameterName.Should().Be("quantity");
        result.Errors[0].ProvidedValue.Should().Be("not_a_number");
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void Parse_MultipleValuesForSameKey_ReturnsError()
    {
        // Arrange
        var config = CreateConfig(("Name", "name", typeof(string)));
        var query = new QueryCollection(
            new Dictionary<string, StringValues>
            {
                { "name", new StringValues(new[] { "Alice", "Bob" }) }
            });

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Message.Should().Contain("Multiple values");
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void Parse_DuplicatePropertyOperatorPair_ReturnsError()
    {
        // The same (property, operator) pair appears twice via bracket + bare syntax.
        // Both "name" and "name[eq]" resolve to (Name, Eq).
        var config = CreateConfig(("Name", "name", typeof(string)));
        var query = BuildQuery(("name", "Alice"), ("name[eq]", "Bob"));

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message.Contains("Duplicate"));
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void Parse_ComparisonOperatorOnNonComparable_ReturnsError()
    {
        // Arrange — NullConvertedType does not implement IComparable
        var config = new FilterConfiguration<NullConverterEntity>();
        config.AddProperty("CustomProp", "custom_prop", typeof(NullConvertedType),
            [FilterOperator.Gt]);
        var query = BuildQuery(("custom_prop[gt]", "anything"));

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Message.Should().Contain("comparable type");
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void Parse_StringOperatorOnNonString_ReturnsError()
    {
        // Arrange
        var config = new FilterConfiguration<FilterableEntity>();
        config.AddProperty("Quantity", "quantity", typeof(int),
            [FilterOperator.Contains]);
        var query = BuildQuery(("quantity[contains]", "10"));

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Message.Should().Contain("string properties");
    }

    #endregion

    #region Parse — Operator-Specific Value Parsing

    [Theory]
    [Trait("Category", "Story6.1")]
    [InlineData("name[contains]", "ali", FilterOperator.Contains)]
    [InlineData("name[starts_with]", "Al", FilterOperator.StartsWith)]
    public void Parse_StringOperators_ParseCorrectly(string key, string value, FilterOperator expectedOp)
    {
        // Arrange
        var config = new FilterConfiguration<FilterableEntity>();
        config.AddProperty("Name", "name", typeof(string),
            [FilterOperator.Contains, FilterOperator.StartsWith]);
        var query = BuildQuery((key, value));

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Values.Should().HaveCount(1);
        result.Values[0].Operator.Should().Be(expectedOp);
        result.Values[0].TypedValue.Should().Be(value);
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void Parse_NeqOperator_ParsesCorrectly()
    {
        // Arrange
        var config = CreateConfig(("IsActive", "is_active", typeof(bool), FilterOperators.Equality));
        var query = BuildQuery(("is_active[neq]", "true"));

        // Act
        var result = FilterParser.Parse(query, config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Values.Should().HaveCount(1);
        result.Values[0].Operator.Should().Be(FilterOperator.Neq);
        result.Values[0].TypedValue.Should().Be(true);
    }

    #endregion

    #region Helpers

    private static FilterConfiguration<FilterableEntity> CreateConfig(
        params (string PropertyName, string QueryName, Type PropertyType)[] properties)
    {
        var config = new FilterConfiguration<FilterableEntity>();
        foreach (var (propName, queryName, propType) in properties)
        {
            config.AddProperty(propName, queryName, propType);
        }

        return config;
    }

    private static FilterConfiguration<FilterableEntity> CreateConfig(
        params (string PropertyName, string QueryName, Type PropertyType, IReadOnlyList<FilterOperator> Operators)[] properties)
    {
        var config = new FilterConfiguration<FilterableEntity>();
        foreach (var (propName, queryName, propType, ops) in properties)
        {
            config.AddProperty(propName, queryName, propType, ops);
        }

        return config;
    }

    private static QueryCollection BuildQuery(params (string Key, string Value)[] pairs)
    {
        var dict = new Dictionary<string, StringValues>();
        foreach (var (key, value) in pairs)
        {
            dict[key] = value;
        }

        return new QueryCollection(dict);
    }

    #endregion
}

/// <summary>
/// Unit tests for <see cref="FilterConfiguration{TEntity}"/>.
/// </summary>
public class FilterConfigurationTests
{
    [Fact]
    [Trait("Category", "Story6.1")]
    public void AddProperty_ConvertsToSnakeCase()
    {
        // Arrange
        var config = new FilterConfiguration<FilterableEntity>();

        // Act
        config.AddProperty(e => e.IsActive);
        config.AddProperty(e => e.CategoryId);
        config.AddProperty(e => e.CreatedAt);

        // Assert
        config.Properties.Should().HaveCount(3);
        config.Properties[0].QueryParameterName.Should().Be("is_active");
        config.Properties[1].QueryParameterName.Should().Be("category_id");
        config.Properties[2].QueryParameterName.Should().Be("created_at");
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void AddProperty_DefaultOperators_IncludesOnlyEq()
    {
        // Arrange
        var config = new FilterConfiguration<FilterableEntity>();

        // Act
        config.AddProperty(e => e.Name);

        // Assert
        config.Properties[0].AllowedOperators.Should().ContainSingle()
            .Which.Should().Be(FilterOperator.Eq);
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void AddProperty_ExplicitOperators_AlwaysIncludesEq()
    {
        // Arrange
        var config = new FilterConfiguration<FilterableEntity>();

        // Act
        config.AddProperty(e => e.Price, FilterOperator.Gt, FilterOperator.Lt);

        // Assert
        config.Properties[0].AllowedOperators.Should().Contain(FilterOperator.Eq);
        config.Properties[0].AllowedOperators.Should().Contain(FilterOperator.Gt);
        config.Properties[0].AllowedOperators.Should().Contain(FilterOperator.Lt);
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void AddProperty_StoresCorrectPropertyType()
    {
        // Arrange
        var config = new FilterConfiguration<FilterableEntity>();

        // Act
        config.AddProperty(e => e.Price);
        config.AddProperty(e => e.IsActive);
        config.AddProperty(e => e.CategoryId);
        config.AddProperty(e => e.Status);

        // Assert
        config.Properties[0].PropertyType.Should().Be(typeof(decimal));
        config.Properties[1].PropertyType.Should().Be(typeof(bool));
        config.Properties[2].PropertyType.Should().Be(typeof(Guid?));
        config.Properties[3].PropertyType.Should().Be(typeof(ProductStatus));
    }

    [Fact]
    [Trait("Category", "Story6.1")]
    public void AddProperty_DuplicateProperty_ThrowsInvalidOperationException()
    {
        // Arrange
        var config = new FilterConfiguration<FilterableEntity>();
        config.AddProperty(e => e.Price);

        // Act
        var act = () => config.AddProperty(e => e.Price);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*'Price'*already configured*filtering*");
    }
}
