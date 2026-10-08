using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Core.HttpCommunication;

public partial class LoggingDelegatingHandler : DelegatingHandler
{
    private readonly ILogger<LoggingDelegatingHandler> _logger;

    public LoggingDelegatingHandler(ILogger<LoggingDelegatingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        LogRequestStarting(_logger, request.Method, request.RequestUri);

        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);

        sw.Stop();

        LogRequestCompleted(_logger, request.Method, request.RequestUri, (int)response.StatusCode, sw.ElapsedMilliseconds);

        return response;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "HTTP {Method} {Uri} starting")]
    private static partial void LogRequestStarting(ILogger logger, HttpMethod method, Uri? uri);

    [LoggerMessage(Level = LogLevel.Information, Message = "HTTP {Method} {Uri} -> {StatusCode} in {Elapsed}ms")]
    private static partial void LogRequestCompleted(ILogger logger, HttpMethod method, Uri? uri, int statusCode, long elapsed);
}
