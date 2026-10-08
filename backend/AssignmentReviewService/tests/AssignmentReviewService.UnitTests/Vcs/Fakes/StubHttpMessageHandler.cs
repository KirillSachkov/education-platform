using System.Net;
using System.Text;

namespace AssignmentReviewService.UnitTests.Vcs.Fakes;

/// <summary>
///     <see cref="HttpMessageHandler"/>-stub. Капчит каждый <see cref="HttpRequestMessage"/>
///     для пост-проверок, и отдаёт canned-ответ через caller-supplied responder.
///     <see cref="Sequence"/> — построитель N разных ответов на N последовательных
///     вызовов (для retry/cache-tests).
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _responder;

    public List<HttpRequestMessage> Requests { get; } = [];

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : this((req, _) => responder(req)) { }

    public StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    public static StubHttpMessageHandler Json(HttpStatusCode status, string json) =>
        new(_ => BuildResponse(status, json));

    public static StubHttpMessageHandler Sequence(params (HttpStatusCode Status, string Json)[] responses)
    {
        int index = 0;
        return new StubHttpMessageHandler((_, _) =>
        {
            (HttpStatusCode status, string json) = responses[Math.Min(index, responses.Length - 1)];
            index++;
            return BuildResponse(status, json);
        });
    }

    private static HttpResponseMessage BuildResponse(HttpStatusCode status, string? json)
    {
        HttpResponseMessage resp = new(status);
        if (!string.IsNullOrEmpty(json))
            resp.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return resp;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.FromResult(_responder(request, ct));
    }
}
