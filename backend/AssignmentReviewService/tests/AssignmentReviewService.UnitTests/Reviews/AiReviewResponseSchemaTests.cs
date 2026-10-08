using System.Text.Json;
using AssignmentReviewService.Core.Features.Reviews.Models;

namespace AssignmentReviewService.UnitTests.Reviews;

public sealed class AiReviewResponseSchemaTests
{
    [Fact]
    public void Schema_IsValidJson()
    {
        using JsonDocument doc = JsonDocument.Parse(AiReviewResponseSchema.SCHEMA);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        Assert.Equal("object", doc.RootElement.GetProperty("type").GetString());

        JsonElement props = doc.RootElement.GetProperty("properties");
        Assert.True(props.TryGetProperty("verdict", out _));
        Assert.True(props.TryGetProperty("summary", out _));
        Assert.True(props.TryGetProperty("inline_comments", out _));
    }

    [Fact]
    public void Build_ReturnsAiJsonSchema_WithStrictTrue()
    {
        var built = AiReviewResponseSchema.Build();
        Assert.Equal(AiReviewResponseSchema.SCHEMA_NAME, built.Name);
        Assert.True(built.Strict);
    }

    private static readonly JsonSerializerOptions JSON_OPTIONS = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    [Fact]
    public void ResponseDto_DeserializesSnakeCaseFields()
    {
        const string json = """
            {
              "verdict": "MINOR_ISSUES",
              "summary": "ok",
              "inline_comments": [
                { "path": "a.cs", "line": 3, "body": "fix", "suggestion": null }
              ]
            }
            """;

        AiReviewResponseDto? dto = JsonSerializer.Deserialize<AiReviewResponseDto>(json, JSON_OPTIONS);
        Assert.NotNull(dto);
        Assert.Equal("MINOR_ISSUES", dto!.Verdict);
        Assert.Single(dto.InlineComments);
        Assert.Equal("a.cs", dto.InlineComments[0].Path);
        Assert.Equal(3, dto.InlineComments[0].Line);
    }
}
