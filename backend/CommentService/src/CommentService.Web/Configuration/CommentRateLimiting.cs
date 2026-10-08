using System.Security.Claims;
using System.Threading.RateLimiting;
using CommentService.Core.Features.Comments.UseCases;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace CommentService.Web.Configuration;

public static class CommentRateLimiting
{
    public static IServiceCollection AddCommentRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<CommentRateLimitOptions>()
            .BindConfiguration(CommentRateLimitOptions.SECTION_NAME)
            .Validate(
                options => options.CreatePermitLimit > 0,
                $"{nameof(CommentRateLimitOptions.CreatePermitLimit)} must be greater than 0.")
            .Validate(
                options => options.CreateWindowMinutes > 0,
                $"{nameof(CommentRateLimitOptions.CreateWindowMinutes)} must be greater than 0.")
            .ValidateOnStart();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                Error error = Error.Failure(
                    "comments.rate_limit.exceeded",
                    "Слишком много запросов. Попробуйте позже.");
                Envelope<object> envelope = Envelope<object>.Fail(error);

                await context.HttpContext.Response.WriteAsJsonAsync(envelope, cancellationToken);
            };

            options.AddPolicy(CreateCommentEndpoint.CREATE_COMMENT_RATE_LIMIT_POLICY, httpContext =>
            {
                string partitionKey =
                    httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? httpContext.User.FindFirstValue("sub")
                    ?? httpContext.Connection.RemoteIpAddress?.ToString()
                    ?? "anonymous";

                CommentRateLimitOptions rateLimitOptions = httpContext.RequestServices
                    .GetRequiredService<IOptions<CommentRateLimitOptions>>().Value;

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimitOptions.CreatePermitLimit,
                        Window = TimeSpan.FromMinutes(rateLimitOptions.CreateWindowMinutes),
                        QueueLimit = 0,
                    });
            });
        });

        return services;
    }
}
