using System.Security.Claims;
using System.Threading.RateLimiting;
using FileService.Core.Features.AssetRegistry.UseCases;
using SharedKernel;

namespace FileService.Web.Configuration;

public static class FileRateLimiting
{
    /// <summary>
    /// Politika rate-limita dlya POST /files/uploads. Zaschiscaet ot DoS-ataki
    /// cherez massovyy initiate (skomprometirovannyy author-akkaunt zalivaet
    /// tysyachu PendingUpload-strok v media_assets).
    /// </summary>
    public const string FILE_UPLOAD_POLICY = "file-upload";

    public static IServiceCollection AddFileRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                Error error = Error.Failure(
                    "files.rate_limit.exceeded",
                    "Слишком много запросов на загрузку. Попробуйте через минуту.");
                Envelope<object> envelope = Envelope<object>.Fail(error);
                await context.HttpContext.Response.WriteAsJsonAsync(envelope, cancellationToken);
            };

            options.AddPolicy(FILE_UPLOAD_POLICY, httpContext =>
            {
                string partitionKey =
                    httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? httpContext.User.FindFirstValue("sub")
                    ?? httpContext.Connection.RemoteIpAddress?.ToString()
                    ?? "anonymous";

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    });
            });

            options.AddPolicy(BindAssetEndpoint.ASSET_BIND_RATE_LIMIT_POLICY, httpContext =>
            {
                string partitionKey =
                    httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? httpContext.User.FindFirstValue("sub")
                    ?? httpContext.Connection.RemoteIpAddress?.ToString()
                    ?? "anonymous";

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    });
            });
        });

        return services;
    }
}
