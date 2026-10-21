using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rose.Infrastructure.Lingo.Migrations
{
    /// <inheritdoc />
    public partial class InitialLingo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Cards",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    Language = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false),
                    Term = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Reading = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Meaning = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Repetitions = table.Column<int>(type: "INTEGER", nullable: false),
                    IntervalDays = table.Column<int>(type: "INTEGER", nullable: false),
                    Ease = table.Column<double>(type: "REAL", nullable: false),
                    DueOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Lapses = table.Column<int>(type: "INTEGER", nullable: false),
                    LastReviewedOn = table.Column<DateOnly>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QuizResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Day = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Language = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: false),
                    Total = table.Column<int>(type: "INTEGER", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuizResults", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Cards_Language_DueOn",
                table: "Cards",
                columns: new[] { "Language", "DueOn" });

            migrationBuilder.CreateIndex(
                name: "IX_Cards_Language_Term",
                table: "Cards",
                columns: new[] { "Language", "Term" });

            migrationBuilder.CreateIndex(
                name: "IX_QuizResults_Day_Language",
                table: "QuizResults",
                columns: new[] { "Day", "Language" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Cards");

            migrationBuilder.DropTable(
                name: "QuizResults");
        }
    }
}
