using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddResumeAndCoverLetterDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ApplicationId",
                table: "Tasks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TailoredContent",
                table: "Tasks",
                type: "TEXT",
                maxLength: 20000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "JobApplications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobApplications", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "ApplicationId", "TailoredContent" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "ApplicationId", "TailoredContent" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "ApplicationId", "TailoredContent" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "ApplicationId", "TailoredContent" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "ApplicationId", "TailoredContent" },
                values: new object[] { null, null });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_ApplicationId",
                table: "Tasks",
                column: "ApplicationId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_JobApplications_ApplicationId",
                table: "Tasks",
                column: "ApplicationId",
                principalTable: "JobApplications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tasks_JobApplications_ApplicationId",
                table: "Tasks");

            migrationBuilder.DropTable(
                name: "JobApplications");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_ApplicationId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "ApplicationId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "TailoredContent",
                table: "Tasks");
        }
    }
}
