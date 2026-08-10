using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using RestLib.Batch;
using RestLib.Configuration;
using RestLib.InMemory;
using RestLib.Internal;
using RestLib.Responses;
using RestLib.Tests.Fakes;
using Xunit;

namespace RestLib.Tests;

[Trait("Feature", "Identity")]
public class KeyConversionTests
{
    [Fact]
    [Trait("Type", "Unit")]
    public void ConvertRouteValue_DecimalUnderGermanCulture_UsesInvariantCulture()
    {
        // Arrange
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var context = CreateHttpContext("amount", "1234.5");

            // Act
            var result = RestLibKeyConversion.ConvertRouteValue<decimal>(context, "amount");

            // Assert
            result.Should().Be(1234.5m);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Theory]
    [Trait("Type", "Unit")]
    [InlineData("active", RouteKeyStatus.Active)]
    [InlineData("1", RouteKeyStatus.Active)]
    public void ConvertRouteValue_DefinedEnumNameOrNumber_ReturnsValue(
        string routeValue,
        RouteKeyStatus expected)
    {
        // Arrange
        var context = CreateHttpContext("status", routeValue);

        // Act
        var result = RestLibKeyConversion.ConvertRouteValue<RouteKeyStatus>(context, "status");

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [Trait("Type", "Unit")]
    [InlineData("0")]
    [InlineData("99")]
    [InlineData("999999999999999999999")]
    [InlineData("unknown")]
    public void ConvertRouteValue_InvalidEnumValue_ThrowsBadHttpRequestException(string routeValue)
    {
        // Arrange
        var context = CreateHttpContext("status", routeValue);

        // Act
        Action act = () => RestLibKeyConversion.ConvertRouteValue<RouteKeyStatus>(context, "status");

        // Assert
        act.Should().Throw<BadHttpRequestException>()
            .WithMessage("*status*RouteKeyStatus*");
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void ConvertRouteValue_OverflowingInteger_ThrowsBadHttpRequestException()
    {
        // Arrange
        var context = CreateHttpContext("sequence", "2147483648");

        // Act
        Action act = () => RestLibKeyConversion.ConvertRouteValue<int>(context, "sequence");

        // Assert
        act.Should().Throw<BadHttpRequestException>()
            .WithMessage("*sequence*Int32*");
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void ConvertRouteValue_NullReturningConverter_ThrowsBadHttpRequestException()
    {
        // Arrange
        var context = CreateHttpContext("custom", "value");

        // Act
        Action act = () => RestLibKeyConversion.ConvertRouteValue<NullRouteKey>(context, "custom");

        // Assert
        act.Should().Throw<BadHttpRequestException>()
            .WithMessage("*custom*NullRouteKey*");
    }

    [Fact]
    [Trait("Type", "Integration")]
    public async Task ScalarEnumRoute_DefinedAndUndefinedValues_EnforcesMembership()
    {
        // Arrange
        var repository = new InMemoryRepository<EnumKeyEntity, RouteKeyStatus>(
            static entity => entity.Id,
            static () => RouteKeyStatus.Active);
        repository.Seed([
            new EnumKeyEntity { Id = RouteKeyStatus.Active, Name = "Active" },
            new EnumKeyEntity { Id = (RouteKeyStatus)0, Name = "Undefined default" },
            new EnumKeyEntity { Id = (RouteKeyStatus)99, Name = "Undefined" },
        ]);

        IHost? host = null;
        HttpClient? client = null;
        try
        {
            (host, client) = await new TestHostBuilder<EnumKeyEntity, RouteKeyStatus>(
                    repository,
                    "/api/enum-keys")
                .WithEndpoint(static config => config.AllowAnonymous())
                .BuildAsync();

            // Act
            var namedResponse = await client.GetAsync("/api/enum-keys/Active");
            var numericResponse = await client.GetAsync("/api/enum-keys/1");
            var undefinedDefaultResponse = await client.GetAsync("/api/enum-keys/0");
            var undefinedResponse = await client.GetAsync("/api/enum-keys/99");

            // Assert
            namedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            numericResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            undefinedDefaultResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            undefinedResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally
        {
            client?.Dispose();
            if (host is not null)
            {
                await host.StopAsync();
                host.Dispose();
            }
        }
    }

    [Fact]
    [Trait("Type", "Integration")]
    public async Task ScalarEnumBatchDelete_UndefinedMiddleKey_ReturnsIndexedErrorAndDeletesValidSiblings()
    {
        // Arrange
        var repository = new InMemoryRepository<EnumKeyEntity, RouteKeyStatus>(
            static entity => entity.Id,
            static () => RouteKeyStatus.Active);
        repository.Seed([
            new EnumKeyEntity { Id = RouteKeyStatus.Active, Name = "Active" },
            new EnumKeyEntity { Id = RouteKeyStatus.Archived, Name = "Archived" },
            new EnumKeyEntity { Id = (RouteKeyStatus)99, Name = "Undefined" },
        ]);

        IHost? host = null;
        HttpClient? client = null;
        try
        {
            (host, client) = await new TestHostBuilder<EnumKeyEntity, RouteKeyStatus>(
                    repository,
                    "/api/enum-keys")
                .WithEndpoint(static config =>
                {
                    config.AllowAnonymous();
                    config.EnableBatch(BatchAction.Delete);
                })
                .BuildAsync();
            var content = new StringContent(
                """
                {
                  "action": "delete",
                  "items": [1, 99, 2]
                }
                """,
                Encoding.UTF8,
                "application/json");

            // Act
            var response = await client.PostAsync("/api/enum-keys/batch", content);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var items = json.GetProperty("items");
            items.GetArrayLength().Should().Be(3);
            items.EnumerateArray().Select(static item => item.GetProperty("index").GetInt32())
                .Should().Equal(0, 1, 2);
            items.EnumerateArray().Select(static item => item.GetProperty("status").GetInt32())
                .Should().Equal(204, 400, 204);
            items[1].GetProperty("error").GetProperty("type").GetString()
                .Should().Be(ProblemTypes.BadRequest);

            (await repository.GetByIdAsync(RouteKeyStatus.Active)).Should().BeNull();
            (await repository.GetByIdAsync(RouteKeyStatus.Archived)).Should().BeNull();
            (await repository.GetByIdAsync((RouteKeyStatus)99)).Should().NotBeNull();
        }
        finally
        {
            client?.Dispose();
            if (host is not null)
            {
                await host.StopAsync();
                host.Dispose();
            }
        }
    }

    [Fact]
    [Trait("Type", "Integration")]
    public async Task CompositeRoute_InvalidEnumOrOverflowingNumber_ReturnsBadRequest()
    {
        // Arrange
        var repository = new InMemoryRepository<CompositeEnumKeyEntity, RestLibCompositeKey<int, RouteKeyStatus>>(
            static entity => new RestLibCompositeKey<int, RouteKeyStatus>(entity.Sequence, entity.Status),
            static () => new RestLibCompositeKey<int, RouteKeyStatus>(1, RouteKeyStatus.Active));
        repository.Seed([
            new CompositeEnumKeyEntity { Sequence = 1, Status = RouteKeyStatus.Active, Name = "Active" },
            new CompositeEnumKeyEntity { Sequence = 5, Status = (RouteKeyStatus)99, Name = "Undefined" },
        ]);

        IHost? host = null;
        HttpClient? client = null;
        try
        {
            (host, client) = await new TestHostBuilder<CompositeEnumKeyEntity, RestLibCompositeKey<int, RouteKeyStatus>>(
                    repository,
                    "/api/composite-enum-keys")
                .WithEndpoint(static config =>
                {
                    config.AllowAnonymous();
                    config.UseCompositeKey(
                        static entity => entity.Sequence,
                        "sequence",
                        static entity => entity.Status,
                        "status");
                })
                .BuildAsync();

            // Act
            var validResponse = await client.GetAsync("/api/composite-enum-keys/1/active");
            var undefinedResponse = await client.GetAsync("/api/composite-enum-keys/5/99");
            var overflowResponse = await client.GetAsync("/api/composite-enum-keys/2147483648/active");

            // Assert
            validResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            undefinedResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            overflowResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally
        {
            client?.Dispose();
            if (host is not null)
            {
                await host.StopAsync();
                host.Dispose();
            }
        }
    }

    private static DefaultHttpContext CreateHttpContext(string routeParameterName, string value)
    {
        var context = new DefaultHttpContext();
        context.Request.RouteValues[routeParameterName] = value;
        return context;
    }

    public enum RouteKeyStatus
    {
        Active = 1,
        Archived = 2,
    }

    private sealed class EnumKeyEntity
    {
        public RouteKeyStatus Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class CompositeEnumKeyEntity
    {
        public int Sequence { get; set; }

        public RouteKeyStatus Status { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    [TypeConverter(typeof(NullRouteKeyConverter))]
    private sealed class NullRouteKey
    {
    }

    private sealed class NullRouteKeyConverter : TypeConverter
    {
        /// <inheritdoc />
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        {
            return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
        }

        /// <inheritdoc />
        public override object? ConvertFrom(
            ITypeDescriptorContext? context,
            CultureInfo? culture,
            object value)
        {
            return null;
        }
    }
}
