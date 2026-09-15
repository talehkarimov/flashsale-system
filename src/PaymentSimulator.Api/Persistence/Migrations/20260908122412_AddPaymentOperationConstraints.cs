using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentSimulator.Api.Persistence.Migrations
{
    public partial class AddPaymentOperationConstraints : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentOperations_Requests",
                table: "PaymentOperations",
                sql: "[Requests] > 0");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentOperations_Requests",
                table: "PaymentOperations");
        }
    }
}
