using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rose.Infrastructure.Lingo.Migrations
{
    /// <inheritdoc />
    public partial class OnDemandQuiz : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The old once-a-day quiz scores do not fit the new practice history, so they go (agreed with the user).
            migrationBuilder.Sql("DELETE FROM \"QuizResults\";");

            migrationBuilder.DropIndex(
                name: "IX_QuizResults_Day_Language",
                table: "QuizResults");

            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "QuizResults",
                type: "TEXT",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_QuizResults_Language",
                table: "QuizResults",
                column: "Language");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QuizResults_Language",
                table: "QuizResults");

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "QuizResults");

            migrationBuilder.CreateIndex(
                name: "IX_QuizResults_Day_Language",
                table: "QuizResults",
                columns: new[] { "Day", "Language" },
                unique: true);
        }
    }
}
