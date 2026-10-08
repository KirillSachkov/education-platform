namespace FileService.Core.Caching;

public static class CacheKeys
{
    public static class FileDownloadUrl
    {
        // #646: variant width is part of the key so each responsive width (or the
        // original) caches its own presigned URL. `null` width = the original object.
        private static string WidthSuffix(int? variantWidth) =>
            variantWidth is { } w ? $":w:{w}" : string.Empty;

        /// <summary>
        ///     Cache key for a globally-cached (non-protected) presigned download URL.
        ///     <paramref name="variantWidth"/> distinguishes responsive variants (#646).
        /// </summary>
        public static string ById(Guid fileId, int? variantWidth = null) =>
            $"download-url:{fileId}{WidthSuffix(variantWidth)}";

        /// <summary>
        ///     Cache key for a per-user presigned download URL. Used for protected content
        ///     (e.g., markdown images/files attached to lessons/issues/articles) so that
        ///     revoking a user's entitlements prevents them from retrieving a shared URL
        ///     cached under another user's identity. <paramref name="variantWidth"/>
        ///     distinguishes responsive variants (#646).
        /// </summary>
        public static string ByIdAndUser(Guid fileId, Guid userId, int? variantWidth = null) =>
            $"download-url:{fileId}:user:{userId}{WidthSuffix(variantWidth)}";

        /// <summary>
        ///     Tag used to invalidate all cached entries (global, per-user, every width) for a file.
        /// </summary>
        public static string TagById(Guid fileId) => $"download-url-tag:{fileId}";
    }
}
