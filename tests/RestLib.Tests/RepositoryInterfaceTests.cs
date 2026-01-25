using System.Text.Json;
using FluentAssertions;
using RestLib.Abstractions;
using RestLib.Pagination;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Tests for Story 1.2: Repository Interface Contract
/// Verifies that the IRepository interface contract is properly defined.
/// </summary>
public class RepositoryInterfaceTests
{
  #region Interface Definition Tests

  [Fact]
  public void IRepository_IsGenericInterface()
  {
    // Assert
    var type = typeof(IRepository<,>);
    type.IsInterface.Should().BeTrue();
    type.IsGenericType.Should().BeTrue();
    type.GetGenericArguments().Should().HaveCount(2);
  }

  [Fact]
  public void IRepository_HasEntityConstraint()
  {
    // Assert - TEntity must be a class
    var type = typeof(IRepository<,>);
    var entityParam = type.GetGenericArguments()[0];
    var constraints = entityParam.GetGenericParameterConstraints();

    // class constraint is represented as having ReferenceTypeConstraint
    entityParam.GenericParameterAttributes
        .HasFlag(System.Reflection.GenericParameterAttributes.ReferenceTypeConstraint)
        .Should().BeTrue("TEntity should have 'class' constraint");
  }

  [Fact]
  public void IRepository_HasAllCrudMethods()
  {
    // Arrange
    var type = typeof(IRepository<TestEntity, Guid>);
    var methods = type.GetMethods();

    // Assert
    methods.Should().Contain(m => m.Name == "GetByIdAsync");
    methods.Should().Contain(m => m.Name == "GetAllAsync");
    methods.Should().Contain(m => m.Name == "CreateAsync");
    methods.Should().Contain(m => m.Name == "UpdateAsync");
    methods.Should().Contain(m => m.Name == "PatchAsync");
    methods.Should().Contain(m => m.Name == "DeleteAsync");
  }

  #endregion

  #region Method Signature Tests

  [Fact]
  public void GetByIdAsync_ReturnsNullableEntity()
  {
    // Arrange
    var method = typeof(IRepository<TestEntity, Guid>).GetMethod("GetByIdAsync");

    // Assert
    method.Should().NotBeNull();
    method!.ReturnType.Should().Be(typeof(Task<TestEntity?>));
  }

  [Fact]
  public void GetByIdAsync_AcceptsKeyAndCancellationToken()
  {
    // Arrange
    var method = typeof(IRepository<TestEntity, Guid>).GetMethod("GetByIdAsync");
    var parameters = method!.GetParameters();

    // Assert
    parameters.Should().HaveCount(2);
    parameters[0].ParameterType.Should().Be(typeof(Guid));
    parameters[1].ParameterType.Should().Be(typeof(CancellationToken));
    parameters[1].HasDefaultValue.Should().BeTrue();
  }

  [Fact]
  public void GetAllAsync_ReturnsPagedResult()
  {
    // Arrange
    var method = typeof(IRepository<TestEntity, Guid>).GetMethod("GetAllAsync");

    // Assert
    method.Should().NotBeNull();
    method!.ReturnType.Should().Be(typeof(Task<PagedResult<TestEntity>>));
  }

  [Fact]
  public void GetAllAsync_AcceptsPaginationRequest()
  {
    // Arrange
    var method = typeof(IRepository<TestEntity, Guid>).GetMethod("GetAllAsync");
    var parameters = method!.GetParameters();

    // Assert
    parameters.Should().HaveCount(2);
    parameters[0].ParameterType.Should().Be(typeof(PaginationRequest));
    parameters[1].ParameterType.Should().Be(typeof(CancellationToken));
  }

  [Fact]
  public void CreateAsync_ReturnsEntity()
  {
    // Arrange
    var method = typeof(IRepository<TestEntity, Guid>).GetMethod("CreateAsync");

    // Assert
    method.Should().NotBeNull();
    method!.ReturnType.Should().Be(typeof(Task<TestEntity>));
  }

  [Fact]
  public void UpdateAsync_ReturnsNullableEntity()
  {
    // Arrange
    var method = typeof(IRepository<TestEntity, Guid>).GetMethod("UpdateAsync");

    // Assert
    method.Should().NotBeNull();
    method!.ReturnType.Should().Be(typeof(Task<TestEntity?>));
  }

  [Fact]
  public void PatchAsync_AcceptsJsonElement()
  {
    // Arrange
    var method = typeof(IRepository<TestEntity, Guid>).GetMethod("PatchAsync");
    var parameters = method!.GetParameters();

    // Assert
    parameters.Should().HaveCount(3);
    parameters[0].ParameterType.Should().Be(typeof(Guid));
    parameters[1].ParameterType.Should().Be(typeof(JsonElement));
    parameters[2].ParameterType.Should().Be(typeof(CancellationToken));
  }

  [Fact]
  public void DeleteAsync_ReturnsBool()
  {
    // Arrange
    var method = typeof(IRepository<TestEntity, Guid>).GetMethod("DeleteAsync");

    // Assert
    method.Should().NotBeNull();
    method!.ReturnType.Should().Be(typeof(Task<bool>));
  }

  #endregion

  #region Pagination Types Tests

  [Fact]
  public void PaginationRequest_HasCursorProperty()
  {
    // Assert
    var property = typeof(PaginationRequest).GetProperty("Cursor");
    property.Should().NotBeNull();
    property!.PropertyType.Should().Be(typeof(string));
  }

  [Fact]
  public void PaginationRequest_HasLimitWithDefault20()
  {
    // Arrange
    var request = new PaginationRequest();

    // Assert
    request.Limit.Should().Be(20);
  }

  [Fact]
  public void PagedResult_HasRequiredProperties()
  {
    // Assert
    var type = typeof(PagedResult<TestEntity>);
    type.GetProperty("Items").Should().NotBeNull();
    type.GetProperty("NextCursor").Should().NotBeNull();
    type.GetProperty("HasMore").Should().NotBeNull();
  }

  [Fact]
  public void PagedResult_HasMore_WhenNextCursorExists()
  {
    // Arrange
    var result = new PagedResult<TestEntity>
    {
      Items = new List<TestEntity>(),
      NextCursor = "abc123"
    };

    // Assert
    result.HasMore.Should().BeTrue();
  }

  [Fact]
  public void PagedResult_HasNoMore_WhenNextCursorIsNull()
  {
    // Arrange
    var result = new PagedResult<TestEntity>
    {
      Items = new List<TestEntity>(),
      NextCursor = null
    };

    // Assert
    result.HasMore.Should().BeFalse();
  }

  #endregion

  // Simple test entity for type checking
  private class TestEntity
  {
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
  }
}
