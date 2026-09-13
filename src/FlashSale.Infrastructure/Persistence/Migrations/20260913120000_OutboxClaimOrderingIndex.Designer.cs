using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlashSale.Infrastructure.Persistence.Migrations;

[DbContext(typeof(SaleDbContext))]
partial class OutboxClaimOrderingIndex
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
    }
}
