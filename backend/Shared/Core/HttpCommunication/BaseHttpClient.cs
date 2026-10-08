using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Core.HttpCommunication;

public abstract class BaseHttpClient
{
    private readonly string _serviceName;

    protected BaseHttpClient(
        HttpClient httpClient,
        ILogger logger,
        string serviceName)
    {
        HttpClient = httpClient;
        Logger = logger;
        _serviceName = serviceName;
    }

    protected HttpClient HttpClient { get; }

    protected ILogger Logger { get; }

    protected Task<Result<T, Error>> GetAsync<T>(
        string url,
        CancellationToken ct)
        => ExecuteAsync<T>(() => HttpClient.GetAsync(url, ct), url, ct);

    protected Task<Result<T?, Error>> GetOptionalAsync<T>(
        string url,
        CancellationToken ct)
        where T : class
        => ExecuteOptionalAsync<T>(() => HttpClient.GetAsync(url, ct), url, ct);

    protected Task<Result<T, Error>> PostAsync<TBody, T>(
        string url,
        TBody body,
        CancellationToken ct)
        => ExecuteAsync<T>(() => HttpClient.PostAsJsonAsync(url, body, ct), url, ct);

    protected Task<Result<T, Error>> PutAsync<TBody, T>(
        string url,
        TBody body,
        CancellationToken ct)
        => ExecuteAsync<T>(() => HttpClient.PutAsJsonAsync(url, body, ct), url, ct);

    protected Task<UnitResult<Error>> DeleteAsync(
        string url,
        CancellationToken ct)
        => ExecuteUnitAsync(() => HttpClient.DeleteAsync(url, ct), url, ct);

    private async Task<Result<T, Error>> ExecuteAsync<T>(
        Func<Task<HttpResponseMessage>> requestFactory,
        string url,
        CancellationToken ct)
    {
        try
        {
            using HttpResponseMessage response = await requestFactory();
            return await ParseEnvelopeAsync<T>(response, ct);
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(
                ex,
                "{Service} request failed for {Url}. StatusCode: {StatusCode}",
                _serviceName, url, ex.StatusCode);

            return Error.Failure(
                "service.unavailable",
                $"{_serviceName} is unavailable.").AsTransient();
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            Logger.LogWarning(
                ex,
                "{Service} request timeout for {Url}",
                _serviceName, url);

            return Error.Failure(
                "service.timeout",
                $"Request to {_serviceName} timed out.").AsTransient();
        }
        catch (JsonException ex)
        {
            Logger.LogError(
                ex,
                "{Service} returned invalid JSON for {Url}",
                _serviceName, url);

            return Error.Failure("http.invalid_json", "Service returned invalid JSON.");
        }
    }

    private async Task<Result<T?, Error>> ExecuteOptionalAsync<T>(
        Func<Task<HttpResponseMessage>> requestFactory,
        string url,
        CancellationToken ct)
        where T : class
    {
        try
        {
            using HttpResponseMessage response = await requestFactory();
            return await ParseOptionalEnvelopeAsync<T>(response, ct);
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(
                ex,
                "{Service} request failed for {Url}. StatusCode: {StatusCode}",
                _serviceName, url, ex.StatusCode);

            return Error.Failure(
                "service.unavailable",
                $"{_serviceName} is unavailable.").AsTransient();
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            Logger.LogWarning(
                ex,
                "{Service} request timeout for {Url}",
                _serviceName, url);

            return Error.Failure(
                "service.timeout",
                $"Request to {_serviceName} timed out.").AsTransient();
        }
        catch (JsonException ex)
        {
            Logger.LogError(
                ex,
                "{Service} returned invalid JSON for {Url}",
                _serviceName, url);

            return Error.Failure("http.invalid_json", "Service returned invalid JSON.");
        }
    }

    private async Task<UnitResult<Error>> ExecuteUnitAsync(
        Func<Task<HttpResponseMessage>> requestFactory,
        string url,
        CancellationToken ct)
    {
        try
        {
            using HttpResponseMessage response = await requestFactory();
            return await ParseUnitEnvelopeAsync(response, ct);
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(
                ex,
                "{Service} request failed for {Url}. StatusCode: {StatusCode}",
                _serviceName, url, ex.StatusCode);

            return Error.Failure(
                "service.unavailable",
                $"{_serviceName} is unavailable.").AsTransient();
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            Logger.LogWarning(
                ex,
                "{Service} request timeout for {Url}",
                _serviceName, url);

            return Error.Failure(
                "service.timeout",
                $"Request to {_serviceName} timed out.").AsTransient();
        }
        catch (JsonException ex)
        {
            Logger.LogError(
                ex,
                "{Service} returned invalid JSON for {Url}",
                _serviceName, url);

            return Error.Failure("http.invalid_json", "Service returned invalid JSON.");
        }
    }

    private async Task<Result<T, Error>> ParseEnvelopeAsync<T>(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        Envelope<T>? envelope = await response.Content
            .ReadFromJsonAsync<Envelope<T>>(ct);

        if (envelope is null)
        {
            Logger.LogError(
                "Failed to deserialize response. StatusCode: {StatusCode}",
                response.StatusCode);

            return Error.Failure(
                "http.deserialization_failed",
                "Failed to parse response.");
        }

        if (!response.IsSuccessStatusCode || envelope.IsError)
        {
            Error error = envelope.Error
                          ?? Error.Failure(
                              "http.unknown_error",
                              $"Service returned {(int)response.StatusCode}");

            Logger.LogWarning(
                "Service returned error. StatusCode: {StatusCode}, Error: {Error}",
                response.StatusCode,
                error.GetMessage());

            return error;
        }

        if (envelope.Result is null)
        {
            Logger.LogError("Service returned null result in successful envelope");

            return Error.Failure(
                "http.null_result",
                "Service returned null result.");
        }

        return envelope.Result;
    }

    private async Task<Result<T?, Error>> ParseOptionalEnvelopeAsync<T>(
        HttpResponseMessage response,
        CancellationToken ct)
        where T : class
    {
        if (response.StatusCode == HttpStatusCode.NoContent)
            return Result.Success<T?, Error>(null);

        Envelope<T>? envelope = await response.Content
            .ReadFromJsonAsync<Envelope<T>>(ct);

        if (envelope is null)
        {
            Logger.LogError(
                "Failed to deserialize response. StatusCode: {StatusCode}",
                response.StatusCode);

            return Error.Failure(
                "http.deserialization_failed",
                "Failed to parse response.");
        }

        if (!response.IsSuccessStatusCode || envelope.IsError)
        {
            Error error = envelope.Error
                          ?? Error.Failure(
                              "http.unknown_error",
                              $"Service returned {(int)response.StatusCode}");

            Logger.LogWarning(
                "Service returned error. StatusCode: {StatusCode}, Error: {Error}",
                response.StatusCode,
                error.GetMessage());

            return error;
        }

        return envelope.Result;
    }

    private async Task<UnitResult<Error>> ParseUnitEnvelopeAsync(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        Envelope? envelope = await response.Content
            .ReadFromJsonAsync<Envelope>(ct);

        if (envelope is null)
        {
            Logger.LogError(
                "Failed to deserialize response. StatusCode: {StatusCode}",
                response.StatusCode);

            return Error.Failure(
                "http.deserialization_failed",
                "Failed to parse response.");
        }

        if (!response.IsSuccessStatusCode || envelope.IsError)
        {
            Error error = envelope.Error
                          ?? Error.Failure(
                              "http.unknown_error",
                              $"Service returned {(int)response.StatusCode}");

            Logger.LogWarning(
                "Service returned error. StatusCode: {StatusCode}, Error: {Error}",
                response.StatusCode,
                error.GetMessage());

            return error;
        }

        return UnitResult.Success<Error>();
    }
}
