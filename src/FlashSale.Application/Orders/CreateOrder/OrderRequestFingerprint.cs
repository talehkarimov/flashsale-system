using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FlashSale.Application.Orders.CreateOrder;

public static class OrderRequestFingerprint
{
    public const int Length = SHA256.HashSizeInBytes * 2;

    public static string Create(IEnumerable<CreateOrderItem> items)
    {
        var canonicalItems = items.OrderBy(item => item.ProductId)
            .Select(item => string.Create(CultureInfo.InvariantCulture, $"{item.ProductId:N}:{item.Quantity}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(";", canonicalItems))));
    }
}
