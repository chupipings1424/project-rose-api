using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rose.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLoans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "loans",
                schema: "rose",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    loan_date = table.Column<DateOnly>(type: "date", nullable: false),
                    term = table.Column<int>(type: "integer", nullable: false),
                    monthly_amortization = table.Column<decimal>(type: "numeric", nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loans", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_loans_owner_id",
                schema: "rose",
                table: "loans",
                column: "owner_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "loans",
                schema: "rose");
        }
    }
}
