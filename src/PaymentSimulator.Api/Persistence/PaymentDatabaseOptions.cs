using Microsoft.Data.SqlClient;

namespace PaymentSimulator.Api.Persistence;

public sealed class PaymentDatabaseOptions
{
    public const string SectionName = "ConnectionStrings";
    public const string EnvironmentVariable = "ConnectionStrings__Payments";
    public string Payments { get; set; } = string.Empty;

    public bool IsValid()
    {
        try
        {
            var connection = new SqlConnectionStringBuilder(Payments);
            return !string.IsNullOrWhiteSpace(connection.DataSource) && !string.IsNullOrWhiteSpace(connection.InitialCatalog);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
