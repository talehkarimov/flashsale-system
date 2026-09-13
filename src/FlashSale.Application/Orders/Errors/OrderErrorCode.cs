namespace FlashSale.Application.Orders.Errors;

public enum OrderErrorCode
{
    InvalidRequest,
    UnknownProduct,
    InsufficientInventory,
    IdempotencyConflict
}
