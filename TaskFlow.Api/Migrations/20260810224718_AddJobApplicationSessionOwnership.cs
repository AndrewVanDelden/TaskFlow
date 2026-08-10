using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddJobApplicationSessionOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IngestionSessionId",
                table: "JobApplications",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "OwnerId",
                table: "JobApplications",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_JobApplications_IngestionSessionId_OwnerId",
                table: "JobApplications",
                columns: new[] { "IngestionSessionId", "OwnerId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JobApplications_IngestionSessionId_OwnerId",
                table: "JobApplications");

            migrationBuilder.DropColumn(
                name: "IngestionSessionId",
                table: "JobApplications");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "JobApplications");
        }
    }
}
