namespace FlashSale.Infrastructure.Payments.Http;

internal sealed class PaymentResponseBufferingHandler : DelegatingHandler
{
    private const int MaxResponseBytes = 64 * 1024;
    private const int BufferSize = 4096;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        try
        {
            // HttpClient normally buffers after delegating handlers return, outside the resilience timeout.
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var body = new MemoryStream();
            var buffer = new byte[BufferSize];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) != 0)
            {
                if (body.Length + read > MaxResponseBytes)
                {
                    throw new HttpRequestException("Payment response exceeds the permitted size.");
                }

                body.Write(buffer, 0, read);
            }

            var content = new ByteArrayContent(body.ToArray());
            foreach (var header in response.Content.Headers)
            {
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            response.Content.Dispose();
            response.Content = content;
            return response;
        }
        catch (IOException exception)
        {
            response.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
            throw new HttpRequestException("Payment response was interrupted.", exception);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }
}
