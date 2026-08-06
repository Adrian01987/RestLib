using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RestLib.Configuration;
using RestLib.FieldSelection;
using RestLib.Filtering;
using RestLib.Responses;
using RestLib.Search;
using RestLib.Sorting;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Characterization tests for the centralized Problem Details catalog and public result API.
/// </summary>
[Trait("Type", "Unit")]
[Trait("Feature", "ProblemDetails")]
public class ProblemDetailsCatalogTests
{
    [Fact]
    [Trait("Category", "Story3.3")]
    public async Task Responder_EndpointOptions_FlowThroughSingleResultPipeline()
    {
        // Arrange
        var services = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
        };
        httpContext.Response.Body = new MemoryStream();
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var options = new RestLibOptions
        {
            ProblemTypeBaseUri = new Uri("https://api.example.com"),
        };
        var responder = ProblemDetailsResult.CreateResponder(jsonOptions, logger: null, options: options);

        // Act
        await responder.Create(ProblemDetailsFactory.BadRequest("bad", "/api/items"))
            .ExecuteAsync(httpContext);
        httpContext.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(httpContext.Response.Body);

        // Assert
        httpContext.Response.ContentType.Should().StartWith("application/problem+json");
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        document.RootElement.GetProperty("type").GetString()
            .Should().Be("https://api.example.com/problems/bad-request");
        document.RootElement.GetProperty("instance").GetString().Should().Be("/api/items");
    }

    [Fact]
    [Trait("Category", "Story3.3")]
    public void Catalog_AllDescriptors_CreateExpectedInvariantMetadata()
    {
        // Arrange
        var cases = new[]
        {
            (ProblemCatalog.NotFound, ProblemTypes.NotFound, "Resource Not Found", StatusCodes.Status404NotFound, (string?)null),
            (ProblemCatalog.ValidationFailed, ProblemTypes.ValidationFailed, "Validation Failed", StatusCodes.Status400BadRequest, "One or more validation errors occurred."),
            (ProblemCatalog.BadRequest, ProblemTypes.BadRequest, "Bad Request", StatusCodes.Status400BadRequest, (string?)null),
            (ProblemCatalog.InvalidCursor, ProblemTypes.InvalidCursor, "Invalid Cursor", StatusCodes.Status400BadRequest, "The provided cursor is not a valid pagination cursor."),
            (ProblemCatalog.InvalidLimit, ProblemTypes.InvalidLimit, "Invalid Limit", StatusCodes.Status400BadRequest, (string?)null),
            (ProblemCatalog.InvalidFilter, ProblemTypes.InvalidFilter, "Invalid Filter Value", StatusCodes.Status400BadRequest, (string?)null),
            (ProblemCatalog.InvalidSort, ProblemTypes.InvalidSort, "Invalid Sort Parameter", StatusCodes.Status400BadRequest, (string?)null),
            (ProblemCatalog.InvalidFields, ProblemTypes.InvalidFields, "Invalid Field Selection", StatusCodes.Status400BadRequest, (string?)null),
            (ProblemCatalog.InvalidSearch, ProblemTypes.InvalidSearch, "Invalid Search Parameter", StatusCodes.Status400BadRequest, (string?)null),
            (ProblemCatalog.InvalidBatchRequest, ProblemTypes.InvalidBatchRequest, "Invalid Batch Request", StatusCodes.Status400BadRequest, (string?)null),
            (ProblemCatalog.BatchSizeExceeded, ProblemTypes.BatchSizeExceeded, "Batch Size Exceeded", StatusCodes.Status400BadRequest, (string?)null),
            (ProblemCatalog.BatchActionNotEnabled, ProblemTypes.BatchActionNotEnabled, "Batch Action Not Enabled", StatusCodes.Status400BadRequest, (string?)null),
            (ProblemCatalog.Conflict, ProblemTypes.Conflict, "Conflict", StatusCodes.Status409Conflict, (string?)null),
            (ProblemCatalog.InsufficientStock, ProblemTypes.InsufficientStock, "Insufficient Stock", StatusCodes.Status409Conflict, (string?)null),
            (ProblemCatalog.InvalidStatusTransition, ProblemTypes.InvalidStatusTransition, "Invalid Status Transition", StatusCodes.Status409Conflict, (string?)null),
            (ProblemCatalog.PreconditionFailed, ProblemTypes.PreconditionFailed, "Precondition Failed", StatusCodes.Status412PreconditionFailed, (string?)null),
            (ProblemCatalog.ConditionalWriteNotSupported, ProblemTypes.ConditionalWriteNotSupported, "Conditional Write Not Supported", StatusCodes.Status501NotImplemented, (string?)null),
            (ProblemCatalog.InternalError, ProblemTypes.InternalError, "Internal Server Error", StatusCodes.Status500InternalServerError, "An unexpected error occurred."),
            (ProblemCatalog.HookShortCircuit, ProblemTypes.HookShortCircuit, "Hook Short-Circuit", StatusCodes.Status500InternalServerError, "The operation was short-circuited by a hook."),
        };

        // Act & Assert
        foreach (var (descriptor, type, title, status, detail) in cases)
        {
            var problem = descriptor.Create();
            problem.Type.Should().Be(type);
            problem.Title.Should().Be(title);
            problem.Status.Should().Be(status);
            problem.Detail.Should().Be(detail);
        }
    }

    [Fact]
    [Trait("Category", "Story3.3")]
    public void PublicResultCompatibilityMethods_AllRemainCallable()
    {
        // Arrange
        var validationErrors = new Dictionary<string, string[]> { ["field"] = ["bad"] };
        var filterErrors = new[]
        {
            new FilterValidationError
            {
                ParameterName = "field",
                ProvidedValue = "bad",
                ExpectedType = typeof(int),
                Message = "bad"
            }
        };
        var sortErrors = new[] { new SortValidationError { Field = "field", Message = "bad" } };
        var fieldErrors = new[] { new FieldSelectionValidationError { Field = "field", Message = "bad" } };
        var searchErrors = new[]
        {
            new SearchValidationError { ParameterName = "q", ProvidedValue = "bad", Message = "bad" }
        };

        // Act
        var results = new IResult[]
        {
            ProblemDetailsResult.Create(ProblemDetailsFactory.BadRequest("bad")),
            ProblemDetailsResult.NotFound("Entity", 1),
            ProblemDetailsResult.ValidationFailed(validationErrors),
            ProblemDetailsResult.BadRequest("bad"),
            ProblemDetailsResult.InvalidCursor("bad"),
            ProblemDetailsResult.InvalidLimit(0, 1, 100),
            ProblemDetailsResult.InvalidFilters(filterErrors),
            ProblemDetailsResult.InvalidSort(sortErrors),
            ProblemDetailsResult.InvalidFields(fieldErrors),
            ProblemDetailsResult.InvalidSearch(searchErrors),
            ProblemDetailsResult.InvalidBatchRequest("bad"),
            ProblemDetailsResult.BatchSizeExceeded(2, 1),
            ProblemDetailsResult.BatchActionNotEnabled("patch", ["create"]),
            ProblemDetailsResult.Conflict("bad"),
            ProblemDetailsResult.InsufficientStock("bad", "1", 2, 1),
            ProblemDetailsResult.InvalidStatusTransition("new", "done"),
            ProblemDetailsResult.PreconditionFailed("bad"),
            ProblemDetailsResult.ConditionalWriteNotSupported("bad"),
            ProblemDetailsResult.InternalError(),
            ProblemDetailsResult.HookShortCircuit(StatusCodes.Status403Forbidden),
        };

        // Assert
        results.Should().AllSatisfy(result => result.Should().NotBeNull());
    }
}
