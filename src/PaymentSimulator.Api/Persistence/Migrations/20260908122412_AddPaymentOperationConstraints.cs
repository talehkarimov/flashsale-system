using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentSimulator.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentOperationConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentOperations_Requests",
                table: "PaymentOperations",
                sql: "[Requests] > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentOperations_Requests",
                table: "PaymentOperations");
        }
    }
}
