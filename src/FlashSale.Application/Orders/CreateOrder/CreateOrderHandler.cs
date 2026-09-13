using FlashSale.Application.Orders.Errors;
using FlashSale.Application.Orders.GetOrder;
using FlashSale.Domain.Orders;

namespace FlashSale.Application.Orders.CreateOrder;

public sealed class CreateOrderHandler(IOrderCreationStore store, TimeProvider clock, TimeSpan reservationPeriod)
{
    public async Task<CreateOrderResult> HandleAsync(
        CreateOrderCommand command, IdempotencyKey key, CancellationToken cancellationToken)
    {
        CreateOrderValidator.Validate(command);
        var fingerprint = OrderRequestFingerprint.Create(command.Items);
        var existing = await store.FindByKeyAsync(command.UserId, key, cancellationToken);
        if (existing is not null)
        {
            return Replay(existing, fingerprint);
        }

        var ids = command.Items.Select(item => item.ProductId).ToArray();
        var products = await store.ReadProductsAsync(ids, cancellationToken);
        if (products.Count != ids.Length)
        {
            throw new OrderRequestException(OrderErrorCode.UnknownProduct, "A requested product does not exist.");
        }

        var items = command.Items.Select(item => new OrderItem(item.ProductId, item.Quantity, products[item.ProductId].Price));
        var order = new Order(command.UserId, key, fingerprint, items, clock.GetUtcNow(), reservationPeriod);
        var committed = await store.CommitAsync(order, cancellationToken);
        return committed.Id == order.Id
            ? new CreateOrderResult(GetOrderResult.From(order), Replayed: false)
            : Replay(committed, fingerprint);
    }

    private static CreateOrderResult Replay(Order order, string fingerprint)
    {
        if (order.RequestHash != fingerprint)
        {
            throw new OrderRequestException(OrderErrorCode.IdempotencyConflict, "This idempotency key was used with a different request.");
        }

        return new CreateOrderResult(GetOrderResult.From(order), Replayed: true);
    }
}
