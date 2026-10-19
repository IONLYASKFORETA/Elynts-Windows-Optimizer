using System.Net;

namespace Elynt.Core.Tests;

/// <summary>Answers each URL with a fixed body (404 for any other URL), and remembers what was asked.</summary>
public sealed class StubHttpHandler : HttpMessageHandler
{
    public Dictionary<string, string> Bodies { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (Status != HttpStatusCode.OK)
            return new HttpResponseMessage(Status);
        return Bodies.TryGetValue(request.RequestUri!.ToString(), out var body)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }
            : new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(Send(request, cancellationToken));
}
