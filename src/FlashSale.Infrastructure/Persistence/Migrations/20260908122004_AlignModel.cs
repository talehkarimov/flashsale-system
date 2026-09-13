using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlashSale.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlignModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_OutboxMessages_Attempts",
                table: "OutboxMessages",
                sql: "[Attempts] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutboxMessages_Lease",
                table: "OutboxMessages",
                sql: "([LeaseToken] IS NULL AND [LeaseUntil] IS NULL) OR ([LeaseToken] IS NOT NULL AND [LeaseUntil] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OutboxMessages_Attempts",
                table: "OutboxMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OutboxMessages_Lease",
                table: "OutboxMessages");
        }
    }
}
