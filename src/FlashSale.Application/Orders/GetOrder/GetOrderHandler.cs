namespace FlashSale.Application.Orders.GetOrder;

public sealed class GetOrderHandler(IOrderReader orders)
{
    public async Task<GetOrderResult?> HandleAsync(GetOrderQuery query, CancellationToken cancellationToken)
    {
        var order = await orders.FindAsync(query.Id, cancellationToken);
        return order is null ? null : GetOrderResult.From(order);
    }
}
