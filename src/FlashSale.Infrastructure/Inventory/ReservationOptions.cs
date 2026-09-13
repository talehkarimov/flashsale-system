namespace FlashSale.Infrastructure.Inventory;

public sealed class ReservationOptions
{
    public const string SectionName = "Reservations";
    public TimeSpan Period { get; set; } = TimeSpan.FromMinutes(2);
}
