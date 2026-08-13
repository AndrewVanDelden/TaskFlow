using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class MakeResumeContextSessionOwnerUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ResumeContexts_IngestionSessionId_OwnerId",
                table: "ResumeContexts");

            migrationBuilder.CreateIndex(
                name: "IX_ResumeContexts_IngestionSessionId_OwnerId",
                table: "ResumeContexts",
                columns: new[] { "IngestionSessionId", "OwnerId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ResumeContexts_IngestionSessionId_OwnerId",
                table: "ResumeContexts");

            migrationBuilder.CreateIndex(
                name: "IX_ResumeContexts_IngestionSessionId_OwnerId",
                table: "ResumeContexts",
                columns: new[] { "IngestionSessionId", "OwnerId" });
        }
    }
}
