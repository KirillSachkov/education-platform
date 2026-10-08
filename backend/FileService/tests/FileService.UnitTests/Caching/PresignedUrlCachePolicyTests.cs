using FileService.Core.Caching;

namespace FileService.UnitTests.Caching;

public class PresignedUrlCachePolicyTests
{
    // Browser cache windows of the two Cache-Control headers GetFileContent sets.
    private static readonly TimeSpan _publicClientMaxAge = TimeSpan.FromSeconds(3600);
    private static readonly TimeSpan _protectedClientMaxAge = TimeSpan.FromSeconds(60);

    // Regression for #438: a course cover (public file) is served as a 302 redirect to a
    // presigned URL. With a 120-min presigned lifetime (prod) the server must NOT reuse a
    // cached URL so long that, once the browser also caches the 302 for its max-age window,
    // the presigned URL is already expired and S3 returns 403 "Request has expired".
    [Fact]
    public void ComputeServerCacheTtl_ProdPublicCover_LeavesRoomForBrowserCache()
    {
        TimeSpan presignedLifetime = TimeSpan.FromMinutes(120);

        TimeSpan serverTtl = PresignedUrlCachePolicy.ComputeServerCacheTtl(presignedLifetime, _publicClientMaxAge);

        // The whole point: server reuse window + browser cache window must fit inside the
        // presigned lifetime. Before the fix serverTtl was 115min → 115 + 60 = 175 > 120 (FAIL).
        Assert.True(
            serverTtl + _publicClientMaxAge < presignedLifetime,
            $"serverTtl ({serverTtl}) + browser maxAge ({_publicClientMaxAge}) must be < presigned lifetime ({presignedLifetime})");
    }

    // The invariant must hold across every configured combination of presigned lifetime and
    // client cache window the service actually uses (prod / Docker / dev × public / protected).
    [Theory]
    [InlineData(120, 3600)]   // prod public cover
    [InlineData(15, 60)]      // prod protected markdown asset
    [InlineData(10080, 3600)] // dev/Docker public (7-day presign)
    [InlineData(10080, 60)]   // dev/Docker protected
    public void ComputeServerCacheTtl_NeverOutlivesPresignedUrl(int presignedMinutes, int clientMaxAgeSeconds)
    {
        TimeSpan presignedLifetime = TimeSpan.FromMinutes(presignedMinutes);
        TimeSpan clientMaxAge = TimeSpan.FromSeconds(clientMaxAgeSeconds);

        TimeSpan serverTtl = PresignedUrlCachePolicy.ComputeServerCacheTtl(presignedLifetime, clientMaxAge);

        Assert.True(
            serverTtl + clientMaxAge < presignedLifetime,
            $"serverTtl ({serverTtl}) + browser maxAge ({clientMaxAge}) must be < presigned lifetime ({presignedLifetime})");
    }

    [Fact]
    public void ComputeServerCacheTtl_LongPresignedLifetime_IsCappedAtMax()
    {
        TimeSpan serverTtl = PresignedUrlCachePolicy.ComputeServerCacheTtl(
            TimeSpan.FromDays(7), _publicClientMaxAge);

        Assert.Equal(PresignedUrlCachePolicy.MAX_SERVER_CACHE_TTL, serverTtl);
    }

    [Fact]
    public void ComputeServerCacheTtl_ProtectedShortLifetime_StaysPositiveAndUnexpired()
    {
        TimeSpan presignedLifetime = TimeSpan.FromMinutes(15);

        TimeSpan serverTtl = PresignedUrlCachePolicy.ComputeServerCacheTtl(presignedLifetime, _protectedClientMaxAge);

        Assert.True(serverTtl > TimeSpan.Zero);
        Assert.True(serverTtl + _protectedClientMaxAge < presignedLifetime);
    }
}
