using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Rose.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "rose");

            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "rose",
                columns: table => new
                {
                    seq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    title = table.Column<string>(type: "text", nullable: false),
                    total_amount = table.Column<double>(type: "double precision", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    frequency = table.Column<string>(type: "text", nullable: false),
                    instalments = table.Column<int>(type: "integer", nullable: false),
                    payment_amount = table.Column<double>(type: "double precision", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.seq);
                });

            migrationBuilder.CreateTable(
                name: "app_secrets",
                schema: "rose",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_secrets", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "budget_lines",
                schema: "rose",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    year = table.Column<int>(type: "integer", nullable: false),
                    section = table.Column<string>(type: "text", nullable: false),
                    group = table.Column<string>(type: "text", nullable: false),
                    item = table.Column<string>(type: "text", nullable: false),
                    amounts = table.Column<double[]>(type: "double precision[]", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_budget_lines", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "habits",
                schema: "rose",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    icon = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    target = table.Column<int>(type: "integer", nullable: false),
                    unit = table.Column<string>(type: "text", nullable: false),
                    remind_at = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    nudged_on = table.Column<DateOnly>(type: "date", nullable: true),
                    snoozed_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_habits", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                schema: "rose",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    title = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<double>(type: "double precision", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: true),
                    time = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    note = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "push_devices",
                schema: "rose",
                columns: table => new
                {
                    endpoint = table.Column<string>(type: "text", nullable: false),
                    p256dh = table.Column<string>(type: "text", nullable: false),
                    auth = table.Column<string>(type: "text", nullable: false),
                    expiration_time = table.Column<long>(type: "bigint", nullable: true),
                    label = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_push_devices", x => x.endpoint);
                });

            migrationBuilder.CreateTable(
                name: "reminders",
                schema: "rose",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    repeat = table.Column<string>(type: "text", nullable: false),
                    week_days = table.Column<int[]>(type: "integer[]", nullable: false),
                    snoozed_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ringing = table.Column<bool>(type: "boolean", nullable: false),
                    fired_for = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_fired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    done = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reminders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "savings_goals",
                schema: "rose",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    icon = table.Column<string>(type: "text", nullable: false),
                    target = table.Column<decimal>(type: "numeric", nullable: false),
                    deadline = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_savings_goals", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "habit_days",
                schema: "rose",
                columns: table => new
                {
                    habit_id = table.Column<string>(type: "text", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    value = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_habit_days", x => new { x.habit_id, x.date });
                    table.ForeignKey(
                        name: "fk_habit_days_habits_habit_id",
                        column: x => x.habit_id,
                        principalSchema: "rose",
                        principalTable: "habits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "savings_entries",
                schema: "rose",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    goal_id = table.Column<string>(type: "text", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    note = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_savings_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_savings_entries_savings_goals_goal_id",
                        column: x => x.goal_id,
                        principalSchema: "rose",
                        principalTable: "savings_goals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_budget_lines_year",
                schema: "rose",
                table: "budget_lines",
                column: "year");

            migrationBuilder.CreateIndex(
                name: "ix_payments_title",
                schema: "rose",
                table: "payments",
                column: "title");

            migrationBuilder.CreateIndex(
                name: "ix_savings_entries_goal_id",
                schema: "rose",
                table: "savings_entries",
                column: "goal_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounts",
                schema: "rose");

            migrationBuilder.DropTable(
                name: "app_secrets",
                schema: "rose");

            migrationBuilder.DropTable(
                name: "budget_lines",
                schema: "rose");

            migrationBuilder.DropTable(
                name: "habit_days",
                schema: "rose");

            migrationBuilder.DropTable(
                name: "payments",
                schema: "rose");

            migrationBuilder.DropTable(
                name: "push_devices",
                schema: "rose");

            migrationBuilder.DropTable(
                name: "reminders",
                schema: "rose");

            migrationBuilder.DropTable(
                name: "savings_entries",
                schema: "rose");

            migrationBuilder.DropTable(
                name: "habits",
                schema: "rose");

            migrationBuilder.DropTable(
                name: "savings_goals",
                schema: "rose");
        }
    }
}
