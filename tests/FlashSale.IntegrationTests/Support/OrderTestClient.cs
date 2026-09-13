using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlashSale.Api.Contracts.Orders;
using FlashSale.Infrastructure.Outbox.Processing;
using Microsoft.Extensions.DependencyInjection;

namespace FlashSale.IntegrationTests.Support;

public static class OrderTestClient
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    public static readonly Guid UserId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    public static Task<HttpResponseMessage> CreateAsync(HttpClient client, string? key = null, int quantity = 1,
        IReadOnlyList<CreateOrderItemRequest>? items = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/orders")
        {
            Content = JsonContent.Create(new CreateOrderRequest(UserId, items ?? [new(SqlFixture.ProductId, quantity)]))
        };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N"));
        return SendAsync(client, request);
    }

    public static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request)
    {
        using (request) return await client.SendAsync(request);
    }

    public static async Task<OrderResponse> ReadAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrderResponse>(Json))!;
    }

    public static async Task<bool> ProcessAsync(SaleFactory factory, CancellationToken cancellationToken = default)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PaymentOutboxProcessor>().ProcessNextAsync(cancellationToken);
    }

}
