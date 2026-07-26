using System.Text.Json;
using FluentAssertions;
using RestLib.Serialization;
using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Conformance tests for the RFC 7396 merge algorithm.
/// </summary>
[Trait("Type", "Unit")]
[Trait("Feature", "Patch")]
[Trait("Category", "Story31")]
public class JsonMergePatchTests
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    [Theory]
    [InlineData("""{"a":"b"}""", """{"a":"c"}""", """{"a":"c"}""")]
    [InlineData("""{"a":"b"}""", """{"b":"c"}""", """{"a":"b","b":"c"}""")]
    [InlineData("""{"a":"b"}""", """{"a":null}""", """{}""")]
    [InlineData("""{"a":"b","b":"c"}""", """{"a":null}""", """{"b":"c"}""")]
    [InlineData("""{"a":["b"]}""", """{"a":"c"}""", """{"a":"c"}""")]
    [InlineData("""{"a":"c"}""", """{"a":["b"]}""", """{"a":["b"]}""")]
    [InlineData("""{"a":{"b":"c"}}""", """{"a":{"b":"d","c":null}}""", """{"a":{"b":"d"}}""")]
    [InlineData("""{"a":[{"b":"c"}]}""", """{"a":[1]}""", """{"a":[1]}""")]
    [InlineData("""["a","b"]""", """["c","d"]""", """["c","d"]""")]
    [InlineData("""{"a":"b"}""", """["c"]""", """["c"]""")]
    [InlineData("""{"a":"foo"}""", "null", "null")]
    [InlineData("""{"a":"foo"}""", @"""bar""", @"""bar""")]
    [InlineData("""{"e":null}""", """{"a":1}""", """{"e":null,"a":1}""")]
    [InlineData("""[1,2]""", """{"a":"b","c":null}""", """{"a":"b"}""")]
    [InlineData("""{}""", """{"a":{"bb":{"ccc":null}}}""", """{"a":{"bb":{}}}""")]
    public void Apply_Rfc7396AppendixAExample_ProducesExpectedResult(
        string targetJson,
        string patchJson,
        string expectedJson)
    {
        // Arrange
        var target = Parse(targetJson);
        var patch = Parse(patchJson);
        var expected = Parse(expectedJson);

        // Act
        var result = JsonMergePatch.Apply(target, patch, JsonOptions);

        // Assert
        JsonElement.DeepEquals(result, expected).Should().BeTrue();
    }

    [Fact]
    public void Apply_CaseDistinctUntypedMembers_PreservesBothMembers()
    {
        // Arrange
        var target = Parse("""{"name":"lower"}""");
        var patch = Parse("""{"Name":"upper"}""");
        var expected = Parse("""{"name":"lower","Name":"upper"}""");

        // Act
        var result = JsonMergePatch.Apply(target, patch, JsonOptions);

        // Assert
        JsonElement.DeepEquals(result, expected).Should().BeTrue();
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
