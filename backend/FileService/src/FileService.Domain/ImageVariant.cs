namespace FileService.Domain;

/// <summary>
///     A server-generated responsive variant of an image asset — a downscaled WebP
///     rendition stored under its own object-storage key. The original object is never
///     touched; variants are an additive, derived layer (issue #646).
/// </summary>
public sealed record ImageVariant
{
    public ImageVariant(int width, string storageKey, string contentType, long size)
    {
        Width = width;
        StorageKey = storageKey;
        ContentType = contentType;
        Size = size;
    }

    /// <summary>Pixel width of the rendered variant (one of <see cref="ImageVariantPolicy.Widths"/>).</summary>
    public int Width { get; init; }

    /// <summary>Object-storage key of the variant (derived from the original's key).</summary>
    public string StorageKey { get; init; }

    /// <summary>MIME type of the variant — always <c>image/webp</c> for now.</summary>
    public string ContentType { get; init; }

    /// <summary>Byte size of the variant object.</summary>
    public long Size { get; init; }
}
