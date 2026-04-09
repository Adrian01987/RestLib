using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using RestLib.Abstractions;
using RestLib.Pagination;
using RestLib.Responses;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Tests for Story 10.1: Data Annotation Validation
/// Verifies that entities are validated using Data Annotations.
/// </summary>
[Trait("Type", "Integration")]
[Trait("Feature", "Validation")]
public class DataAnnotationValidationTests : IDisposable
{
    private IHost? _host;
    private HttpClient? _client;
    private ValidatedEntityRepository? _repository;

    private void SetupHost(bool enableValidation = true)
    {
        _repository = new ValidatedEntityRepository();

        (_host, _client) = new TestHostBuilder<ValidatedEntity, Guid>(_repository, "/api/validated")
            .WithOptions(options =>
            {
                options.EnableValidation = enableValidation;
            })
            .WithEndpoint(config =>
            {
                config.AllowAnonymous();
            })
            .Build();
    }

    public void Dispose()
    {
        _client?.Dispose();
        _host?.Dispose();
    }

    #region Acceptance Criteria: Data Annotations validated

    [Fact]
    public async Task Create_WithMissingRequiredField_Returns_ValidationError()
    {
        // Arrange
        SetupHost();
        var entity = new { unit_price = 10.00 }; // Missing required 'name'

        // Act
        var response = await _client!.PostAsJsonAsync("/api/validated", entity);

        // Assert
        var problem = await response.ShouldBeProblemDetails(
            HttpStatusCode.BadRequest,
            ProblemTypes.ValidationFailed);
        problem.Errors.Should().ContainKey("name");
    }

    [Fact]
    public async Task Create_WithStringTooLong_Returns_ValidationError()
    {
        // Arrange
        SetupHost();
        var entity = new
        {
            name = new string('a', 101), // Exceeds MaxLength(100)
            unit_price = 10.00
        };

        // Act
        var response = await _client!.PostAsJsonAsync("/api/validated", entity);

        // Assert
        var problem = await response.ShouldBeProblemDetails(
            HttpStatusCode.BadRequest,
            ProblemTypes.ValidationFailed);
        problem.Errors.Should().ContainKey("name");
    }

    [Fact]
    public async Task Create_WithValueOutOfRange_Returns_ValidationError()
    {
        // Arrange
        SetupHost();
        var entity = new
        {
            name = "Test Product",
            unit_price = -5.00 // Range(0, double.MaxValue)
        };

        // Act
        var response = await _client!.PostAsJsonAsync("/api/validated", entity);

        // Assert
        var problem = await response.ShouldBeProblemDetails(
            HttpStatusCode.BadRequest,
            ProblemTypes.ValidationFailed);
        problem.Errors.Should().ContainKey("unit_price");
    }

    [Fact]
    public async Task Create_WithInvalidEmail_Returns_ValidationError()
    {
        // Arrange
        SetupHost();
        var entity = new
        {
            name = "Test Product",
            unit_price = 10.00,
            contact_email = "not-an-email" // Invalid email
        };

        // Act
        var response = await _client!.PostAsJsonAsync("/api/validated", entity);

        // Assert
        var problem = await response.ShouldBeProblemDetails(
            HttpStatusCode.BadRequest,
            ProblemTypes.ValidationFailed);
        problem.Errors.Should().ContainKey("contact_email");
    }

    [Fact]
    public async Task Create_WithValidEntity_Succeeds()
    {
        // Arrange
        SetupHost();
        var entity = new
        {
            name = "Valid Product",
            unit_price = 10.00,
            contact_email = "test@example.com"
        };

        // Act
        var response = await _client!.PostAsJsonAsync("/api/validated", entity);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Update_WithMissingRequiredField_Returns_ValidationError()
    {
        // Arrange
        SetupHost();
        var existingId = Guid.NewGuid();
        _repository!.Seed(new ValidatedEntity
        {
            Id = existingId,
            Name = "Existing",
            UnitPrice = 10.00m
        });

        var entity = new { unit_price = 20.00 }; // Missing required 'name'

        // Act
        var response = await _client!.PutAsJsonAsync($"/api/validated/{existingId}", entity);

        // Assert
        var problem = await response.ShouldBeProblemDetails(
            HttpStatusCode.BadRequest,
            ProblemTypes.ValidationFailed);
        problem.Errors.Should().ContainKey("name");
    }

    [Fact]
    public async Task Update_WithValidEntity_Succeeds()
    {
        // Arrange
        SetupHost();
        var existingId = Guid.NewGuid();
        _repository!.Seed(new ValidatedEntity
        {
            Id = existingId,
            Name = "Existing",
            UnitPrice = 10.00m
        });

        var entity = new
        {
            name = "Updated Product",
            unit_price = 20.00
        };

        // Act
        var response = await _client!.PutAsJsonAsync($"/api/validated/{existingId}", entity);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region Acceptance Criteria: Returns 400 on failure

    [Fact]
    public async Task ValidationFailure_Returns_400BadRequest()
    {
        // Arrange
        SetupHost();
        var entity = new { unit_price = 10.00 }; // Missing required field

        // Act
        var response = await _client!.PostAsJsonAsync("/api/validated", entity);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ValidationFailure_Returns_ProblemDetails_ContentType()
    {
        // Arrange
        SetupHost();
        var entity = new { unit_price = 10.00 };

        // Act
        var response = await _client!.PostAsJsonAsync("/api/validated", entity);

        // Assert
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task ValidationFailure_Returns_CorrectProblemType()
    {
        // Arrange
        SetupHost();
        var entity = new { unit_price = 10.00 };

        // Act
        var response = await _client!.PostAsJsonAsync("/api/validated", entity);

        // Assert
        await response.ShouldBeProblemDetails(
            HttpStatusCode.BadRequest,
            ProblemTypes.ValidationFailed,
            expectedTitle: "Validation Failed");
    }

    #endregion

    #region Acceptance Criteria: All errors returned

    [Fact]
    public async Task MultipleValidationErrors_ReturnsAllErrors()
    {
        // Arrange
        SetupHost();
        var entity = new
        {
            // Missing 'name' (Required)
            unit_price = -5.00, // Invalid range
            contact_email = "not-an-email" // Invalid email
        };

        // Act
        var response = await _client!.PostAsJsonAsync("/api/validated", entity);

        // Assert
        var problem = await response.ShouldBeProblemDetails(
            HttpStatusCode.BadRequest,
            ProblemTypes.ValidationFailed);
        problem.Errors.Should().NotBeNull();
        problem.Errors!.Should().HaveCountGreaterThanOrEqualTo(2); // At least name and one other
        problem.Errors.Should().ContainKey("name");
        // Price or email error should also be present
    }

    [Fact]
    public async Task ValidationError_ContainsDetailMessage()
    {
        // Arrange
        SetupHost();
        var entity = new { unit_price = 10.00 };

        // Act
        var response = await _client!.PostAsJsonAsync("/api/validated", entity);

        // Assert
        var problem = await response.ShouldBeProblemDetails(
            HttpStatusCode.BadRequest,
            ProblemTypes.ValidationFailed,
            expectedDetail: "validation");
    }

    #endregion

    #region Acceptance Criteria: Field names in snake_case

    [Fact]
    public async Task ValidationError_FieldName_IsSnakeCase_ForSingleWord()
    {
        // Arrange
        SetupHost();
        var entity = new { unit_price = 10.00 }; // Missing 'name'

        // Act
        var response = await _client!.PostAsJsonAsync("/api/validated", entity);

        // Assert
        var problem = await response.ShouldBeProblemDetails(
            HttpStatusCode.BadRequest,
            ProblemTypes.ValidationFailed);
        problem.Errors.Should().ContainKey("name"); // Single word stays lowercase
    }

    [Fact]
    public async Task ValidationError_FieldName_IsSnakeCase_ForMultipleWords()
    {
        // Arrange
        SetupHost();
        var entity = new
        {
            name = "Test",
            unit_price = -5.00 // Invalid - property is UnitPrice
        };

        // Act
        var response = await _client!.PostAsJsonAsync("/api/validated", entity);

        // Assert
        var problem = await response.ShouldBeProblemDetails(
            HttpStatusCode.BadRequest,
            ProblemTypes.ValidationFailed);
        problem.Errors.Should().ContainKey("unit_price"); // UnitPrice -> unit_price
    }

    [Fact]
    public async Task ValidationError_FieldName_IsSnakeCase_ForComplexPropertyName()
    {
        // Arrange
        SetupHost();
        var entity = new
        {
            name = "Test",
            unit_price = 10.00,
            contact_email = "invalid-email" // ContactEmail -> contact_email
        };

        // Act
        var response = await _client!.PostAsJsonAsync("/api/validated", entity);

        // Assert
        var problem = await response.ShouldBeProblemDetails(
            HttpStatusCode.BadRequest,
            ProblemTypes.ValidationFailed);
        problem.Errors.Should().ContainKey("contact_email"); // ContactEmail -> contact_email
    }

    #endregion

    #region Validation can be disabled

    [Fact]
    public async Task Create_WithValidationDisabled_DoesNotValidate()
    {
        // Arrange
        SetupHost(enableValidation: false);
        var entity = new { unit_price = 10.00 }; // Missing required 'name'

        // Act
        var response = await _client!.PostAsJsonAsync("/api/validated", entity);

        // Assert
        // Should succeed because validation is disabled
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    #endregion

    #region PATCH validation

    [Fact]
    public async Task Patch_ThatResultsInInvalidEntity_Returns_ValidationError()
    {
        // Arrange
        SetupHost();
        var existingId = Guid.NewGuid();
        _repository!.Seed(new ValidatedEntity
        {
            Id = existingId,
            Name = "Existing Product",
            UnitPrice = 10.00m
        });

        // Patch to set name to empty (which would fail validation if entity validates on result)
        var patch = new { name = "" }; // Setting to empty should fail Required validation

        // Act
        var response = await _client!.PatchAsJsonAsync($"/api/validated/{existingId}", patch);

        // Assert
        var problem = await response.ShouldBeProblemDetails(
            HttpStatusCode.BadRequest,
            ProblemTypes.ValidationFailed);
        problem.Errors.Should().ContainKey("name");
    }

    [Fact]
    [Trait("Category", "Story10.1")]
    public async Task Patch_ThatResultsInInvalidEntity_DoesNotPersistInvalidData()
    {
        // Arrange
        SetupHost();
        var existingId = Guid.NewGuid();
        _repository!.Seed(new ValidatedEntity
        {
            Id = existingId,
            Name = "Existing Product",
            UnitPrice = 10.00m
        });

        var patch = new { name = "" }; // Setting to empty should fail Required validation

        // Act
        var response = await _client!.PatchAsJsonAsync($"/api/validated/{existingId}", patch);

        // Assert — response should be 400
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Assert — repository should still have the original valid entity
        var getResponse = await _client!.GetAsync($"/api/validated/{existingId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var entity = await getResponse.Content.ReadFromJsonAsync<ValidatedEntity>();
        entity.Should().NotBeNull();
        entity!.Name.Should().Be("Existing Product");
    }

    [Fact]
    public async Task Patch_ThatKeepsEntityValid_Succeeds()
    {
        // Arrange
        SetupHost();
        var existingId = Guid.NewGuid();
        _repository!.Seed(new ValidatedEntity
        {
            Id = existingId,
            Name = "Existing Product",
            UnitPrice = 10.00m
        });

        var patch = new { unit_price = 25.00 }; // Valid change

        // Act
        var response = await _client!.PatchAsJsonAsync($"/api/validated/{existingId}", patch);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region Instance field in validation errors

    [Fact]
    public async Task ValidationError_IncludesInstance()
    {
        // Arrange
        SetupHost();
        var entity = new { unit_price = 10.00 };

        // Act
        var response = await _client!.PostAsJsonAsync("/api/validated", entity);

        // Assert
        var problem = await response.ShouldBeProblemDetails(
            HttpStatusCode.BadRequest,
            ProblemTypes.ValidationFailed);
        problem.Instance.Should().Be("/api/validated");
    }

    #endregion
}

#region Test Entities and Repository

/// <summary>
/// Entity with Data Annotation validations for testing.
/// </summary>
public class ValidatedEntity
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "The Name field is required.")]
    [MaxLength(100, ErrorMessage = "The Name field must not exceed 100 characters.")]
    public string Name { get; set; } = string.Empty;

    [Range(0, double.MaxValue, ErrorMessage = "The UnitPrice field must be a positive number.")]
    public decimal UnitPrice { get; set; }

    [EmailAddress(ErrorMessage = "The ContactEmail field is not a valid email address.")]
    public string? ContactEmail { get; set; }

    public string? Description { get; set; }
}

/// <summary>
/// Repository for ValidatedEntity.
/// </summary>
public class ValidatedEntityRepository : IRepository<ValidatedEntity, Guid>
{
    private readonly Dictionary<Guid, ValidatedEntity> _store = new();

    public Task<ValidatedEntity?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        _store.TryGetValue(id, out var entity);
        return Task.FromResult(entity);
    }

    public Task<PagedResult<ValidatedEntity>> GetAllAsync(PaginationRequest pagination, CancellationToken ct = default)
    {
        var items = _store.Values.Take(pagination.Limit).ToList();
        return Task.FromResult(new PagedResult<ValidatedEntity>
        {
            Items = items,
            NextCursor = null
        });
    }

    public Task<ValidatedEntity> CreateAsync(ValidatedEntity entity, CancellationToken ct = default)
    {
        if (entity.Id == Guid.Empty)
            entity.Id = Guid.NewGuid();

        _store[entity.Id] = entity;
        return Task.FromResult(entity);
    }

    public Task<ValidatedEntity?> UpdateAsync(Guid id, ValidatedEntity entity, CancellationToken ct = default)
    {
        if (!_store.ContainsKey(id))
            return Task.FromResult<ValidatedEntity?>(null);

        entity.Id = id;
        _store[id] = entity;
        return Task.FromResult<ValidatedEntity?>(entity);
    }

    public Task<ValidatedEntity?> PatchAsync(Guid id, JsonElement patchDocument, CancellationToken ct = default)
    {
        if (!_store.TryGetValue(id, out var existing))
            return Task.FromResult<ValidatedEntity?>(null);

        // Simple merge patch implementation
        if (patchDocument.TryGetProperty("name", out var nameElement))
        {
            var newName = nameElement.GetString();
            existing.Name = newName ?? string.Empty;
        }

        if (patchDocument.TryGetProperty("unit_price", out var priceElement))
            existing.UnitPrice = priceElement.GetDecimal();

        if (patchDocument.TryGetProperty("contact_email", out var emailElement))
            existing.ContactEmail = emailElement.GetString();

        if (patchDocument.TryGetProperty("description", out var descElement))
            existing.Description = descElement.GetString();

        _store[id] = existing;
        return Task.FromResult<ValidatedEntity?>(existing);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        return Task.FromResult(_store.Remove(id));
    }

    public void Seed(params ValidatedEntity[] entities)
    {
        foreach (var entity in entities)
        {
            _store[entity.Id] = entity;
        }
    }

    public void Clear() => _store.Clear();
}

#endregion
