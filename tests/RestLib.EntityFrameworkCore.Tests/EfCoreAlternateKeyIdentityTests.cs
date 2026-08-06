using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestLib.Batch;
using RestLib.EntityFrameworkCore.Tests.Fakes;
using Xunit;

namespace RestLib.EntityFrameworkCore.Tests;

[Trait("Type", "Integration")]
[Trait("Feature", "Identity")]
public sealed class EfCoreAlternateKeyIdentityTests
{
    [Fact]
    public async Task UpdateEndpoint_AlternateBodyKeyDiffersFromRouteKey_PreservesRouteAndStorageIdentity()
    {
        // Arrange
        await using var testHost = await AlternateKeyTestHost.CreateAsync();
        var routeKey = Guid.NewGuid();
        var bodyKey = Guid.NewGuid();
        testHost.DbContext.IntKeyEntities.Add(new IntKeyEntity
        {
            Id = 42,
            ExternalId = routeKey,
            Name = "Original"
        });
        await testHost.DbContext.SaveChangesAsync();
        testHost.DbContext.ChangeTracker.Clear();

        var replacement = new
        {
            id = 999,
            external_id = bodyKey,
            name = "Updated through HTTP"
        };

        // Act
        var response = await testHost.Client.PutAsJsonAsync(
            $"/api/alternate-key-items/{routeKey}",
            replacement);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var responseDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        responseDocument.RootElement.GetProperty("id").GetInt32().Should().Be(42);
        responseDocument.RootElement.GetProperty("external_id").GetGuid().Should().Be(routeKey);

        testHost.DbContext.ChangeTracker.Clear();
        var repository = CreateRepository(testHost.DbContext);
        var persisted = await repository.GetByIdAsync(routeKey);
        persisted.Should().NotBeNull();
        persisted!.Id.Should().Be(42);
        persisted.ExternalId.Should().Be(routeKey);
        persisted.Name.Should().Be("Updated through HTTP");
        (await repository.GetByIdAsync(bodyKey)).Should().BeNull();
        (await testHost.DbContext.IntKeyEntities.FindAsync(999)).Should().BeNull();
    }

    [Fact]
    public async Task BatchUpdateEndpoint_ConflictingBodyIdentity_UsesEnvelopeAndStorageIdentity()
    {
        // Arrange
        await using var testHost = await AlternateKeyTestHost.CreateAsync();
        var envelopeKey = Guid.NewGuid();
        var bodyKey = Guid.NewGuid();
        testHost.DbContext.IntKeyEntities.Add(new IntKeyEntity
        {
            Id = 42,
            ExternalId = envelopeKey,
            Name = "Original"
        });
        await testHost.DbContext.SaveChangesAsync();
        testHost.DbContext.ChangeTracker.Clear();

        var payload = new
        {
            action = "update",
            items = new[]
            {
                new
                {
                    id = envelopeKey,
                    body = new
                    {
                        id = 999,
                        external_id = bodyKey,
                        name = "Updated through batch"
                    }
                }
            }
        };

        // Act
        var response = await testHost.Client.PostAsJsonAsync(
            "/api/alternate-key-items/batch",
            payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var responseDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var resultEntity = responseDocument.RootElement
            .GetProperty("items")[0]
            .GetProperty("entity");
        resultEntity.GetProperty("id").GetInt32().Should().Be(42);
        resultEntity.GetProperty("external_id").GetGuid().Should().Be(envelopeKey);

        testHost.DbContext.ChangeTracker.Clear();
        var repository = CreateRepository(testHost.DbContext);
        var persisted = await repository.GetByIdAsync(envelopeKey);
        persisted.Should().NotBeNull();
        persisted!.Id.Should().Be(42);
        persisted.ExternalId.Should().Be(envelopeKey);
        persisted.Name.Should().Be("Updated through batch");
        (await repository.GetByIdAsync(bodyKey)).Should().BeNull();
        (await testHost.DbContext.IntKeyEntities.FindAsync(999)).Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_AlternateBodyKeyDiffersFromRouteKey_PreservesRouteAndStorageIdentity()
    {
        // Arrange
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDbContext(connection);
        await db.Database.EnsureCreatedAsync();
        var routeKey = Guid.NewGuid();
        var bodyKey = Guid.NewGuid();
        db.IntKeyEntities.Add(new IntKeyEntity
        {
            Id = 42,
            ExternalId = routeKey,
            Name = "Original"
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var repository = CreateRepository(db);
        var replacement = new IntKeyEntity
        {
            Id = 999,
            ExternalId = bodyKey,
            Name = "Updated"
        };

        // Act
        var result = await repository.UpdateAsync(routeKey, replacement);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(42);
        result.ExternalId.Should().Be(routeKey);
        result.Name.Should().Be("Updated");
        db.ChangeTracker.Clear();
        (await repository.GetByIdAsync(routeKey)).Should().BeEquivalentTo(result);
        (await repository.GetByIdAsync(bodyKey)).Should().BeNull();
    }

    [Fact]
    public async Task PatchAsync_AlternateKeyFieldIsPresent_RejectsPatchAndPreservesTrackedEntity()
    {
        // Arrange
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDbContext(connection);
        await db.Database.EnsureCreatedAsync();
        var routeKey = Guid.NewGuid();
        var entity = new IntKeyEntity
        {
            Id = 42,
            ExternalId = routeKey,
            Name = "Original"
        };
        db.IntKeyEntities.Add(entity);
        await db.SaveChangesAsync();
        var repository = CreateRepository(db);
        using var patchDocument = JsonDocument.Parse(
            $$"""{"external_id":"{{Guid.NewGuid()}}","name":"Should not persist"}""");

        // Act
        var act = () => repository.PatchAsync(routeKey, patchDocument.RootElement);

        // Assert
        await act.Should().ThrowAsync<EfCorePatchValidationException>()
            .WithMessage("*immutable resource key field 'external_id'*");
        entity.ExternalId.Should().Be(routeKey);
        entity.Name.Should().Be("Original");
        db.Entry(entity).State.Should().Be(EntityState.Unchanged);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var persisted = await repository.GetByIdAsync(routeKey);
        persisted.Should().NotBeNull();
        persisted!.ExternalId.Should().Be(routeKey);
        persisted.Name.Should().Be("Original");
    }

    private static KeyDetectionTestDbContext CreateDbContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<KeyDetectionTestDbContext>()
            .UseSqlite(connection)
            .Options;
        return new KeyDetectionTestDbContext(options);
    }

    private static EfCoreRepository<KeyDetectionTestDbContext, IntKeyEntity, Guid> CreateRepository(
        KeyDetectionTestDbContext db)
    {
        return new EfCoreRepository<KeyDetectionTestDbContext, IntKeyEntity, Guid>(
            db,
            new EfCoreRepositoryOptions<IntKeyEntity, Guid>
            {
                KeySelector = entity => entity.ExternalId
            });
    }

    private sealed class AlternateKeyTestHost : IAsyncDisposable
    {
        private readonly IHost _host;
        private readonly IServiceScope _scope;
        private readonly SqliteConnection _connection;

        private AlternateKeyTestHost(
            IHost host,
            HttpClient client,
            IServiceScope scope,
            KeyDetectionTestDbContext dbContext,
            SqliteConnection connection)
        {
            _host = host;
            _scope = scope;
            _connection = connection;
            Client = client;
            DbContext = dbContext;
        }

        public HttpClient Client { get; }

        public KeyDetectionTestDbContext DbContext { get; }

        public static async Task<AlternateKeyTestHost> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            IHost? host = null;
            IServiceScope? scope = null;
            try
            {
                await connection.OpenAsync();

                host = new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder
                        .UseTestServer()
                        .ConfigureServices(services =>
                        {
                            services.AddRestLib(_ => { });
                            services.AddSingleton(connection);
                            services.AddDbContext<KeyDetectionTestDbContext>(options =>
                                options.UseSqlite(connection));
                            services.AddRestLibEfCore<KeyDetectionTestDbContext, IntKeyEntity, Guid>(options =>
                                options.KeySelector = entity => entity.ExternalId);
                            services.AddRouting();
                        })
                        .Configure(app =>
                        {
                            app.UseRouting();
                            app.UseEndpoints(endpoints =>
                            {
                                endpoints.MapRestLib<IntKeyEntity, Guid>(
                                    "/api/alternate-key-items",
                                    config =>
                                    {
                                        config.AllowAnonymous();
                                        config.KeySelector = entity => entity.ExternalId;
                                        config.EnableBatch(BatchAction.Update);
                                    });
                            });
                        });
                })
                .Build();

                await host.StartAsync();
                scope = host.Services.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<KeyDetectionTestDbContext>();
                await dbContext.Database.EnsureCreatedAsync();

                return new AlternateKeyTestHost(
                    host,
                    host.GetTestClient(),
                    scope,
                    dbContext,
                    connection);
            }
            catch
            {
                scope?.Dispose();
                host?.Dispose();
                await connection.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            try
            {
                await _host.StopAsync();
            }
            finally
            {
                _scope.Dispose();
                _host.Dispose();
                await _connection.DisposeAsync();
            }
        }
    }
}
