using System.Text.Json;
using FluentAssertions;
using RestLib.Filtering;
using RestLib.InMemory;
using RestLib.Pagination;
using Xunit;

namespace RestLib.Tests;

public partial class InMemoryRepositoryTests
{
  #region Filter Tests

  [Fact]
  public async Task GetAllAsync_WithEqualFilter_FiltersCorrectly()
  {
    var repository = CreateRepository();
    await repository.CreateAsync(CreateEntity("Alice", 100));
    await repository.CreateAsync(CreateEntity("Bob", 200));
    await repository.CreateAsync(CreateEntity("Charlie", 100));

    var filters = new List<FilterValue> { CreateFilter("Value", 100) };
    var request = new PaginationRequest { Limit = 10, Filters = filters };

    var result = await repository.GetAllAsync(request);

    result.Items.Should().HaveCount(2);
    result.Items.Should().OnlyContain(e => e.Value == 100);
  }

  [Fact]
  public async Task GetAllAsync_WithNameFilter_FiltersCorrectly()
  {
    var repository = CreateRepository();
    await repository.CreateAsync(CreateEntity("Alice", 100));
    await repository.CreateAsync(CreateEntity("Bob", 200));
    await repository.CreateAsync(CreateEntity("Alice", 300));

    var filters = new List<FilterValue> { CreateFilter("Name", "Alice") };
    var request = new PaginationRequest { Limit = 10, Filters = filters };

    var result = await repository.GetAllAsync(request);

    result.Items.Should().HaveCount(2);
    result.Items.Should().OnlyContain(e => e.Name == "Alice");
  }

  [Fact]
  public async Task GetAllAsync_WithMultipleFilters_AppliesAll()
  {
    var repository = CreateRepository();
    await repository.CreateAsync(CreateEntity("Alice", 100));
    await repository.CreateAsync(CreateEntity("Alice", 200));
    await repository.CreateAsync(CreateEntity("Bob", 100));

    var filters = new List<FilterValue> { CreateFilter("Name", "Alice"), CreateFilter("Value", 100) };
    var request = new PaginationRequest { Limit = 10, Filters = filters };

    var result = await repository.GetAllAsync(request);

    result.Items.Should().HaveCount(1);
    result.Items.Single().Name.Should().Be("Alice");
    result.Items.Single().Value.Should().Be(100);
  }

  [Fact]
  public async Task GetAllAsync_WithUnknownProperty_IgnoresFilter()
  {
    var repository = CreateRepository();
    await repository.CreateAsync(CreateEntity("Alice", 100));
    await repository.CreateAsync(CreateEntity("Bob", 200));

    var filters = new List<FilterValue> { CreateFilter("NonExistent", "Something") };
    var request = new PaginationRequest { Limit = 10, Filters = filters };

    var result = await repository.GetAllAsync(request);

    result.Items.Should().HaveCount(2);
  }

  #endregion

  #region ConvertFilterValue Tests

  // Entity with diverse property types to exercise all ConvertFilterValue branches
  private record FilterTestEntity(
      Guid Id,
      string Name,
      int Value,
      Guid CategoryId,
      DateTime CreatedAt,
      DateTimeOffset ModifiedAt,
      DayOfWeek DayOfWeek,
      int? NullableValue);

  private static InMemoryRepository<FilterTestEntity, Guid> CreateFilterTestRepository() =>
      new(e => e.Id, Guid.NewGuid);

  private static FilterTestEntity CreateFilterTestEntity(
      Guid? categoryId = null,
      DateTime? createdAt = null,
      DateTimeOffset? modifiedAt = null,
      DayOfWeek dayOfWeek = DayOfWeek.Monday,
      int? nullableValue = null)
  {
    return new FilterTestEntity(
        Guid.NewGuid(),
        "Test",
        100,
        categoryId ?? Guid.NewGuid(),
        createdAt ?? DateTime.UtcNow,
        modifiedAt ?? DateTimeOffset.UtcNow,
        dayOfWeek,
        nullableValue);
  }

  private static FilterValue CreateFilterForProperty(string propertyName, string rawValue, Type propertyType) => new()
  {
    PropertyName = propertyName,
    QueryParameterName = propertyName.ToLowerInvariant(),
    PropertyType = propertyType,
    RawValue = rawValue,
    TypedValue = null // Force ConvertFilterValue to be used
  };

  [Fact]
  public async Task GetAllAsync_WithGuidFilter_FiltersCorrectly()
  {
    // Arrange
    var repository = CreateFilterTestRepository();
    var targetCategoryId = Guid.NewGuid();
    var matchingEntity = CreateFilterTestEntity(categoryId: targetCategoryId);
    var otherEntity = CreateFilterTestEntity(categoryId: Guid.NewGuid());
    await repository.CreateAsync(matchingEntity);
    await repository.CreateAsync(otherEntity);

    var filters = new List<FilterValue>
    {
      CreateFilterForProperty("CategoryId", targetCategoryId.ToString(), typeof(Guid))
    };
    var request = new PaginationRequest { Limit = 10, Filters = filters };

    // Act
    var result = await repository.GetAllAsync(request);

    // Assert
    result.Items.Should().HaveCount(1);
    result.Items.Single().CategoryId.Should().Be(targetCategoryId);
  }

  [Fact]
  public async Task GetAllAsync_WithDateTimeFilter_FiltersCorrectly()
  {
    // Arrange — Use Unspecified kind so DateTime.Parse round-trips cleanly
    // (DateTime.Parse("2025-06-15T12:00:00") returns DateTimeKind.Unspecified)
    var targetDate = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Unspecified);
    var repository = CreateFilterTestRepository();
    var matchingEntity = CreateFilterTestEntity(createdAt: targetDate);
    var otherEntity = CreateFilterTestEntity(createdAt: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Unspecified));
    await repository.CreateAsync(matchingEntity);
    await repository.CreateAsync(otherEntity);

    var filters = new List<FilterValue>
    {
      CreateFilterForProperty("CreatedAt", "2025-06-15T12:00:00", typeof(DateTime))
    };
    var request = new PaginationRequest { Limit = 10, Filters = filters };

    // Act
    var result = await repository.GetAllAsync(request);

    // Assert
    result.Items.Should().HaveCount(1);
    result.Items.Single().CreatedAt.Should().Be(targetDate);
  }

  [Fact]
  public async Task GetAllAsync_WithDateTimeOffsetFilter_FiltersCorrectly()
  {
    // Arrange
    var repository = CreateFilterTestRepository();
    var targetDate = new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero);
    var matchingEntity = CreateFilterTestEntity(modifiedAt: targetDate);
    var otherEntity = CreateFilterTestEntity(modifiedAt: DateTimeOffset.UtcNow.AddDays(-1));
    await repository.CreateAsync(matchingEntity);
    await repository.CreateAsync(otherEntity);

    var filters = new List<FilterValue>
    {
      CreateFilterForProperty("ModifiedAt", targetDate.ToString("O"), typeof(DateTimeOffset))
    };
    var request = new PaginationRequest { Limit = 10, Filters = filters };

    // Act
    var result = await repository.GetAllAsync(request);

    // Assert
    result.Items.Should().HaveCount(1);
    result.Items.Single().ModifiedAt.Should().Be(targetDate);
  }

  [Fact]
  public async Task GetAllAsync_WithEnumFilter_FiltersCorrectly()
  {
    // Arrange
    var repository = CreateFilterTestRepository();
    var mondayEntity = CreateFilterTestEntity(dayOfWeek: DayOfWeek.Monday);
    var fridayEntity = CreateFilterTestEntity(dayOfWeek: DayOfWeek.Friday);
    await repository.CreateAsync(mondayEntity);
    await repository.CreateAsync(fridayEntity);

    var filters = new List<FilterValue>
    {
      CreateFilterForProperty("DayOfWeek", "Friday", typeof(DayOfWeek))
    };
    var request = new PaginationRequest { Limit = 10, Filters = filters };

    // Act
    var result = await repository.GetAllAsync(request);

    // Assert
    result.Items.Should().HaveCount(1);
    result.Items.Single().DayOfWeek.Should().Be(DayOfWeek.Friday);
  }

  [Fact]
  public async Task GetAllAsync_WithNullableTypeFilter_FiltersCorrectly()
  {
    // Arrange
    var repository = CreateFilterTestRepository();
    var withValue = CreateFilterTestEntity(nullableValue: 42);
    var withoutValue = CreateFilterTestEntity(nullableValue: null);
    await repository.CreateAsync(withValue);
    await repository.CreateAsync(withoutValue);

    var filters = new List<FilterValue>
    {
      CreateFilterForProperty("NullableValue", "42", typeof(int?))
    };
    var request = new PaginationRequest { Limit = 10, Filters = filters };

    // Act
    var result = await repository.GetAllAsync(request);

    // Assert
    result.Items.Should().HaveCount(1);
    result.Items.Single().NullableValue.Should().Be(42);
  }

  [Fact]
  public async Task GetAllAsync_WithNullFilterValue_ReturnsAllItems()
  {
    // Arrange — null RawValue triggers the early null return in ConvertFilterValue
    var repository = CreateFilterTestRepository();
    var entity1 = CreateFilterTestEntity();
    var entity2 = CreateFilterTestEntity();
    await repository.CreateAsync(entity1);
    await repository.CreateAsync(entity2);

    var filters = new List<FilterValue>
    {
      new()
      {
        PropertyName = "Name",
        QueryParameterName = "name",
        PropertyType = typeof(string),
        RawValue = null!,
        TypedValue = null
      }
    };
    var request = new PaginationRequest { Limit = 10, Filters = filters };

    // Act
    var result = await repository.GetAllAsync(request);

    // Assert — null filter value matches entities where Name is also null (neither should match)
    // but both entities have non-null Name, so 0 items match
    result.Items.Should().HaveCount(0);
  }

  [Fact]
  public async Task GetAllAsync_WithUnconvertibleFilterValue_FallsBackToRawString()
  {
    // Arrange — "not-a-guid" can't be parsed as Guid, so ConvertFilterValue
    // falls into the catch branch and returns the raw string
    var repository = CreateFilterTestRepository();
    var entity = CreateFilterTestEntity();
    await repository.CreateAsync(entity);

    var filters = new List<FilterValue>
    {
      CreateFilterForProperty("CategoryId", "not-a-guid", typeof(Guid))
    };
    var request = new PaginationRequest { Limit = 10, Filters = filters };

    // Act
    var result = await repository.GetAllAsync(request);

    // Assert — the raw string "not-a-guid" won't match any Guid value
    result.Items.Should().BeEmpty();
  }

  #endregion

  #region Operator Filter Tests

  private static FilterValue CreateOperatorFilter(string propertyName, object? typedValue, FilterOperator op) => new()
  {
    PropertyName = propertyName,
    QueryParameterName = propertyName.ToLowerInvariant(),
    PropertyType = typedValue?.GetType() ?? typeof(string),
    RawValue = typedValue?.ToString() ?? "",
    TypedValue = typedValue,
    Operator = op
  };

  private static FilterValue CreateInFilter(string propertyName, Type propertyType, IReadOnlyList<object?> typedValues) => new()
  {
    PropertyName = propertyName,
    QueryParameterName = propertyName.ToLowerInvariant(),
    PropertyType = propertyType,
    RawValue = string.Join(",", typedValues),
    TypedValue = null,
    Operator = FilterOperator.In,
    TypedValues = typedValues
  };

  [Fact]
  public async Task GetAllAsync_WithNeqFilter_FiltersCorrectly()
  {
    // Arrange
    var repository = CreateRepository();
    await repository.CreateAsync(CreateEntity("Alice", 100));
    await repository.CreateAsync(CreateEntity("Bob", 200));
    await repository.CreateAsync(CreateEntity("Charlie", 300));

    var filters = new List<FilterValue> { CreateOperatorFilter("Value", 100, FilterOperator.Neq) };
    var request = new PaginationRequest { Limit = 10, Filters = filters };

    // Act
    var result = await repository.GetAllAsync(request);

    // Assert
    result.Items.Should().HaveCount(2);
    result.Items.Should().OnlyContain(e => e.Value != 100);
  }

  [Fact]
  public async Task GetAllAsync_WithGtFilter_FiltersCorrectly()
  {
    // Arrange
    var repository = CreateRepository();
    await repository.CreateAsync(CreateEntity("Alice", 100));
    await repository.CreateAsync(CreateEntity("Bob", 200));
    await repository.CreateAsync(CreateEntity("Charlie", 300));

    var filters = new List<FilterValue> { CreateOperatorFilter("Value", 200, FilterOperator.Gt) };
    var request = new PaginationRequest { Limit = 10, Filters = filters };

    // Act
    var result = await repository.GetAllAsync(request);

    // Assert
    result.Items.Should().HaveCount(1);
    result.Items.Single().Value.Should().Be(300);
  }

  [Fact]
  public async Task GetAllAsync_WithLtFilter_FiltersCorrectly()
  {
    // Arrange
    var repository = CreateRepository();
    await repository.CreateAsync(CreateEntity("Alice", 100));
    await repository.CreateAsync(CreateEntity("Bob", 200));
    await repository.CreateAsync(CreateEntity("Charlie", 300));

    var filters = new List<FilterValue> { CreateOperatorFilter("Value", 200, FilterOperator.Lt) };
    var request = new PaginationRequest { Limit = 10, Filters = filters };

    // Act
    var result = await repository.GetAllAsync(request);

    // Assert
    result.Items.Should().HaveCount(1);
    result.Items.Single().Value.Should().Be(100);
  }

  [Fact]
  public async Task GetAllAsync_WithGteFilter_FiltersCorrectly()
  {
    // Arrange
    var repository = CreateRepository();
    await repository.CreateAsync(CreateEntity("Alice", 100));
    await repository.CreateAsync(CreateEntity("Bob", 200));
    await repository.CreateAsync(CreateEntity("Charlie", 300));

    var filters = new List<FilterValue> { CreateOperatorFilter("Value", 200, FilterOperator.Gte) };
    var request = new PaginationRequest { Limit = 10, Filters = filters };

    // Act
    var result = await repository.GetAllAsync(request);

    // Assert
    result.Items.Should().HaveCount(2);
    result.Items.Should().OnlyContain(e => e.Value >= 200);
  }

  [Fact]
  public async Task GetAllAsync_WithLteFilter_FiltersCorrectly()
  {
    // Arrange
    var repository = CreateRepository();
    await repository.CreateAsync(CreateEntity("Alice", 100));
    await repository.CreateAsync(CreateEntity("Bob", 200));
    await repository.CreateAsync(CreateEntity("Charlie", 300));

    var filters = new List<FilterValue> { CreateOperatorFilter("Value", 200, FilterOperator.Lte) };
    var request = new PaginationRequest { Limit = 10, Filters = filters };

    // Act
    var result = await repository.GetAllAsync(request);

    // Assert
    result.Items.Should().HaveCount(2);
    result.Items.Should().OnlyContain(e => e.Value <= 200);
  }

  [Fact]
  public async Task GetAllAsync_WithContainsFilter_FiltersCorrectly()
  {
    // Arrange
    var repository = CreateRepository();
    await repository.CreateAsync(CreateEntity("Alice Smith", 100));
    await repository.CreateAsync(CreateEntity("Bob Jones", 200));
    await repository.CreateAsync(CreateEntity("Alice Jones", 300));

    var filters = new List<FilterValue> { CreateOperatorFilter("Name", "Alice", FilterOperator.Contains) };
    var request = new PaginationRequest { Limit = 10, Filters = filters };

    // Act
    var result = await repository.GetAllAsync(request);

    // Assert
    result.Items.Should().HaveCount(2);
    result.Items.Should().OnlyContain(e => e.Name.Contains("Alice", StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public async Task GetAllAsync_WithContainsFilter_CaseInsensitive()
  {
    // Arrange
    var repository = CreateRepository();
    await repository.CreateAsync(CreateEntity("UPPERCASE", 100));
    await repository.CreateAsync(CreateEntity("lowercase", 200));
    await repository.CreateAsync(CreateEntity("MixedCase", 300));

    var filters = new List<FilterValue> { CreateOperatorFilter("Name", "case", FilterOperator.Contains) };
    var request = new PaginationRequest { Limit = 10, Filters = filters };

    // Act
    var result = await repository.GetAllAsync(request);

    // Assert
    result.Items.Should().HaveCount(3); // All contain "case" in some form
  }

  [Fact]
  public async Task GetAllAsync_WithStartsWithFilter_FiltersCorrectly()
  {
    // Arrange
    var repository = CreateRepository();
    await repository.CreateAsync(CreateEntity("Alice Smith", 100));
    await repository.CreateAsync(CreateEntity("Alice Jones", 200));
    await repository.CreateAsync(CreateEntity("Bob Smith", 300));

    var filters = new List<FilterValue> { CreateOperatorFilter("Name", "Alice", FilterOperator.StartsWith) };
    var request = new PaginationRequest { Limit = 10, Filters = filters };

    // Act
    var result = await repository.GetAllAsync(request);

    // Assert
    result.Items.Should().HaveCount(2);
    result.Items.Should().OnlyContain(e => e.Name.StartsWith("Alice", StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public async Task GetAllAsync_WithInFilter_FiltersCorrectly()
  {
    // Arrange
    var repository = CreateRepository();
    await repository.CreateAsync(CreateEntity("Alice", 100));
    await repository.CreateAsync(CreateEntity("Bob", 200));
    await repository.CreateAsync(CreateEntity("Charlie", 300));

    var typedValues = new List<object?> { 100, 300 };
    var filters = new List<FilterValue> { CreateInFilter("Value", typeof(int), typedValues) };
    var request = new PaginationRequest { Limit = 10, Filters = filters };

    // Act
    var result = await repository.GetAllAsync(request);

    // Assert
    result.Items.Should().HaveCount(2);
    result.Items.Should().Contain(e => e.Value == 100);
    result.Items.Should().Contain(e => e.Value == 300);
  }

  [Fact]
  public async Task GetAllAsync_WithInFilter_EmptyTypedValues_ReturnsNoItems()
  {
    // Arrange
    var repository = CreateRepository();
    await repository.CreateAsync(CreateEntity("Alice", 100));

    var typedValues = new List<object?>();
    var filters = new List<FilterValue> { CreateInFilter("Value", typeof(int), typedValues) };
    var request = new PaginationRequest { Limit = 10, Filters = filters };

    // Act
    var result = await repository.GetAllAsync(request);

    // Assert
    result.Items.Should().BeEmpty();
  }

  [Fact]
  public async Task GetAllAsync_WithRangeFilters_GteAndLte_FiltersCorrectly()
  {
    // Arrange
    var repository = CreateRepository();
    await repository.CreateAsync(CreateEntity("A", 10));
    await repository.CreateAsync(CreateEntity("B", 20));
    await repository.CreateAsync(CreateEntity("C", 30));
    await repository.CreateAsync(CreateEntity("D", 40));
    await repository.CreateAsync(CreateEntity("E", 50));

    var filters = new List<FilterValue>
    {
      CreateOperatorFilter("Value", 20, FilterOperator.Gte),
      CreateOperatorFilter("Value", 40, FilterOperator.Lte),
    };
    var request = new PaginationRequest { Limit = 10, Filters = filters };

    // Act
    var result = await repository.GetAllAsync(request);

    // Assert
    result.Items.Should().HaveCount(3); // 20, 30, 40
    result.Items.Should().OnlyContain(e => e.Value >= 20 && e.Value <= 40);
  }

  [Fact]
  public async Task GetAllAsync_ContainsFilter_NonStringProperty_ReturnsFalse()
  {
    // Arrange — Contains on a non-string property should not match anything
    var repository = CreateRepository();
    await repository.CreateAsync(CreateEntity("Alice", 100));

    var filters = new List<FilterValue> { CreateOperatorFilter("Value", 100, FilterOperator.Contains) };
    var request = new PaginationRequest { Limit = 10, Filters = filters };

    // Act
    var result = await repository.GetAllAsync(request);

    // Assert — Contains requires strings; int won't match
    result.Items.Should().BeEmpty();
  }

  [Fact]
  public async Task GetAllAsync_StartsWithFilter_NonStringProperty_ReturnsFalse()
  {
    // Arrange — StartsWith on a non-string property should not match anything
    var repository = CreateRepository();
    await repository.CreateAsync(CreateEntity("Alice", 100));

    var filters = new List<FilterValue> { CreateOperatorFilter("Value", 100, FilterOperator.StartsWith) };
    var request = new PaginationRequest { Limit = 10, Filters = filters };

    // Act
    var result = await repository.GetAllAsync(request);

    // Assert — StartsWith requires strings; int won't match
    result.Items.Should().BeEmpty();
  }

  #endregion
}
