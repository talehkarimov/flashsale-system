using System.Net;
using System.Net.Http.Json;
using FlashSale.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace FlashSale.IntegrationTests.Products;

[Collection("SQL")]
public sealed class ProductReadTests(SqlFixture sql) : IAsyncLifetime
{
    public Task InitializeAsync() => sql.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Product_read_returns_metadata_and_missing_product_is_not_found()
    {
        await using var simulator = new SimulatorFactory(sql);
        await using var factory = new SaleFactory(sql, simulator);
        using var client = factory.CreateClient();

        using var found = await client.GetAsync($"/products/{SqlFixture.ProductId}");
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        var body = await found.Content.ReadFromJsonAsync<ProductBody>();
        Assert.Equal(SqlFixture.ProductId, body!.Id);
        Assert.Equal("Flash product", body.Name);
        Assert.Equal(19.99m, body.Price);

        using var missing = await client.GetAsync($"/products/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private sealed record ProductBody(Guid Id, string Name, decimal Price);
}
