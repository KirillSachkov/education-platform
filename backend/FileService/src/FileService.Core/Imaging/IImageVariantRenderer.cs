using System.Diagnostics.CodeAnalysis;

namespace FileService.Core.Imaging;

[SuppressMessage("Design", "CA1819:Properties should not return arrays",
    Justification = "Rendered image bytes are an inherently array-shaped payload passed straight to S3 upload.")]
public sealed record RenderedImage(int Width, int Height, byte[] Content, string ContentType);

/// <summary>
///     Decodes an original image and renders downscaled WebP variants. Implemented by
///     <c>FileService.Infrastructure.Imaging.SkiaVariantRenderer</c>.
///     Pure CPU work, no I/O — the caller supplies bytes and persists the result.
/// </summary>
public interface IImageVariantRenderer
{
    /// <summary>
    ///     Reads the intrinsic pixel width of <paramref name="originalBytes"/> without
    ///     fully decoding the pixel buffer. Used to apply the only-downscale rule.
    /// </summary>
    Result<int, Error> ReadWidth(byte[] originalBytes);

    /// <summary>
    ///     Renders a WebP variant of the given target width, preserving aspect ratio.
    ///     The caller is responsible for only requesting widths &lt; the original width.
    /// </summary>
    Task<Result<RenderedImage, Error>> RenderWebpAsync(
        byte[] originalBytes,
        int targetWidth,
        CancellationToken cancellationToken = default);
}
