using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PaymentSimulator.Api.Persistence;

public sealed class PaymentDbContextFactory : IDesignTimeDbContextFactory<PaymentDbContext>
{
    public PaymentDbContext CreateDbContext(string[] args)
    {
        var database = new PaymentDatabaseOptions
        {
            Payments = Environment.GetEnvironmentVariable(PaymentDatabaseOptions.EnvironmentVariable) ?? string.Empty
        };
        if (!database.IsValid())
        {
            throw new InvalidOperationException($"Set {PaymentDatabaseOptions.EnvironmentVariable} to a SQL Server connection string with a database name.");
        }

        return new PaymentDbContext(new DbContextOptionsBuilder<PaymentDbContext>().UseSqlServer(database.Payments).Options);
    }
}
