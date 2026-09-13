using Polly.CircuitBreaker;
using Polly.Timeout;

namespace FlashSale.IntegrationTests.Support;

internal sealed class ScriptedHandler(Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    public int Attempts { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        send(request, ++Attempts, cancellationToken);
}
