using CSharpFunctionalExtensions;
using FileService.Domain;
using SharedKernel;

namespace FileService.UnitTests.Domain;

public class ImageVariantPolicyTests
{
    private static IReadOnlyList<ImageVariant> Variants(params int[] widths) =>
        widths.Select(w => new ImageVariant(w, $"files/variants/x_{w}.webp", "image/webp", 1000)).ToArray();

    // ── content-type scope ─────────────────────────────────────────────────

    [Theory]
    [InlineData("image/jpeg", true)]
    [InlineData("image/png", true)]
    [InlineData("image/webp", true)]
    [InlineData("IMAGE/JPEG", true)] // case-insensitive
    [InlineData("image/gif", false)]
    [InlineData("application/pdf", false)]
    [InlineData("video/mp4", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsImageContentType_MatchesUploadPolicyScope(string? contentType, bool expected) =>
        Assert.Equal(expected, ImageVariantPolicy.IsImageContentType(contentType));

    // ── decode budget (decompression-bomb guard) ───────────────────────────

    [Theory]
    [InlineData(1000, 600)]      // typical cover — fine
    [InlineData(4000, 3000)]     // 12 MP — fine
    [InlineData(12000, 3000)]    // dimension at the cap (not over) — fine
    [InlineData(6324, 6324)]     // ~39.99 MP — just under the 40 MP area cap
    public void ExceedsDecodeBudget_WithinBudget_False(int width, int height) =>
        Assert.False(ImageVariantPolicy.ExceedsDecodeBudget(width, height));

    [Theory]
    [InlineData(10000, 10000)]   // 100 MP — over the area cap
    [InlineData(12001, 100)]     // width over the dimension cap
    [InlineData(100, 12001)]     // height over the dimension cap
    [InlineData(7000, 7000)]     // 49 MP — over the area cap
    public void ExceedsDecodeBudget_OverBudget_True(int width, int height) =>
        Assert.True(ImageVariantPolicy.ExceedsDecodeBudget(width, height));

    [Fact]
    public void ExceedsDecodeBudget_AreaCheckDoesNotOverflowInt()
    {
        // 12000 x 12000 = 144 MP; the product must be computed as long, not wrap to a
        // small/negative int and falsely pass. Both dims are at the dimension cap so the
        // dimension check alone wouldn't catch it — the area check (as long) must.
        Assert.True(ImageVariantPolicy.ExceedsDecodeBudget(12000, 12000));
    }

    // ── only-downscale ─────────────────────────────────────────────────────

    [Fact]
    public void WidthsToGenerate_SmallOriginal_GeneratesNothing()
    {
        // 200px original is below the smallest bucket (320) → no variants.
        Assert.Empty(ImageVariantPolicy.WidthsToGenerate(200));
    }

    [Fact]
    public void WidthsToGenerate_SkipsWidthsAtOrAboveOriginal()
    {
        // 1000px original → only 320, 640, 960 (1280 >= 1000 is skipped).
        Assert.Equal(new[] { 320, 640, 960 }, ImageVariantPolicy.WidthsToGenerate(1000));
    }

    [Fact]
    public void WidthsToGenerate_LargeOriginal_GeneratesAllBuckets()
    {
        Assert.Equal(new[] { 320, 640, 960, 1280 }, ImageVariantPolicy.WidthsToGenerate(4000));
    }

    [Fact]
    public void WidthsToGenerate_ExactlySmallestBucket_GeneratesNothing()
    {
        // original == 320 → 320 is not < 320, nothing to downscale.
        Assert.Empty(ImageVariantPolicy.WidthsToGenerate(320));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void WidthsToGenerate_NonPositive_GeneratesNothing(int width) =>
        Assert.Empty(ImageVariantPolicy.WidthsToGenerate(width));

    // ── clamping ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(100, 320)]   // below smallest → smallest
    [InlineData(320, 320)]   // exact
    [InlineData(500, 640)]   // between → next up
    [InlineData(960, 960)]
    [InlineData(1280, 1280)]
    [InlineData(4000, 1280)] // above largest → largest
    public void ClampRequestedWidth_SnapsToBuckets(int requested, int expected) =>
        Assert.Equal(expected, ImageVariantPolicy.ClampRequestedWidth(requested));

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void ClampRequestedWidth_InvalidInput_ReturnsNull(int? requested) =>
        Assert.Null(ImageVariantPolicy.ClampRequestedWidth(requested));

    // ── variant selection (nearest >= w) ───────────────────────────────────

    [Fact]
    public void SelectVariant_NearestAtOrAboveRequested()
    {
        IReadOnlyList<ImageVariant> variants = Variants(320, 640, 960);

        // w=320 → 320; w=500 (clamps to 640) → 640; w=640 → 640.
        Assert.Equal(320, ImageVariantPolicy.SelectVariant(variants, 320)!.Width);
        Assert.Equal(640, ImageVariantPolicy.SelectVariant(variants, 500)!.Width);
        Assert.Equal(640, ImageVariantPolicy.SelectVariant(variants, 640)!.Width);
    }

    [Fact]
    public void SelectVariant_RequestedBelowSmallest_PicksSmallest()
    {
        IReadOnlyList<ImageVariant> variants = Variants(320, 640);
        Assert.Equal(320, ImageVariantPolicy.SelectVariant(variants, 100)!.Width);
    }

    [Fact]
    public void SelectVariant_RequestedAtOrAboveOriginal_ReturnsNull_FallBackToOriginal()
    {
        // Original was 1000px, so only 320/640/960 exist. Requesting 1280 (clamps to
        // 1280) has no variant >= 1280 → null → caller serves the original.
        IReadOnlyList<ImageVariant> variants = Variants(320, 640, 960);
        Assert.Null(ImageVariantPolicy.SelectVariant(variants, 1280));
        Assert.Null(ImageVariantPolicy.SelectVariant(variants, 4000));
    }

    [Fact]
    public void SelectVariant_NoVariants_ReturnsNull()
    {
        Assert.Null(ImageVariantPolicy.SelectVariant([], 320));
        Assert.Null(ImageVariantPolicy.SelectVariant(null, 320));
    }

    [Fact]
    public void SelectVariant_NoRequestedWidth_ReturnsNull()
    {
        IReadOnlyList<ImageVariant> variants = Variants(320, 640);
        Assert.Null(ImageVariantPolicy.SelectVariant(variants, null));
        Assert.Null(ImageVariantPolicy.SelectVariant(variants, 0));
    }

    // ── storage-key scheme ─────────────────────────────────────────────────

    [Fact]
    public void VariantStorageKey_DerivesSiblingKey()
    {
        var assetId = Guid.NewGuid();
        Result<StorageKey, Error> key = ImageVariantPolicy.VariantStorageKey(assetId, 640);

        Assert.True(key.IsSuccess);
        Assert.Equal($"files/variants/{assetId:N}_640.webp", key.Value.Value);
    }

    [Fact]
    public void VariantStorageKey_EmptyId_Fails() =>
        Assert.True(ImageVariantPolicy.VariantStorageKey(Guid.Empty, 640).IsFailure);

    [Fact]
    public void VariantStorageKey_NonPositiveWidth_Fails() =>
        Assert.True(ImageVariantPolicy.VariantStorageKey(Guid.NewGuid(), 0).IsFailure);
}
