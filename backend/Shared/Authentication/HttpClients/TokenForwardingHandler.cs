using System.Net.Http.Headers;
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Http;
using SharedKernel;

namespace PlatformAuth.HttpClients;

public sealed class TokenForwardingHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ServiceTokenProvider _serviceTokenProvider;

    public TokenForwardingHandler(
        IHttpContextAccessor httpContextAccessor,
        ServiceTokenProvider serviceTokenProvider)
    {
        _httpContextAccessor = httpContextAccessor;
        _serviceTokenProvider = serviceTokenProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        bool isInternalRequest = request.RequestUri?.AbsolutePath
            .StartsWith("/internal/", StringComparison.OrdinalIgnoreCase) == true;

        string? authHeader = _httpContextAccessor.HttpContext?
            .Request.Headers.Authorization.ToString();

        if (!isInternalRequest && !string.IsNullOrEmpty(authHeader))
        {
            request.Headers.TryAddWithoutValidation("Authorization", authHeader);
        }
        else
        {
            Result<string, Error> result = await _serviceTokenProvider.GetTokenAsync(cancellationToken);

            if (result.IsSuccess)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", result.Value);
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
