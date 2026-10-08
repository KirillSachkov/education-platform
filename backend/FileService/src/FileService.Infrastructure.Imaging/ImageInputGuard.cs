using System.Buffers.Binary;
using System.IO.Compression;
using CSharpFunctionalExtensions;
using FileService.Domain;
using SharedKernel;
using SkiaSharp;

namespace FileService.Infrastructure.Imaging;

/// <summary>
/// Container preflight before native codec creation, not a pixel decoder. Only upload
/// formats are accepted. Native codecs still validate the image and must fully decode it.
/// </summary>
internal static class ImageInputGuard
{
    // Matches the largest image upload policy. Metadata also has an aggregate bound,
    // including expanded PNG profiles/text, before native parsing can inflate them.
    private const int MAX_ENCODED_BYTES = 10 * 1024 * 1024;
    private const int MAX_METADATA_BYTES = 1024 * 1024;

    public static Result<SKSizeI, Error> ReadDimensions(byte[] bytes)
    {
        if (bytes.Length > MAX_ENCODED_BYTES)
        {
            return TooLarge();
        }

        ReadOnlySpan<byte> input = bytes;
        ReadOnlySpan<byte> pngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (input.StartsWith(pngSignature))
        {
            return ReadPng(input);
        }

        if (input.Length >= 2 && input[0] == 0xff && input[1] == 0xd8)
        {
            return ReadJpeg(input);
        }

        if (input.Length >= 12 && input[..4].SequenceEqual("RIFF"u8) && input.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return ReadWebp(input);
        }

        return DecodeFailed();
    }

    private static Result<SKSizeI, Error> ReadPng(ReadOnlySpan<byte> input)
    {
        if (input.Length < 33 || !input.Slice(12, 4).SequenceEqual("IHDR"u8)
            || BinaryPrimitives.ReadUInt32BigEndian(input.Slice(8, 4)) != 13)
        {
            return DecodeFailed();
        }

        Result<SKSizeI, Error> dimensions = CheckDimensions(
            BinaryPrimitives.ReadUInt32BigEndian(input.Slice(16, 4)),
            BinaryPrimitives.ReadUInt32BigEndian(input.Slice(20, 4)));
        if (dimensions.IsFailure)
        {
            return dimensions.Error;
        }

        long metadataBytes = 0;
        bool hasPixels = false;
        for (int offset = 8; offset <= input.Length - 12;)
        {
            uint length = BinaryPrimitives.ReadUInt32BigEndian(input.Slice(offset, 4));
            if (length > input.Length - offset - 12)
            {
                return DecodeFailed();
            }

            ReadOnlySpan<byte> type = input.Slice(offset + 4, 4);
            ReadOnlySpan<byte> data = input.Slice(offset + 8, (int)length);
            if (type.SequenceEqual("acTL"u8) || type.SequenceEqual("fcTL"u8) || type.SequenceEqual("fdAT"u8))
            {
                return DecodeFailed();
            }

            if (type.SequenceEqual("IDAT"u8))
            {
                hasPixels = true;
            }
            else if (!type.SequenceEqual("IHDR"u8) && !type.SequenceEqual("IEND"u8))
            {
                metadataBytes += length;
                if (metadataBytes > MAX_METADATA_BYTES)
                {
                    return TooLarge();
                }

                // iCCP and zTXt: keyword, NUL, compression method, zlib payload.
                int compressedOffset = -1;
                if (type.SequenceEqual("iCCP"u8) || type.SequenceEqual("zTXt"u8))
                {
                    int separator = data.IndexOf((byte)0);
                    if (separator is < 1 or > 79 || separator + 2 >= data.Length || data[separator + 1] != 0)
                    {
                        return DecodeFailed();
                    }

                    compressedOffset = separator + 2;
                }
                else if (type.SequenceEqual("iTXt"u8))
                {
                    int separator = data.IndexOf((byte)0);
                    if (separator is < 1 or > 79 || separator + 3 > data.Length
                        || data[separator + 1] > 1 || data[separator + 2] != 0)
                    {
                        return DecodeFailed();
                    }

                    int languageEnd = data[(separator + 3)..].IndexOf((byte)0);
                    if (languageEnd < 0)
                    {
                        return DecodeFailed();
                    }

                    int translatedStart = separator + 4 + languageEnd;
                    int translatedEnd = data[translatedStart..].IndexOf((byte)0);
                    if (translatedEnd < 0)
                    {
                        return DecodeFailed();
                    }

                    if (data[separator + 1] == 1)
                    {
                        compressedOffset = translatedStart + translatedEnd + 1;
                    }
                }

                if (compressedOffset >= 0)
                {
                    Result<int, Error> expanded = CountExpandedMetadata(data[compressedOffset..], (int)(MAX_METADATA_BYTES - metadataBytes));
                    if (expanded.IsFailure)
                    {
                        return expanded.Error;
                    }

                    metadataBytes += expanded.Value;
                }
            }

            offset += (int)length + 12;
            if (type.SequenceEqual("IEND"u8))
            {
                return length == 0 && hasPixels && offset == input.Length ? dimensions : DecodeFailed();
            }
        }

        return DecodeFailed();
    }

    private static Result<SKSizeI, Error> ReadJpeg(ReadOnlySpan<byte> input)
    {
        Result<SKSizeI, Error> dimensions = DecodeFailed();
        long metadataBytes = 0;
        bool entropy = false;
        int offset = 2;
        while (offset < input.Length)
        {
            if (input[offset++] != 0xff)
            {
                if (entropy)
                {
                    continue;
                }

                return DecodeFailed();
            }

            while (offset < input.Length && input[offset] == 0xff)
            {
                offset++;
            }

            if (offset >= input.Length)
            {
                return DecodeFailed();
            }

            byte marker = input[offset++];
            if (entropy && (marker == 0 || marker is >= 0xd0 and <= 0xd7))
            {
                continue;
            }

            if (marker == 0xd9)
            {
                return dimensions;
            }

            if (offset > input.Length - 2)
            {
                return DecodeFailed();
            }

            int length = BinaryPrimitives.ReadUInt16BigEndian(input.Slice(offset, 2));
            if (length < 2 || length > input.Length - offset)
            {
                return DecodeFailed();
            }

            ReadOnlySpan<byte> data = input.Slice(offset + 2, length - 2);
            if (marker is >= 0xe0 and <= 0xef or 0xfe)
            {
                metadataBytes += data.Length;
                if (metadataBytes > MAX_METADATA_BYTES)
                {
                    return TooLarge();
                }
            }

            if (marker is 0xc0 or 0xc1 or 0xc2)
            {
                if (data.Length < 6 || dimensions.IsSuccess)
                {
                    return DecodeFailed();
                }

                dimensions = CheckDimensions(BinaryPrimitives.ReadUInt16BigEndian(data.Slice(3, 2)),
                    BinaryPrimitives.ReadUInt16BigEndian(data.Slice(1, 2)));
                if (dimensions.IsFailure)
                {
                    return dimensions.Error;
                }
            }

            offset += length;
            entropy = marker == 0xda;
        }

        return DecodeFailed();
    }

    private static Result<SKSizeI, Error> ReadWebp(ReadOnlySpan<byte> input)
    {
        if (BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(4, 4)) != input.Length - 8)
        {
            return DecodeFailed();
        }

        Result<SKSizeI, Error> dimensions = DecodeFailed();
        long metadataBytes = 0;
        int images = 0;
        for (int offset = 12; offset <= input.Length - 8;)
        {
            ReadOnlySpan<byte> type = input.Slice(offset, 4);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(offset + 4, 4));
            long next = (long)offset + 8 + length + (length & 1);
            if (next > input.Length)
            {
                return DecodeFailed();
            }

            ReadOnlySpan<byte> data = input.Slice(offset + 8, (int)length);
            Result<SKSizeI, Error>? imageDimensions = null;
            if (type.SequenceEqual("ANIM"u8) || type.SequenceEqual("ANMF"u8))
            {
                return DecodeFailed();
            }

            if (type.SequenceEqual("VP8X"u8))
            {
                if (data.Length != 10 || (data[0] & 2) != 0 || dimensions.IsSuccess)
                {
                    return DecodeFailed();
                }

                dimensions = CheckDimensions(ReadUInt24(data.Slice(4, 3)) + 1, ReadUInt24(data.Slice(7, 3)) + 1);
                if (dimensions.IsFailure)
                {
                    return dimensions.Error;
                }
            }
            else if (type.SequenceEqual("VP8 "u8))
            {
                if (data.Length < 10 || !data.Slice(3, 3).SequenceEqual(new byte[] { 0x9d, 1, 0x2a }))
                {
                    return DecodeFailed();
                }

                imageDimensions = CheckDimensions((uint)(BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(6, 2)) & 0x3fff),
                    (uint)(BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(8, 2)) & 0x3fff));
            }
            else if (type.SequenceEqual("VP8L"u8))
            {
                if (data.Length < 5 || data[0] != 0x2f)
                {
                    return DecodeFailed();
                }

                uint bits = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(1, 4));
                imageDimensions = CheckDimensions((bits & 0x3fff) + 1, ((bits >> 14) & 0x3fff) + 1);
            }
            else if (!type.SequenceEqual("ALPH"u8))
            {
                metadataBytes += length;
                if (metadataBytes > MAX_METADATA_BYTES)
                {
                    return TooLarge();
                }
            }

            if (imageDimensions is { } image)
            {
                if (image.IsFailure)
                {
                    return image.Error;
                }

                if (++images != 1 || (dimensions.IsSuccess && dimensions.Value != image.Value))
                {
                    return DecodeFailed();
                }

                dimensions = image;
            }

            offset = (int)next;
            if (offset == input.Length)
            {
                return images == 1 ? dimensions : DecodeFailed();
            }
        }

        return DecodeFailed();
    }

    private static uint ReadUInt24(ReadOnlySpan<byte> bytes) => (uint)(bytes[0] | (bytes[1] << 8) | (bytes[2] << 16));

    private static Result<SKSizeI, Error> CheckDimensions(uint width, uint height)
    {
        if (width == 0 || height == 0)
        {
            return DecodeFailed();
        }

        if (width > ImageVariantPolicy.MAX_DIMENSION || height > ImageVariantPolicy.MAX_DIMENSION
            || (long)width * height > ImageVariantPolicy.MAX_PIXELS)
        {
            return TooLarge();
        }

        return new SKSizeI((int)width, (int)height);
    }

    private static Result<int, Error> CountExpandedMetadata(ReadOnlySpan<byte> compressed, int budget)
    {
        try
        {
            using var input = new MemoryStream(compressed.ToArray(), writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            Span<byte> buffer = stackalloc byte[4096];
            int total = 0;
            int read;
            while ((read = zlib.Read(buffer)) != 0)
            {
                total += read;
                if (total > budget)
                {
                    return TooLarge();
                }
            }

            return total;
        }
        catch (InvalidDataException)
        {
            return DecodeFailed();
        }
    }

    private static Error DecodeFailed() =>
        Error.Validation(ImageVariantPolicy.ERROR_DECODE_FAILED, "Не удалось прочитать изображение");

    private static Error TooLarge() =>
        Error.Validation(ImageVariantPolicy.ERROR_TOO_LARGE, "Изображение слишком большое для обработки");
}