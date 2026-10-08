using CSharpFunctionalExtensions;
using FileService.Core.Imaging;
using FileService.Domain;
using SharedKernel;
using SkiaSharp;

namespace FileService.Infrastructure.Imaging;

/// <summary>
/// JPEG/PNG/WebP variants with bounded input, dimensions, metadata and one native render
/// at a time. These limits bound known allocations, not total native memory. Cancellation
/// is checked between synchronous native calls; it cannot interrupt a call in progress.
/// </summary>
public sealed class SkiaVariantRenderer : IImageVariantRenderer, IDisposable
{
    private readonly SemaphoreSlim _renderGate = new(1, 1);

    public Result<int, Error> ReadWidth(byte[] originalBytes)
    {
        if (originalBytes is null || originalBytes.Length == 0)
        {
            return GeneralErrors.ValueIsInvalid("originalBytes");
        }

        Result<SKSizeI, Error> header = ImageInputGuard.ReadDimensions(originalBytes);
        if (header.IsFailure)
        {
            return header.Error;
        }

        _renderGate.Wait();
        try
        {
            using var stream = new SKMemoryStream(originalBytes);
            using SKCodec? codec = SKCodec.Create(stream);
            return IsValidCodec(codec, header.Value) ? header.Value.Width : DecodeFailed();
        }
        finally
        {
            _renderGate.Release();
        }
    }

    public async Task<Result<RenderedImage, Error>> RenderWebpAsync(
        byte[] originalBytes,
        int targetWidth,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (originalBytes is null || originalBytes.Length == 0)
        {
            return GeneralErrors.ValueIsInvalid("originalBytes");
        }

        if (targetWidth <= 0)
        {
            return GeneralErrors.ValueIsInvalid("targetWidth");
        }

        Result<SKSizeI, Error> header = ImageInputGuard.ReadDimensions(originalBytes);
        if (header.IsFailure)
        {
            return header.Error;
        }

        await _renderGate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = new SKMemoryStream(originalBytes);
            using SKCodec? codec = SKCodec.Create(stream);
            if (!IsValidCodec(codec, header.Value))
            {
                return DecodeFailed();
            }

            // Convert bounded embedded profiles to sRGB pixels. Keep raw intrinsic
            // orientation, as before; fresh output carries no EXIF/XMP/IPTC/ICC metadata.
            using SKColorSpace colorSpace = SKColorSpace.CreateSrgb();
            var sourceInfo = new SKImageInfo(header.Value.Width, header.Value.Height,
                SKColorType.Bgra8888, SKAlphaType.Premul, colorSpace);
            cancellationToken.ThrowIfCancellationRequested();
            using var original = new SKBitmap(sourceInfo);
            if (original.GetPixels() == IntPtr.Zero
                || codec!.GetPixels(sourceInfo, original.GetPixels()) != SKCodecResult.Success)
            {
                return DecodeFailed();
            }

            cancellationToken.ThrowIfCancellationRequested();
            int width = Math.Min(targetWidth, sourceInfo.Width);
            int height = Math.Max(1, (int)Math.Round((double)sourceInfo.Height * width / sourceInfo.Width));
            var targetInfo = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul, colorSpace);
            using SKBitmap? resized = original.Resize(targetInfo, new SKSamplingOptions(SKCubicResampler.Mitchell));
            if (resized is null)
            {
                return DecodeFailed();
            }

            cancellationToken.ThrowIfCancellationRequested();
            // Pixels have already been converted to sRGB. Drop the profile association
            // before encoding, otherwise Skia emits a generated ICCP WebP chunk.
            using var outputPixels = new SKPixmap(targetInfo.WithColorSpace(null), resized.GetPixels(), resized.RowBytes);
            using SKImage image = SKImage.FromPixels(outputPixels);
            using SKData? encoded = image.Encode(SKEncodedImageFormat.Webp, ImageVariantPolicy.WEBP_QUALITY);
            if (encoded is null)
            {
                return DecodeFailed();
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new RenderedImage(width, height, encoded.ToArray(), ImageVariantPolicy.VARIANT_CONTENT_TYPE);
        }
        finally
        {
            _renderGate.Release();
        }
    }

    public void Dispose() => _renderGate.Dispose();

    private static bool IsValidCodec(SKCodec? codec, SKSizeI dimensions) =>
        codec is not null
        && codec.EncodedFormat is SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp
        && codec.Info.Width == dimensions.Width && codec.Info.Height == dimensions.Height
        && codec.FrameCount <= 1;

    private static Error DecodeFailed() =>
        Error.Validation(ImageVariantPolicy.ERROR_DECODE_FAILED, "Не удалось обработать изображение");
}