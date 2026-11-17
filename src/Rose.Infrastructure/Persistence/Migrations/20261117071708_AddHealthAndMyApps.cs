using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rose.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHealthAndMyApps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_prefs",
                schema: "rose",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hidden = table.Column<string[]>(type: "text[]", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_prefs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "health_days",
                schema: "rose",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    weight_kg = table.Column<double>(type: "double precision", nullable: true),
                    walk_km = table.Column<double>(type: "double precision", nullable: false),
                    fasting_hours = table.Column<int>(type: "integer", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_health_days", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "health_profiles",
                schema: "rose",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    height_cm = table.Column<double>(type: "double precision", nullable: true),
                    birth_year = table.Column<int>(type: "integer", nullable: true),
                    sex = table.Column<string>(type: "text", nullable: false),
                    goal_kg = table.Column<double>(type: "double precision", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_health_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "health_meals",
                schema: "rose",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    day_id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    kcal = table.Column<int>(type: "integer", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_health_meals", x => x.id);
                    table.ForeignKey(
                        name: "fk_health_meals_health_days_day_id",
                        column: x => x.day_id,
                        principalSchema: "rose",
                        principalTable: "health_days",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_app_prefs_owner_id",
                schema: "rose",
                table: "app_prefs",
                column: "owner_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_health_days_owner_id_date",
                schema: "rose",
                table: "health_days",
                columns: new[] { "owner_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_health_meals_day_id",
                schema: "rose",
                table: "health_meals",
                column: "day_id");

            migrationBuilder.CreateIndex(
                name: "ix_health_profiles_owner_id",
                schema: "rose",
                table: "health_profiles",
                column: "owner_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_prefs",
                schema: "rose");

            migrationBuilder.DropTable(
                name: "health_meals",
                schema: "rose");

            migrationBuilder.DropTable(
                name: "health_profiles",
                schema: "rose");

            migrationBuilder.DropTable(
                name: "health_days",
                schema: "rose");
        }
    }
}
