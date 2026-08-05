using FluentAssertions;
using RestLib.Abstractions;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

/// <summary>
/// Tests the adapter-neutral patch-validation exception contract.
/// </summary>
public class EfCorePatchValidationExceptionTests
{
    [Fact]
    public void EfCorePatchValidationException_IsPatchValidationException()
    {
        // Arrange
        var exception = new EfCorePatchValidationException("Invalid patch.");

        // Act
        var result = exception as PatchValidationException;

        // Assert
        result.Should().BeSameAs(exception);
    }
}
