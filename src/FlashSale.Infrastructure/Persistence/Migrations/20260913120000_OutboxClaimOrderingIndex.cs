using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlashSale.Infrastructure.Persistence.Migrations;

[Migration("20260913120000_OutboxClaimOrderingIndex")]
public partial class OutboxClaimOrderingIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_OutboxMessages_NextAttemptAt_LeaseUntil",
            table: "OutboxMessages");

        migrationBuilder.CreateIndex(
            name: "IX_OutboxMessages_NextAttemptAt_Id",
            table: "OutboxMessages",
            columns: new[] { "NextAttemptAt", "Id" },
            filter: "[ProcessedAt] IS NULL")
            .Annotation("SqlServer:Include", new[] { "LeaseUntil", "LeaseToken", "OrderId", "Attempts" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_OutboxMessages_NextAttemptAt_Id", "OutboxMessages");
        migrationBuilder.CreateIndex(
            name: "IX_OutboxMessages_NextAttemptAt_LeaseUntil",
            table: "OutboxMessages",
            columns: new[] { "NextAttemptAt", "LeaseUntil" },
            filter: "[ProcessedAt] IS NULL");
    }
}
