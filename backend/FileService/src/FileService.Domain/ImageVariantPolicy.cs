namespace FileService.Domain;

/// <summary>
///     Pure policy for server-side responsive image variants (issue #646). Holds the
///     width buckets, the in-scope content-types, the only-downscale rule, the
///     nearest-variant selection used when serving <c>?w=</c>, and the variant
///     storage-key scheme. No I/O — fully unit-testable.
/// </summary>
public static class ImageVariantPolicy
{
    public const string VARIANT_CONTENT_TYPE = "image/webp";
    public const string VARIANT_EXTENSION = "webp";

    /// <summary>WebP encoder quality used for generated variants (1..100).</summary>
    public const int WEBP_QUALITY = 80;

    /// <summary>
    ///     Decompression-bomb guard: max pixel area (width × height) we will decode.
    ///     A 10&nbsp;MB upload can encode a tiny file that decodes to a huge raster
    ///     (e.g. 10000×10000 ≈ 100&nbsp;MP ≈ 400&nbsp;MB at 32bpp) and OOM the FileService
    ///     process. Because generation runs on a durable local queue, an OOM-killed
    ///     poison message would be re-delivered into an OOM loop — so we reject oversized
    ///     images BEFORE any full decode (dimensions are read from bounded headers).
    ///     40&nbsp;MP comfortably covers any legitimate cover/avatar/markdown image.
    /// </summary>
    public const int MAX_PIXELS = 40_000_000;

    /// <summary>
    ///     Companion to <see cref="MAX_PIXELS"/>: a hard cap on either dimension. Blocks
    ///     pathological aspect ratios (e.g. 1×200000) that stay under the area budget but
    ///     still allocate huge buffers / stress the decoder.
    /// </summary>
    public const int MAX_DIMENSION = 12_000;

    /// <summary>
    ///     Stable error code for an image that exceeds the decode budget
    ///     (<see cref="MAX_PIXELS"/> / <see cref="MAX_DIMENSION"/>). PERMANENT — re-decode
    ///     of the same bytes will always exceed the cap, so the variant-generation handler
    ///     maps it straight to the DLQ instead of retrying.
    /// </summary>
    public const string ERROR_TOO_LARGE = "image.too.large";

    /// <summary>
    ///     Stable error code for an undecodable image (unknown/unsupported format or
    ///     corrupt content). PERMANENT — retrying the same bytes never succeeds.
    /// </summary>
    public const string ERROR_DECODE_FAILED = "image.decode.failed";

    private const string VARIANTS_SEGMENT = "variants";

    /// <summary>
    ///     True when an image of the given pixel dimensions exceeds the decode budget
    ///     (<see cref="MAX_PIXELS"/> area or <see cref="MAX_DIMENSION"/> on either side).
    ///     Callers must check this against cheaply-read dimensions and refuse to fully
    ///     decode when it returns <c>true</c> (decompression-bomb / OOM guard).
    /// </summary>
    public static bool ExceedsDecodeBudget(int width, int height) =>
        width > MAX_DIMENSION
        || height > MAX_DIMENSION
        || (long)width * height > MAX_PIXELS;

    /// <summary>
    ///     Allowed responsive widths, ascending. Mirrors the frontend contract
    ///     <c>?w={320|640|960|1280}</c>.
    /// </summary>
    public static readonly IReadOnlyList<int> Widths = [320, 640, 960, 1280];

    /// <summary>
    ///     Image content-types we generate variants for. Mirrors the upload policy
    ///     for image usage types (avatar / previews / markdown images).
    /// </summary>
    private static readonly HashSet<string> _imageContentTypes =
        new(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "image/webp" };

    public static bool IsImageContentType(string? contentType) =>
        !string.IsNullOrWhiteSpace(contentType) && _imageContentTypes.Contains(contentType.Trim());

    /// <summary>
    ///     The widths that should be generated for an original of the given width.
    ///     Only-downscale: a width is skipped when it is &gt;= the original width
    ///     (we never upscale). A tiny original (smaller than the smallest bucket)
    ///     yields no variants.
    /// </summary>
    public static IReadOnlyList<int> WidthsToGenerate(int originalWidth) =>
        originalWidth <= 0
            ? []
            : Widths.Where(w => w < originalWidth).ToArray();

    /// <summary>
    ///     Clamps a requested width to the bucket list. A value below the smallest
    ///     bucket clamps up to the smallest; a value at/above the largest clamps to
    ///     the largest. <c>null</c> for non-positive input.
    /// </summary>
    public static int? ClampRequestedWidth(int? requestedWidth)
    {
        if (requestedWidth is null or <= 0)
        {
            return null;
        }

        int w = requestedWidth.Value;
        if (w <= Widths[0])
        {
            return Widths[0];
        }

        // Smallest bucket that is >= requested; if none, the largest bucket.
        foreach (int bucket in Widths)
        {
            if (bucket >= w)
            {
                return bucket;
            }
        }

        return Widths[^1];
    }

    /// <summary>
    ///     Selects the variant to serve for a requested width: the nearest generated
    ///     variant whose width is &gt;= the clamped requested width. Returns
    ///     <c>null</c> (→ caller falls back to the original) when the request is
    ///     absent/invalid, no variants exist, or every variant is smaller than the
    ///     requested width (i.e. the requested width is at/above the original size).
    /// </summary>
    public static ImageVariant? SelectVariant(IReadOnlyCollection<ImageVariant>? variants, int? requestedWidth)
    {
        if (variants is null || variants.Count == 0)
        {
            return null;
        }

        int? clamped = ClampRequestedWidth(requestedWidth);
        if (clamped is null)
        {
            return null;
        }

        return variants
            .Where(v => v.Width >= clamped.Value)
            .OrderBy(v => v.Width)
            .FirstOrDefault();
    }

    /// <summary>
    ///     Derives the storage key of a width variant from the original asset id.
    ///     Scheme: <c>files/variants/{assetId:N}_{width}.webp</c> — a sibling
    ///     namespace of the original (<c>files/{assetId:N}.{ext}</c>), so variants
    ///     are easy to enumerate and delete alongside their parent.
    /// </summary>
    public static Result<StorageKey, Error> VariantStorageKey(Guid assetId, int width)
    {
        if (assetId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid("assetId");
        }

        if (width <= 0)
        {
            return GeneralErrors.ValueIsInvalid("width");
        }

        return StorageKey.Of($"files/{VARIANTS_SEGMENT}/{assetId:N}_{width}.{VARIANT_EXTENSION}");
    }
}
