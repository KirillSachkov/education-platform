namespace FileService.Core.Caching;

/// <summary>
///     Computes the server-side (HybridCache) TTL for a cached presigned download URL
///     that <c>GET /files/{id}/content</c> hands back inside a browser-cacheable 302 redirect.
/// </summary>
/// <remarks>
///     The presigned URL lives behind two caches: this HybridCache entry, and the browser's
///     own cache of the 302 (<c>Cache-Control: max-age=…, immutable</c>). The browser may keep
///     serving the cached redirect — and thus the presigned URL inside it — for up to
///     <c>clientCacheMaxAge</c> after FileService last handed it out. So the invariant that must
///     hold is <c>serverCacheTtl + clientCacheMaxAge + skew ≤ presignedLifetime</c>; otherwise the
///     browser replays a cached redirect to an already-expired presigned URL and S3 answers
///     <c>403 "Request has expired"</c> (issue #438 — broken course covers). Reserving only a
///     fixed buffer (ignoring the browser window) is what opened the stale window.
/// </remarks>
public static class PresignedUrlCachePolicy
{
    // Hard cap regardless of presigned lifetime — limits how long a leaked URL can be
    // replayed out of the cache, independent of the entitlement check done per request.
    public static readonly TimeSpan MAX_SERVER_CACHE_TTL = TimeSpan.FromHours(4);

    // Clock-skew / URL-generation-latency margin reserved on top of the browser cache window.
    private static readonly TimeSpan _shortUrlSkewBuffer = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan _defaultUrlSkewBuffer = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     Longest the server may reuse one presigned URL so that, even after the browser caches
    ///     the resulting 302 for <paramref name="clientCacheMaxAge"/>, the URL is still unexpired.
    /// </summary>
    public static TimeSpan ComputeServerCacheTtl(TimeSpan presignedLifetime, TimeSpan clientCacheMaxAge)
    {
        TimeSpan skewBuffer = presignedLifetime <= TimeSpan.FromMinutes(10)
            ? _shortUrlSkewBuffer
            : _defaultUrlSkewBuffer;

        // Reserve the full browser cache window plus a skew margin, so the presigned URL
        // outlives every replay of the cached redirect.
        TimeSpan reserve = clientCacheMaxAge + skewBuffer;

        TimeSpan candidate = presignedLifetime > reserve
            ? presignedLifetime - reserve
            : _shortUrlSkewBuffer;

        return candidate < MAX_SERVER_CACHE_TTL ? candidate : MAX_SERVER_CACHE_TTL;
    }
}
