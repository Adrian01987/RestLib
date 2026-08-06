using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RestLib.Filtering;
using RestLib.Pagination;
using RestLib.Search;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

/// <summary>
/// Verifies the portable and collation-dependent string-query contracts against live databases.
/// </summary>
[Trait("Category", "Story5.1.1")]
[Trait("Feature", "Filtering")]
[Trait("Feature", "Search")]
[Trait("Type", "Integration")]
public class EfCoreProviderCollationBehaviorTests
{
    private const string CaseInsensitiveSqlServerCollation = "Latin1_General_100_CI_AS";
    private const string CaseSensitiveSqlServerCollation = "Latin1_General_100_CS_AS";

    [Fact]
    public async Task PortableStringSemantics_Sqlite_MatchesAdapterContract()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateSqliteContext(connection);
        await InitializeAsync(context, PortableValues);

        // Act
        var results = await ExercisePortableContractAsync(context);

        // Assert
        AssertPortableContract(results);
    }

    [WindowsLocalDbFact]
    public async Task PortableStringSemantics_SqlServerCaseInsensitiveCollation_MatchesAdapterContract()
    {
        // Arrange
        await using var context = CreateCaseInsensitiveSqlServerContext();
        try
        {
            await InitializeAsync(context, PortableValues);

            // Act
            var results = await ExercisePortableContractAsync(context);

            // Assert
            AssertPortableContract(results);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }

    [WindowsLocalDbFact]
    public async Task PortableStringSemantics_SqlServerCaseSensitiveCollation_MatchesAdapterContract()
    {
        // Arrange
        await using var context = CreateCaseSensitiveSqlServerContext();
        try
        {
            await InitializeAsync(context, PortableValues);

            // Act
            var results = await ExercisePortableContractAsync(context);

            // Assert
            AssertPortableContract(results);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task ProviderDependentStringSemantics_SqliteBinaryCollation_RequireExactCase()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateSqliteContext(connection);
        await InitializeAsync(context, CaseValues);

        // Act
        var results = await ExerciseProviderDependentContractAsync(context);

        // Assert
        AssertCaseSensitiveContract(results);
    }

    [WindowsLocalDbFact]
    public async Task ProviderDependentStringSemantics_SqlServerCaseInsensitiveCollation_IgnoreCase()
    {
        // Arrange
        await using var context = CreateCaseInsensitiveSqlServerContext();
        try
        {
            await InitializeAsync(context, CaseValues);

            // Act
            var results = await ExerciseProviderDependentContractAsync(context);

            // Assert
            results.EqualityMatches.Should().BeEquivalentTo(CaseValues);
            results.CaseSensitiveSearchMatches.Should().BeEquivalentTo(CaseValues);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }

    [WindowsLocalDbFact]
    public async Task ProviderDependentStringSemantics_SqlServerCaseSensitiveCollation_RequireExactCase()
    {
        // Arrange
        await using var context = CreateCaseSensitiveSqlServerContext();
        try
        {
            await InitializeAsync(context, CaseValues);

            // Act
            var results = await ExerciseProviderDependentContractAsync(context);

            // Assert
            AssertCaseSensitiveContract(results);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }

    private static string[] PortableValues =>
    [
        "Alpha WIDGET",
        "beta widget",
        "100% Genuine",
        "1000 Genuine",
        "under_score",
        "underXscore"
    ];

    private static string[] CaseValues => ["widget", "WIDGET"];

    private static SqliteStringSemanticsDbContext CreateSqliteContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<SqliteStringSemanticsDbContext>()
            .UseSqlite(connection)
            .Options;
        return new SqliteStringSemanticsDbContext(options);
    }

    private static SqlServerCaseInsensitiveDbContext CreateCaseInsensitiveSqlServerContext()
    {
        var databaseName = $"RestLibQ20Ci_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<SqlServerCaseInsensitiveDbContext>()
            .UseSqlServer(CreateLocalDbConnectionString(databaseName))
            .Options;
        return new SqlServerCaseInsensitiveDbContext(options);
    }

    private static SqlServerCaseSensitiveDbContext CreateCaseSensitiveSqlServerContext()
    {
        var databaseName = $"RestLibQ20Cs_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<SqlServerCaseSensitiveDbContext>()
            .UseSqlServer(CreateLocalDbConnectionString(databaseName))
            .Options;
        return new SqlServerCaseSensitiveDbContext(options);
    }

    private static string CreateLocalDbConnectionString(string databaseName)
    {
        return $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};"
            + "Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=False";
    }

    private static async Task InitializeAsync(
        StringSemanticsDbContext context,
        IReadOnlyList<string> values)
    {
        await context.Database.EnsureCreatedAsync();
        context.Values.AddRange(values.Select((value, index) => new StringSemanticsEntity
        {
            Id = index + 1,
            Value = value
        }));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    private static async Task<PortableContractResults> ExercisePortableContractAsync(
        StringSemanticsDbContext context)
    {
        var containsWidget = await QueryAsync(
            context,
            filters: [CreateFilter(FilterOperator.Contains, "wIdGeT")]);
        var searchWidget = await QueryAsync(
            context,
            search: CreateSearch("wIdGeT", caseSensitive: false));
        var containsPercent = await QueryAsync(
            context,
            filters: [CreateFilter(FilterOperator.Contains, "%")]);
        var containsUnderscore = await QueryAsync(
            context,
            filters: [CreateFilter(FilterOperator.Contains, "_")]);
        var searchPercent = await QueryAsync(
            context,
            search: CreateSearch("%", caseSensitive: false));
        var searchUnderscore = await QueryAsync(
            context,
            search: CreateSearch("_", caseSensitive: false));

        return new PortableContractResults(
            containsWidget,
            searchWidget,
            containsPercent,
            containsUnderscore,
            searchPercent,
            searchUnderscore);
    }

    private static async Task<ProviderDependentContractResults> ExerciseProviderDependentContractAsync(
        StringSemanticsDbContext context)
    {
        var equalityMatches = await QueryAsync(
            context,
            filters: [CreateFilter(FilterOperator.Eq, "widget")]);
        var caseSensitiveSearchMatches = await QueryAsync(
            context,
            search: CreateSearch("widget", caseSensitive: true));

        return new ProviderDependentContractResults(equalityMatches, caseSensitiveSearchMatches);
    }

    private static async Task<IReadOnlyList<string>> QueryAsync(
        StringSemanticsDbContext context,
        IReadOnlyList<FilterValue>? filters = null,
        SearchRequest? search = null)
    {
        var repository = new EfCoreRepository<StringSemanticsDbContext, StringSemanticsEntity, int>(
            context,
            new EfCoreRepositoryOptions<StringSemanticsEntity, int>());
        var page = await repository.GetAllAsync(new PaginationRequest
        {
            Limit = 100,
            Filters = filters ?? [],
            Search = search
        });

        return page.Items.Select(entity => entity.Value).ToList();
    }

    private static FilterValue CreateFilter(FilterOperator filterOperator, string value)
    {
        return new FilterValue
        {
            PropertyName = nameof(StringSemanticsEntity.Value),
            QueryParameterName = "value",
            PropertyType = typeof(string),
            RawValue = value,
            TypedValue = value,
            Operator = filterOperator
        };
    }

    private static SearchRequest CreateSearch(string term, bool caseSensitive)
    {
        return new SearchRequest
        {
            Term = term,
            QueryParameterName = "q",
            CaseSensitive = caseSensitive,
            Properties =
            [
                new SearchPropertyConfiguration
                {
                    PropertyName = nameof(StringSemanticsEntity.Value),
                    QueryParameterName = "value"
                }
            ]
        };
    }

    private static void AssertPortableContract(PortableContractResults results)
    {
        results.ContainsWidget.Should().BeEquivalentTo("Alpha WIDGET", "beta widget");
        results.SearchWidget.Should().BeEquivalentTo("Alpha WIDGET", "beta widget");
        results.ContainsPercent.Should().ContainSingle().Which.Should().Be("100% Genuine");
        results.ContainsUnderscore.Should().ContainSingle().Which.Should().Be("under_score");
        results.SearchPercent.Should().ContainSingle().Which.Should().Be("100% Genuine");
        results.SearchUnderscore.Should().ContainSingle().Which.Should().Be("under_score");
    }

    private static void AssertCaseSensitiveContract(ProviderDependentContractResults results)
    {
        results.EqualityMatches.Should().ContainSingle().Which.Should().Be("widget");
        results.CaseSensitiveSearchMatches.Should().ContainSingle().Which.Should().Be("widget");
    }

    private sealed record PortableContractResults(
        IReadOnlyList<string> ContainsWidget,
        IReadOnlyList<string> SearchWidget,
        IReadOnlyList<string> ContainsPercent,
        IReadOnlyList<string> ContainsUnderscore,
        IReadOnlyList<string> SearchPercent,
        IReadOnlyList<string> SearchUnderscore);

    private sealed record ProviderDependentContractResults(
        IReadOnlyList<string> EqualityMatches,
        IReadOnlyList<string> CaseSensitiveSearchMatches);

    private sealed class StringSemanticsEntity
    {
        public int Id { get; set; }

        public string Value { get; set; } = string.Empty;
    }

    private abstract class StringSemanticsDbContext : DbContext
    {
        protected StringSemanticsDbContext(DbContextOptions options)
            : base(options)
        {
        }

        public DbSet<StringSemanticsEntity> Values => Set<StringSemanticsEntity>();

        protected virtual string? Collation => null;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var entity = modelBuilder.Entity<StringSemanticsEntity>();
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).ValueGeneratedNever();
            entity.Property(value => value.Value).HasMaxLength(100).IsRequired();

            if (Collation is not null)
            {
                entity.Property(value => value.Value).UseCollation(Collation);
            }
        }
    }

    private sealed class SqliteStringSemanticsDbContext : StringSemanticsDbContext
    {
        public SqliteStringSemanticsDbContext(DbContextOptions<SqliteStringSemanticsDbContext> options)
            : base(options)
        {
        }
    }

    private sealed class SqlServerCaseInsensitiveDbContext : StringSemanticsDbContext
    {
        public SqlServerCaseInsensitiveDbContext(
            DbContextOptions<SqlServerCaseInsensitiveDbContext> options)
            : base(options)
        {
        }

        protected override string Collation => CaseInsensitiveSqlServerCollation;
    }

    private sealed class SqlServerCaseSensitiveDbContext : StringSemanticsDbContext
    {
        public SqlServerCaseSensitiveDbContext(
            DbContextOptions<SqlServerCaseSensitiveDbContext> options)
            : base(options)
        {
        }

        protected override string Collation => CaseSensitiveSqlServerCollation;
    }

    [AttributeUsage(AttributeTargets.Method)]
    private sealed class WindowsLocalDbFactAttribute : FactAttribute
    {
        public WindowsLocalDbFactAttribute()
        {
            if (!OperatingSystem.IsWindows())
            {
                Skip = "SQL Server LocalDB behavioral tests require Windows.";
            }
        }
    }
}
