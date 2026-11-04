using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rose.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUsersAndOwners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payments_title",
                schema: "rose",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ix_budget_lines_year",
                schema: "rose",
                table: "budget_lines");

            migrationBuilder.AddColumn<Guid>(
                name: "owner_id",
                schema: "rose",
                table: "savings_goals",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "owner_id",
                schema: "rose",
                table: "reminders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "owner_id",
                schema: "rose",
                table: "push_devices",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "owner_id",
                schema: "rose",
                table: "payments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "owner_id",
                schema: "rose",
                table: "habits",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "owner_id",
                schema: "rose",
                table: "budget_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "owner_id",
                schema: "rose",
                table: "accounts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "users",
                schema: "rose",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    must_change_password = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_savings_goals_owner_id",
                schema: "rose",
                table: "savings_goals",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_reminders_owner_id",
                schema: "rose",
                table: "reminders",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_push_devices_owner_id",
                schema: "rose",
                table: "push_devices",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_owner_id_title",
                schema: "rose",
                table: "payments",
                columns: new[] { "owner_id", "title" });

            migrationBuilder.CreateIndex(
                name: "ix_habits_owner_id",
                schema: "rose",
                table: "habits",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_budget_lines_owner_id_year",
                schema: "rose",
                table: "budget_lines",
                columns: new[] { "owner_id", "year" });

            migrationBuilder.CreateIndex(
                name: "ix_accounts_owner_id",
                schema: "rose",
                table: "accounts",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                schema: "rose",
                table: "users",
                column: "email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "users",
                schema: "rose");

            migrationBuilder.DropIndex(
                name: "ix_savings_goals_owner_id",
                schema: "rose",
                table: "savings_goals");

            migrationBuilder.DropIndex(
                name: "ix_reminders_owner_id",
                schema: "rose",
                table: "reminders");

            migrationBuilder.DropIndex(
                name: "ix_push_devices_owner_id",
                schema: "rose",
                table: "push_devices");

            migrationBuilder.DropIndex(
                name: "ix_payments_owner_id_title",
                schema: "rose",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ix_habits_owner_id",
                schema: "rose",
                table: "habits");

            migrationBuilder.DropIndex(
                name: "ix_budget_lines_owner_id_year",
                schema: "rose",
                table: "budget_lines");

            migrationBuilder.DropIndex(
                name: "ix_accounts_owner_id",
                schema: "rose",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "owner_id",
                schema: "rose",
                table: "savings_goals");

            migrationBuilder.DropColumn(
                name: "owner_id",
                schema: "rose",
                table: "reminders");

            migrationBuilder.DropColumn(
                name: "owner_id",
                schema: "rose",
                table: "push_devices");

            migrationBuilder.DropColumn(
                name: "owner_id",
                schema: "rose",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "owner_id",
                schema: "rose",
                table: "habits");

            migrationBuilder.DropColumn(
                name: "owner_id",
                schema: "rose",
                table: "budget_lines");

            migrationBuilder.DropColumn(
                name: "owner_id",
                schema: "rose",
                table: "accounts");

            migrationBuilder.CreateIndex(
                name: "ix_payments_title",
                schema: "rose",
                table: "payments",
                column: "title");

            migrationBuilder.CreateIndex(
                name: "ix_budget_lines_year",
                schema: "rose",
                table: "budget_lines",
                column: "year");
        }
    }
}
