using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace RestLib.EntityFrameworkCore;

/// <summary>
/// Classifies provider-specific database constraint violations.
/// </summary>
internal static class ConstraintViolationClassifier
{
    private const int SqliteConstraintCode = 19;
    private const int SqliteUniqueExtendedCode = 2067;
    private const int SqlitePrimaryKeyExtendedCode = 1555;
    private const int SqliteForeignKeyExtendedCode = 787;
    private const int SqlServerUniqueConstraintCode = 2627;
    private const int SqlServerUniqueIndexCode = 2601;
    private const int SqlServerForeignKeyCode = 547;
    private const string PostgresUniqueViolationCode = "23505";
    private const string PostgresForeignKeyViolationCode = "23503";

    /// <summary>
    /// Classifies the specified database update exception.
    /// </summary>
    /// <param name="exception">The database update exception to classify.</param>
    /// <returns>The detected constraint type, or <see cref="EfCoreConstraintType.Unknown"/>.</returns>
    internal static EfCoreConstraintType Classify(DbUpdateException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var innerException = exception.InnerException;
        var providerTypeName = innerException?.GetType().FullName;

        return Classify(
            providerTypeName,
            GetIntProperty(innerException, "SqliteErrorCode"),
            GetIntProperty(innerException, "SqliteExtendedErrorCode"),
            GetIntProperty(innerException, "Number"),
            GetStringProperty(innerException, "SqlState"),
            innerException?.Message);
    }

    /// <summary>
    /// Classifies a constraint violation from extracted provider metadata.
    /// </summary>
    /// <param name="providerTypeName">The full provider exception type name.</param>
    /// <param name="sqliteErrorCode">The SQLite primary error code, if available.</param>
    /// <param name="sqliteExtendedErrorCode">The SQLite extended error code, if available.</param>
    /// <param name="sqlServerNumber">The SQL Server error number, if available.</param>
    /// <param name="postgresSqlState">The PostgreSQL SQLSTATE code, if available.</param>
    /// <param name="message">The provider exception message.</param>
    /// <returns>The detected constraint type, or <see cref="EfCoreConstraintType.Unknown"/>.</returns>
    internal static EfCoreConstraintType Classify(
        string? providerTypeName,
        int? sqliteErrorCode,
        int? sqliteExtendedErrorCode,
        int? sqlServerNumber,
        string? postgresSqlState,
        string? message)
    {
        return TryClassifySqlite(providerTypeName, sqliteErrorCode, sqliteExtendedErrorCode)
            ?? TryClassifySqlServer(providerTypeName, sqlServerNumber)
            ?? TryClassifyPostgres(providerTypeName, postgresSqlState)
            ?? ClassifyFromMessage(message)
            ?? EfCoreConstraintType.Unknown;
    }

    private static EfCoreConstraintType? TryClassifySqlite(
        string? providerTypeName,
        int? sqliteErrorCode,
        int? sqliteExtendedErrorCode)
    {
        if (!string.Equals(providerTypeName, "Microsoft.Data.Sqlite.SqliteException", StringComparison.Ordinal))
        {
            return null;
        }

        if (sqliteErrorCode != SqliteConstraintCode)
        {
            return null;
        }

        return sqliteExtendedErrorCode switch
        {
            SqliteUniqueExtendedCode or SqlitePrimaryKeyExtendedCode => EfCoreConstraintType.UniqueConstraint,
            SqliteForeignKeyExtendedCode => EfCoreConstraintType.ForeignKeyConstraint,
            _ => null,
        };
    }

    private static EfCoreConstraintType? TryClassifySqlServer(string? providerTypeName, int? sqlServerNumber)
    {
        if (!string.Equals(providerTypeName, "Microsoft.Data.SqlClient.SqlException", StringComparison.Ordinal) &&
            !string.Equals(providerTypeName, "System.Data.SqlClient.SqlException", StringComparison.Ordinal))
        {
            return null;
        }

        return sqlServerNumber switch
        {
            SqlServerUniqueConstraintCode or SqlServerUniqueIndexCode => EfCoreConstraintType.UniqueConstraint,
            SqlServerForeignKeyCode => EfCoreConstraintType.ForeignKeyConstraint,
            _ => null,
        };
    }

    private static EfCoreConstraintType? TryClassifyPostgres(string? providerTypeName, string? postgresSqlState)
    {
        if (!string.Equals(providerTypeName, "Npgsql.PostgresException", StringComparison.Ordinal))
        {
            return null;
        }

        return postgresSqlState switch
        {
            PostgresUniqueViolationCode => EfCoreConstraintType.UniqueConstraint,
            PostgresForeignKeyViolationCode => EfCoreConstraintType.ForeignKeyConstraint,
            _ => null,
        };
    }

    private static EfCoreConstraintType? ClassifyFromMessage(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return null;
        }

        if (message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("PRIMARY KEY", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("DUPLICATE", StringComparison.OrdinalIgnoreCase))
        {
            return EfCoreConstraintType.UniqueConstraint;
        }

        if (message.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase))
        {
            return EfCoreConstraintType.ForeignKeyConstraint;
        }

        return null;
    }

    private static int? GetIntProperty(object? instance, string propertyName)
    {
        if (instance is null)
        {
            return null;
        }

        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        return property?.GetValue(instance) as int?;
    }

    private static string? GetStringProperty(object? instance, string propertyName)
    {
        if (instance is null)
        {
            return null;
        }

        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        return property?.GetValue(instance) as string;
    }
}
