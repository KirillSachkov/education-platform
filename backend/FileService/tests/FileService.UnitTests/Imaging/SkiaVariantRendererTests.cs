using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using CSharpFunctionalExtensions;
using FileService.Core.Imaging;
using FileService.Domain;
using FileService.Infrastructure.Imaging;
using SharedKernel;
using SkiaSharp;

namespace FileService.UnitTests.Imaging;

public sealed class SkiaVariantRendererTests
{
    private const int MEBIBYTE = 1024 * 1024;

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public async Task RenderWebpAsync_AllowedFormat_PreservesAspectRatio(SKEncodedImageFormat format)
    {
        byte[] original = CreateImage(format, 1000, 600);
        using var renderer = new SkiaVariantRenderer();

        Result<int, Error> width = renderer.ReadWidth(original);
        Result<RenderedImage, Error> result = await renderer.RenderWebpAsync(original, 320);

        Assert.True(width.IsSuccess);
        Assert.Equal(1000, width.Value);
        Assert.True(result.IsSuccess);
        Assert.Equal((320, 192), (result.Value.Width, result.Value.Height));
        Assert.Equal(ImageVariantPolicy.VARIANT_CONTENT_TYPE, result.Value.ContentType);
        using var stream = new SKMemoryStream(result.Value.Content);
        using SKCodec codec = SKCodec.Create(stream);
        Assert.NotNull(codec);
        Assert.Equal(SKEncodedImageFormat.Webp, codec.EncodedFormat);
        Assert.Equal((320, 192), (codec.Info.Width, codec.Info.Height));
        using SKBitmap pixels = Decode(result.Value.Content);
        Assert.Equal((320, 192), (pixels.Width, pixels.Height));
    }

    [Theory]
    [InlineData(200, 150, 320, 200, 150)]
    [InlineData(640, 480, 640, 640, 480)]
    [InlineData(1000, 3000, 320, 320, 960)]
    [InlineData(1000, 1, 320, 320, 1)]
    public async Task RenderWebpAsync_DifferentShapes_NeverUpscales(
        int width, int height, int targetWidth, int expectedWidth, int expectedHeight)
    {
        using var renderer = new SkiaVariantRenderer();

        Result<RenderedImage, Error> result = await renderer.RenderWebpAsync(
            CreateImage(SKEncodedImageFormat.Png, width, height), targetWidth);

        Assert.True(result.IsSuccess);
        using SKBitmap pixels = Decode(result.Value.Content);
        Assert.Equal((expectedWidth, expectedHeight), (pixels.Width, pixels.Height));
        Assert.Equal((expectedWidth, expectedHeight), (result.Value.Width, result.Value.Height));
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public async Task RenderWebpAsync_TransparentImage_PreservesAlpha(SKEncodedImageFormat format)
    {
        using var renderer = new SkiaVariantRenderer();
        byte[] original = CreateImage(format, 1000, 600, new SKColor(40, 80, 120, 96));

        Result<RenderedImage, Error> result = await renderer.RenderWebpAsync(original, 320);

        Assert.True(result.IsSuccess);
        using SKBitmap pixels = Decode(result.Value.Content);
        SKColor color = pixels.GetPixel(160, 96);
        Assert.InRange(color.Alpha, 90, 102);
        Assert.InRange(color.Red, 28, 52);
        Assert.InRange(color.Green, 68, 92);
        Assert.InRange(color.Blue, 108, 132);
    }

    [Theory]
    [InlineData(20_000, 20_000)]
    [InlineData(15_000, 100)]
    [InlineData(100, 15_000)]
    [InlineData(8_000, 5_001)]
    public async Task OversizedDeclaredDimensions_ReturnTooLargeBeforePixelDecode(int width, int height)
    {
        byte[] original = PngWithForgedDimensions(width, height);

        await AssertRejectedAsync(original, ImageVariantPolicy.ERROR_TOO_LARGE);
    }

    [Theory]
    [InlineData(12_000, 1)]
    [InlineData(1, 12_000)]
    [InlineData(8_000, 5_000)]
    public void ReadWidth_DimensionsExactlyWithinBudget_DoesNotDecodePixels(int width, int height)
    {
        using var renderer = new SkiaVariantRenderer();

        Result<int, Error> result = renderer.ReadWidth(PngWithForgedDimensions(width, height));

        Assert.True(result.IsSuccess);
        Assert.Equal(width, result.Value);
    }

    [Fact]
    public async Task OversizedEncodedInput_ReturnsTooLarge()
    {
        byte[] original = new byte[10 * MEBIBYTE + 1];
        CreateImage(SKEncodedImageFormat.Png, 8, 8).CopyTo(original, 0);

        await AssertRejectedAsync(original, ImageVariantPolicy.ERROR_TOO_LARGE);
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public async Task OversizedAggregateMetadata_ReturnsTooLarge(SKEncodedImageFormat format)
    {
        byte[] original = CreateImage(format, 8, 8);
        byte[] metadata = new byte[600_000];
        if (format == SKEncodedImageFormat.Png)
        {
            original = AddPngChunks(original, PngChunk("tEXt", metadata), PngChunk("tEXt", metadata));
        }
        else if (format == SKEncodedImageFormat.Jpeg)
        {
            for (int i = 0; i < 18; i++)
            {
                original = AddJpegSegment(original, 0xE1, new byte[60_000]);
            }
        }
        else
        {
            original = AddWebpChunks(original, WebpChunk("EXIF", metadata), WebpChunk("XMP ", metadata));
        }

        await AssertRejectedAsync(original, ImageVariantPolicy.ERROR_TOO_LARGE);
    }

    [Theory]
    [InlineData("iCCP")]
    [InlineData("zTXt")]
    [InlineData("iTXt")]
    public async Task CompressedPngMetadata_ExpandedOverBudget_ReturnsTooLarge(string type)
    {
        byte[] compressed = Compress(new byte[MEBIBYTE + 1]);
        byte[] prefix = type == "iTXt" ? "test\0\u0001\0\0\0"u8.ToArray() : "test\0\0"u8.ToArray();
        byte[] original = AddPngChunks(CreateImage(SKEncodedImageFormat.Png, 8, 8),
            PngChunk(type, [.. prefix, .. compressed]));
        Assert.True(original.Length < 10_000);

        await AssertRejectedAsync(original, ImageVariantPolicy.ERROR_TOO_LARGE);
    }

    [Theory]
    [InlineData("acTL")]
    [InlineData("fcTL")]
    [InlineData("fdAT")]
    public async Task PngAnimationChunks_AreRejectedEvenWithoutCompleteAnimation(string type)
    {
        byte[] data = new byte[type == "acTL" ? 8 : type == "fcTL" ? 26 : 4];
        if (type == "acTL")
        {
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0, 4), 2);
        }

        byte[] original = AddPngChunks(CreateImage(SKEncodedImageFormat.Png, 8, 8), PngChunk(type, data));

        await AssertRejectedAsync(original, ImageVariantPolicy.ERROR_DECODE_FAILED);
    }

    [Fact]
    public async Task AnimatedWebp_IsRejected()
    {
        byte[] original = CreateAnimatedWebp();
        using var stream = new SKMemoryStream(original);
        using SKCodec codec = SKCodec.Create(stream);
        Assert.NotNull(codec);
        Assert.True(codec.FrameCount > 1);

        await AssertRejectedAsync(original, ImageVariantPolicy.ERROR_DECODE_FAILED);
    }

    [Fact]
    public async Task WebpAnimationFlagWithoutFrames_IsRejected()
    {
        byte[] extendedHeader = new byte[10];
        extendedHeader[0] = 2;
        extendedHeader[4] = 7;
        extendedHeader[7] = 7;
        byte[] original = AddWebpChunks(CreateImage(SKEncodedImageFormat.Webp, 8, 8),
            WebpChunk("VP8X", extendedHeader));

        await AssertRejectedAsync(original, ImageVariantPolicy.ERROR_DECODE_FAILED);
    }

    [Fact]
    public async Task MetadataWithSmallPayload_IsStrippedFromOutput()
    {
        byte[] original = AddPngChunks(CreateImage(SKEncodedImageFormat.Png, 1000, 600),
            PngChunk("tEXt", "Comment\0private-input-marker"u8.ToArray()));
        using var renderer = new SkiaVariantRenderer();

        Result<RenderedImage, Error> result = await renderer.RenderWebpAsync(original, 320);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(WebpChunks(result.Value.Content), c => c.Type is "EXIF" or "XMP " or "ICCP");
        Assert.DoesNotContain("private-input-marker", Encoding.Latin1.GetString(result.Value.Content), StringComparison.Ordinal);
    }

    [Fact]
    public async Task JpegExifOrientation_PreservesRawDimensionsAndStripsExif()
    {
        // TIFF little-endian IFD: Orientation=6 (rotate 90 degrees clockwise).
        byte[] exif = [.. "Exif\0\0"u8.ToArray(),
            0x49, 0x49, 0x2A, 0, 8, 0, 0, 0, 1, 0,
            0x12, 0x01, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0];
        byte[] original = AddJpegSegment(CreateImage(SKEncodedImageFormat.Jpeg, 1000, 600), 0xE1, exif);
        using var stream = new SKMemoryStream(original);
        using SKCodec codec = SKCodec.Create(stream);
        Assert.Equal(SKEncodedOrigin.RightTop, codec.EncodedOrigin);
        using var renderer = new SkiaVariantRenderer();

        Result<int, Error> width = renderer.ReadWidth(original);
        Result<RenderedImage, Error> result = await renderer.RenderWebpAsync(original, 320);

        Assert.True(width.IsSuccess);
        Assert.Equal(1000, width.Value);
        Assert.True(result.IsSuccess);
        using SKBitmap pixels = Decode(result.Value.Content);
        Assert.Equal((320, 192), (pixels.Width, pixels.Height));
        Assert.DoesNotContain(WebpChunks(result.Value.Content), c => c.Type is "EXIF" or "XMP " or "ICCP");
    }

    [Fact]
    public async Task IncompletePixelData_WithValidContainer_IsNotRendered()
    {
        // Keep RIFF and chunk framing valid while removing the end of the VP8 payload.
        byte[] original = CreateImage(SKEncodedImageFormat.Webp, 1000, 600)[..^16];
        BinaryPrimitives.WriteInt32LittleEndian(original.AsSpan(4, 4), original.Length - 8);
        BinaryPrimitives.WriteInt32LittleEndian(original.AsSpan(16, 4), original.Length - 20);
        Assert.Equal("VP8 ", Assert.Single(WebpChunks(original)).Type);
        using var stream = new SKMemoryStream(original);
        using SKCodec codec = SKCodec.Create(stream);
        Assert.NotNull(codec);
        using var pixels = new SKBitmap(new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        Assert.Equal(SKCodecResult.IncompleteInput, codec.GetPixels(pixels.Info, pixels.GetPixels()));
        using var renderer = new SkiaVariantRenderer();

        Result<RenderedImage, Error> result = await renderer.RenderWebpAsync(original, 320);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Error.Messages, m => m.Code == ImageVariantPolicy.ERROR_DECODE_FAILED);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("gif")]
    public async Task UnsupportedOrCorruptInput_ReturnsDecodeFailed(string fixture)
    {
        byte[] original = fixture == "gif"
            ? Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7")
            : "not an image"u8.ToArray();

        await AssertRejectedAsync(original, ImageVariantPolicy.ERROR_DECODE_FAILED);
    }

    [Fact]
    public async Task TruncatedChunkLength_ReturnsDecodeFailed()
    {
        byte[] original = CreateImage(SKEncodedImageFormat.Png, 8, 8);
        BinaryPrimitives.WriteUInt32BigEndian(original.AsSpan(8, 4), uint.MaxValue);

        await AssertRejectedAsync(original, ImageVariantPolicy.ERROR_DECODE_FAILED);
    }

    [Fact]
    public async Task RenderWebpAsync_PreCancelledToken_PropagatesCancellation()
    {
        using var renderer = new SkiaVariantRenderer();
        byte[] original = CreateImage(SKEncodedImageFormat.Png, 1000, 600);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => renderer.RenderWebpAsync(original, 320, new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task RenderWebpAsync_CancelledWhileWaitingForRenderSlot_PropagatesCancellation()
    {
        using var renderer = new SkiaVariantRenderer();
        byte[] original = CreateImage(SKEncodedImageFormat.Png, 1000, 600);
        // Hold the native-render slot deterministically instead of timing a slow decode.
        var gate = (SemaphoreSlim?)typeof(SkiaVariantRenderer)
            .GetField("_renderGate", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(renderer);
        Assert.NotNull(gate);
        await gate.WaitAsync();
        try
        {
            using var cancellation = new CancellationTokenSource();
            Task<Result<RenderedImage, Error>> waiting = renderer.RenderWebpAsync(original, 320, cancellation.Token);
            Assert.False(waiting.IsCompleted);

            await cancellation.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        }
        finally
        {
            gate.Release();
        }
    }

    [Fact]
    public async Task RenderWebpAsync_ValidLinearIccProfile_ConvertsPixelsToSrgbAndStripsProfile()
    {
        byte[] profile = CreateLinearSrgbProfile();
        byte[] original = AddPngChunks(CreateImage(SKEncodedImageFormat.Png, 1000, 600),
            PngChunk("iCCP", [.. "test\0\0"u8.ToArray(), .. Compress(profile)]));
        using var stream = new SKMemoryStream(original);
        using SKCodec codec = SKCodec.Create(stream);
        using SKColorSpace sourceProfile = codec.Info.ColorSpace;
        Assert.NotNull(sourceProfile);
        Assert.False(sourceProfile.IsSrgb);
        using var renderer = new SkiaVariantRenderer();

        Result<RenderedImage, Error> result = await renderer.RenderWebpAsync(original, 320);

        Assert.True(result.IsSuccess);
        using SKBitmap pixels = Decode(result.Value.Content);
        SKColor color = pixels.GetPixel(160, 96);
        // Linear RGB samples (40,80,120) become approximately (110,152,182) in sRGB.
        Assert.InRange(color.Red, 100, 120);
        Assert.InRange(color.Green, 142, 162);
        Assert.InRange(color.Blue, 172, 192);
        Assert.DoesNotContain(WebpChunks(result.Value.Content), c => c.Type is "EXIF" or "XMP " or "ICCP");
    }

    private static async Task AssertRejectedAsync(byte[] original, string errorCode)
    {
        using var renderer = new SkiaVariantRenderer();
        Result<int, Error> width = renderer.ReadWidth(original);
        Result<RenderedImage, Error> rendered = await renderer.RenderWebpAsync(original, 320);

        Assert.True(width.IsFailure);
        Assert.True(rendered.IsFailure);
        Assert.Contains(width.Error.Messages, m => m.Code == errorCode);
        Assert.Contains(rendered.Error.Messages, m => m.Code == errorCode);
    }

    private static byte[] CreateImage(SKEncodedImageFormat format, int width, int height, SKColor? color = null)
    {
        using var pixels = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        pixels.Erase(color ?? new SKColor(40, 80, 120));
        using var image = SKImage.FromBitmap(pixels);
        using SKData encoded = image.Encode(format, 80);
        return encoded.ToArray();
    }

    private static SKBitmap Decode(byte[] original)
    {
        using var stream = new SKMemoryStream(original);
        using SKCodec codec = SKCodec.Create(stream);
        Assert.NotNull(codec);
        var pixels = new SKBitmap(new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        Assert.Equal(SKCodecResult.Success, codec.GetPixels(pixels.Info, pixels.GetPixels()));
        return pixels;
    }

    private static byte[] PngWithForgedDimensions(int width, int height)
    {
        byte[] original = CreateImage(SKEncodedImageFormat.Png, 8, 8);
        BinaryPrimitives.WriteInt32BigEndian(original.AsSpan(16, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(original.AsSpan(20, 4), height);
        BinaryPrimitives.WriteUInt32BigEndian(original.AsSpan(29, 4), Crc32(original.AsSpan(12, 17)));
        return original;
    }

    private static byte[] PngChunk(string type, byte[] data)
    {
        byte[] chunk = new byte[data.Length + 12];
        BinaryPrimitives.WriteInt32BigEndian(chunk.AsSpan(0, 4), data.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(data.Length + 8, 4), Crc32(chunk.AsSpan(4, data.Length + 4)));
        return chunk;
    }

    private static byte[] AddPngChunks(byte[] original, params byte[][] chunks) =>
        [.. original.AsSpan(0, 33), .. chunks.SelectMany(c => c), .. original.AsSpan(33)];

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var compressed = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            compressed.Write(data);
        }

        return output.ToArray();
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static byte[] AddJpegSegment(byte[] original, byte marker, byte[] data)
    {
        byte[] segment = new byte[data.Length + 4];
        segment[0] = 0xFF;
        segment[1] = marker;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2, 2), checked((ushort)(data.Length + 2)));
        data.CopyTo(segment, 4);
        return [.. original.AsSpan(0, 2), .. segment, .. original.AsSpan(2)];
    }

    private static byte[] WebpChunk(string type, byte[] data)
    {
        byte[] chunk = new byte[8 + data.Length + (data.Length & 1)];
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 0);
        BinaryPrimitives.WriteInt32LittleEndian(chunk.AsSpan(4, 4), data.Length);
        data.CopyTo(chunk, 8);
        return chunk;
    }

    private static byte[] AddWebpChunks(byte[] original, params byte[][] chunks)
    {
        byte[] result = [.. original.AsSpan(0, 12), .. chunks.SelectMany(c => c), .. original.AsSpan(12)];
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4, 4), result.Length - 8);
        return result;
    }

    private static List<(string Type, byte[] Data)> WebpChunks(byte[] original)
    {
        var chunks = new List<(string Type, byte[] Data)>();
        for (int offset = 12; offset < original.Length;)
        {
            int length = BinaryPrimitives.ReadInt32LittleEndian(original.AsSpan(offset + 4, 4));
            chunks.Add((Encoding.ASCII.GetString(original, offset, 4), original.AsSpan(offset + 8, length).ToArray()));
            offset += 8 + length + (length & 1);
        }

        return chunks;
    }

    private static byte[] CreateAnimatedWebp()
    {
        (string type, byte[] data) = WebpChunks(CreateImage(SKEncodedImageFormat.Webp, 8, 8))
            .Single(c => c.Type is "VP8 " or "VP8L");
        byte[] frame = new byte[16];
        frame[6] = 7; // frame width - 1, little-endian UInt24
        frame[9] = 7; // frame height - 1
        frame[12] = 10; // duration in milliseconds
        frame = [.. frame, .. WebpChunk(type, data)];
        byte[] extendedHeader = new byte[10];
        extendedHeader[0] = 2; // animated
        extendedHeader[4] = 7; // canvas width - 1
        extendedHeader[7] = 7; // canvas height - 1
        return AddWebpChunks([.. "RIFF"u8.ToArray(), 4, 0, 0, 0, .. "WEBP"u8.ToArray()],
            WebpChunk("VP8X", extendedHeader), WebpChunk("ANIM", new byte[6]),
            WebpChunk("ANMF", frame), WebpChunk("ANMF", frame));
    }

    private static byte[] CreateLinearSrgbProfile()
    {
        using SKColorSpace colorSpace = SKColorSpace.CreateSrgbLinear();
        using var pixels = new SKBitmap(new SKImageInfo(8, 8, SKColorType.Bgra8888, SKAlphaType.Premul, colorSpace));
        pixels.Erase(new SKColor(40, 80, 120));
        using var image = SKImage.FromBitmap(pixels);
        using SKData encoded = image.Encode(SKEncodedImageFormat.Webp, 80);
        return WebpChunks(encoded.ToArray()).Single(c => c.Type == "ICCP").Data;
    }
}