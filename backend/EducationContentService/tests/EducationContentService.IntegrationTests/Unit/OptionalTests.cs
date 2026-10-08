using System.Text.Json;
using EducationContentService.Contracts.Common;
using EducationContentService.Contracts.Courses;

namespace EducationContentService.IntegrationTests.Unit;

public class OptionalTests
{
    [Fact]
    public void Default_IsNotSet()
    {
        Optional<int> o = default;

        Assert.False(o.IsSet);
        Assert.Equal(0, o.Value);
    }

    [Fact]
    public void Of_MarksIsSetTrue()
    {
        Optional<int> o = Optional<int>.Of(42);

        Assert.True(o.IsSet);
        Assert.Equal(42, o.Value);
    }

    [Fact]
    public void Of_WithNullableNull_IsSetTrue()
    {
        Optional<decimal?> o = Optional<decimal?>.Of(null);

        Assert.True(o.IsSet);
        Assert.Null(o.Value);
    }

    [Fact]
    public void ImplicitConversion_FromValue_IsSetTrue()
    {
        Optional<string> o = "hello";

        Assert.True(o.IsSet);
        Assert.Equal("hello", o.Value);
    }

    [Fact]
    public void Or_WhenNotSet_ReturnsFallback()
    {
        Optional<string> o = default;

        Assert.Equal("fallback", o.Or("fallback"));
    }

    [Fact]
    public void Or_WhenSet_ReturnsValue()
    {
        Optional<string> o = "actual";

        Assert.Equal("actual", o.Or("fallback"));
    }

    [Fact]
    public void Equality_TwoUnset_AreEqual()
    {
        Optional<int> a = default;
        Optional<int> b = default;

        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void Equality_UnsetVsSetWithDefaultValue_AreNotEqual()
    {
        // Required for [JsonIgnore(WhenWritingDefault)] correctness:
        // Optional with IsSet=false must NOT compare equal to Optional<int>.Of(0).
        Optional<int> unset = default;
        Optional<int> setToZero = Optional<int>.Of(0);

        Assert.NotEqual(unset, setToZero);
    }

    // ── JSON round-trip ────────────────────────────────────────────────────

    [Fact]
    public void Deserialize_MissingProperty_LeavesOptionalUnset()
    {
        const string json = """{"title":"Hello"}""";

        UpdateCourseRequest? req = JsonSerializer.Deserialize<UpdateCourseRequest>(json, JsonOptions);

        Assert.NotNull(req);
        Assert.True(req.Title.IsSet);
        Assert.Equal("Hello", req.Title.Value);
        Assert.False(req.Slug.IsSet);
    }

    [Fact]
    public void Deserialize_PresentNull_IsSetWithNull()
    {
        const string json = """{"gettingStartedModuleId":null}""";

        UpdateCourseRequest? req = JsonSerializer.Deserialize<UpdateCourseRequest>(json, JsonOptions);

        Assert.NotNull(req);
        Assert.True(req.GettingStartedModuleId.IsSet);
        Assert.Null(req.GettingStartedModuleId.Value);
    }

    [Fact]
    public void Deserialize_PresentValue_IsSetWithValue()
    {
        const string json = """{"slug":"my-slug"}""";

        UpdateCourseRequest? req = JsonSerializer.Deserialize<UpdateCourseRequest>(json, JsonOptions);

        Assert.NotNull(req);
        Assert.True(req.Slug.IsSet);
        Assert.Equal("my-slug", req.Slug.Value);
    }

    [Fact]
    public void Serialize_UnsetFields_AreOmittedFromJson()
    {
        // Critical for PATCH semantics: a C# client that constructs UpdateCourseRequest
        // with only some fields set must not emit the rest as null in the JSON body.
        UpdateCourseRequest req = new(Title: "T", Description: "D");

        string json = JsonSerializer.Serialize(req, JsonOptions);

        Assert.Contains("\"title\":\"T\"", json, StringComparison.Ordinal);
        Assert.Contains("\"description\":\"D\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"gettingStartedModuleId\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"slug\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_ExplicitNullValue_IsEmittedAsJsonNull()
    {
        UpdateCourseRequest req = new(GettingStartedModuleId: new Optional<Guid?>(null));

        string json = JsonSerializer.Serialize(req, JsonOptions);

        Assert.Contains("\"gettingStartedModuleId\":null", json, StringComparison.Ordinal);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };
}
