using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rose.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentBudgetMark : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "budget_amount",
                schema: "rose",
                table: "payments",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "budget_month",
                schema: "rose",
                table: "payments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "budget_year",
                schema: "rose",
                table: "payments",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "budget_amount",
                schema: "rose",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "budget_month",
                schema: "rose",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "budget_year",
                schema: "rose",
                table: "payments");
        }
    }
}
