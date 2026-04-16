using FluentAssertions;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

/// <summary>
/// Tests provider-specific constraint classification behavior.
/// </summary>
public class ConstraintViolationClassifierTests
{
    [Fact]
    public void Classify_SqliteUniqueExtendedCode_ReturnsUniqueConstraint()
    {
        // Arrange

        // Act
        var result = ConstraintViolationClassifier.Classify(
            providerTypeName: "Microsoft.Data.Sqlite.SqliteException",
            sqliteErrorCode: 19,
            sqliteExtendedErrorCode: 2067,
            sqlServerNumber: null,
            postgresSqlState: null,
            message: "UNIQUE constraint failed");

        // Assert
        result.Should().Be(EfCoreConstraintType.UniqueConstraint);
    }

    [Fact]
    public void Classify_SqliteForeignKeyExtendedCode_ReturnsForeignKeyConstraint()
    {
        // Arrange

        // Act
        var result = ConstraintViolationClassifier.Classify(
            providerTypeName: "Microsoft.Data.Sqlite.SqliteException",
            sqliteErrorCode: 19,
            sqliteExtendedErrorCode: 787,
            sqlServerNumber: null,
            postgresSqlState: null,
            message: "FOREIGN KEY constraint failed");

        // Assert
        result.Should().Be(EfCoreConstraintType.ForeignKeyConstraint);
    }

    [Fact]
    public void Classify_SqlServerUniqueNumber_ReturnsUniqueConstraint()
    {
        // Arrange

        // Act
        var result = ConstraintViolationClassifier.Classify(
            providerTypeName: "Microsoft.Data.SqlClient.SqlException",
            sqliteErrorCode: null,
            sqliteExtendedErrorCode: null,
            sqlServerNumber: 2627,
            postgresSqlState: null,
            message: "Violation of PRIMARY KEY constraint");

        // Assert
        result.Should().Be(EfCoreConstraintType.UniqueConstraint);
    }

    [Fact]
    public void Classify_SqlServerForeignKeyNumber_ReturnsForeignKeyConstraint()
    {
        // Arrange

        // Act
        var result = ConstraintViolationClassifier.Classify(
            providerTypeName: "Microsoft.Data.SqlClient.SqlException",
            sqliteErrorCode: null,
            sqliteExtendedErrorCode: null,
            sqlServerNumber: 547,
            postgresSqlState: null,
            message: "The INSERT statement conflicted with the FOREIGN KEY constraint");

        // Assert
        result.Should().Be(EfCoreConstraintType.ForeignKeyConstraint);
    }

    [Fact]
    public void Classify_PostgresUniqueSqlState_ReturnsUniqueConstraint()
    {
        // Arrange

        // Act
        var result = ConstraintViolationClassifier.Classify(
            providerTypeName: "Npgsql.PostgresException",
            sqliteErrorCode: null,
            sqliteExtendedErrorCode: null,
            sqlServerNumber: null,
            postgresSqlState: "23505",
            message: "duplicate key value violates unique constraint");

        // Assert
        result.Should().Be(EfCoreConstraintType.UniqueConstraint);
    }

    [Fact]
    public void Classify_PostgresForeignKeySqlState_ReturnsForeignKeyConstraint()
    {
        // Arrange

        // Act
        var result = ConstraintViolationClassifier.Classify(
            providerTypeName: "Npgsql.PostgresException",
            sqliteErrorCode: null,
            sqliteExtendedErrorCode: null,
            sqlServerNumber: null,
            postgresSqlState: "23503",
            message: "insert or update violates foreign key constraint");

        // Assert
        result.Should().Be(EfCoreConstraintType.ForeignKeyConstraint);
    }

    [Fact]
    public void Classify_UnknownProviderWithUniqueMessage_FallsBackToMessageParsing()
    {
        // Arrange

        // Act
        var result = ConstraintViolationClassifier.Classify(
            providerTypeName: "Custom.Provider.Exception",
            sqliteErrorCode: null,
            sqliteExtendedErrorCode: null,
            sqlServerNumber: null,
            postgresSqlState: null,
            message: "UNIQUE constraint failed");

        // Assert
        result.Should().Be(EfCoreConstraintType.UniqueConstraint);
    }

    [Fact]
    public void Classify_UnknownProviderWithNoKnownSignal_ReturnsUnknown()
    {
        // Arrange

        // Act
        var result = ConstraintViolationClassifier.Classify(
            providerTypeName: "Custom.Provider.Exception",
            sqliteErrorCode: null,
            sqliteExtendedErrorCode: null,
            sqlServerNumber: null,
            postgresSqlState: null,
            message: "Some unrelated database error");

        // Assert
        result.Should().Be(EfCoreConstraintType.Unknown);
    }
}
