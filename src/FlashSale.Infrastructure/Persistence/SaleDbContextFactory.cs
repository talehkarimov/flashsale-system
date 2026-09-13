using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FlashSale.Infrastructure.Persistence;

public sealed class SaleDbContextFactory : IDesignTimeDbContextFactory<SaleDbContext>
{
    public SaleDbContext CreateDbContext(string[] args)
    {
        var database = new SaleDatabaseOptions
        {
            FlashSale = Environment.GetEnvironmentVariable(SaleDatabaseOptions.EnvironmentVariable) ?? string.Empty
        };
        if (!database.IsValid())
        {
            throw new InvalidOperationException($"Set {SaleDatabaseOptions.EnvironmentVariable} to a SQL Server connection string with a database name.");
        }

        return new SaleDbContext(new DbContextOptionsBuilder<SaleDbContext>().UseSqlServer(database.FlashSale).Options);
    }
}
