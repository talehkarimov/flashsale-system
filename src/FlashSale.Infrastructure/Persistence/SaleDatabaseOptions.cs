using Microsoft.Data.SqlClient;

namespace FlashSale.Infrastructure.Persistence;

public sealed class SaleDatabaseOptions
{
    public const string SectionName = "ConnectionStrings";
    public const string ConnectionName = "FlashSale";
    public const string EnvironmentVariable = "ConnectionStrings__FlashSale";
    public string FlashSale { get; set; } = string.Empty;

    public bool IsValid()
    {
        try
        {
            var connection = new SqlConnectionStringBuilder(FlashSale);
            return !string.IsNullOrWhiteSpace(connection.DataSource) && !string.IsNullOrWhiteSpace(connection.InitialCatalog);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
