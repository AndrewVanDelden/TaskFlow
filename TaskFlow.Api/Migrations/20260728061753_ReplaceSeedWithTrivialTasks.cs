using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceSeedWithTrivialTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "AssignedToId", "CreatedAt", "Description", "DueDate", "Priority", "Title", "UpdatedAt" },
                values: new object[] { null, new DateTime(2026, 7, 27, 0, 0, 0, 0, DateTimeKind.Utc), "Compose a three-line haiku (5-7-5 syllables) about the fall season.", null, "Low", "Write a haiku about autumn", new DateTime(2026, 7, 27, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "AssignedToId", "CreatedAt", "Description", "DueDate", "Priority", "Status", "Title", "UpdatedAt" },
                values: new object[] { null, new DateTime(2026, 7, 27, 0, 0, 0, 0, DateTimeKind.Utc), "Give three short, creative uses for a paperclip.", null, "Low", "Todo", "List three uses for a paperclip", new DateTime(2026, 7, 27, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "AssignedToId", "CreatedAt", "Description", "DueDate", "Priority", "Status", "Title", "UpdatedAt" },
                values: new object[] { null, new DateTime(2026, 7, 27, 0, 0, 0, 0, DateTimeKind.Utc), "Suggest one name for a friendly helper robot, with a one-line reason.", null, "Low", "Todo", "Name a friendly robot", new DateTime(2026, 7, 27, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "CreatedAt", "Description", "DueDate", "Priority", "Title", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 7, 27, 0, 0, 0, 0, DateTimeKind.Utc), "Write a single clear sentence explaining what a to-do app does.", null, "Low", "Describe a to-do app in one sentence", new DateTime(2026, 7, 27, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "AssignedToId", "CreatedAt", "Description", "DueDate", "Priority", "Status", "Title", "UpdatedAt" },
                values: new object[] { null, new DateTime(2026, 7, 27, 0, 0, 0, 0, DateTimeKind.Utc), "Provide one interesting fact about the number seven.", null, "Low", "Todo", "Share a fun fact about the number 7", new DateTime(2026, 7, 27, 0, 0, 0, 0, DateTimeKind.Utc) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "AssignedToId", "CreatedAt", "Description", "DueDate", "Priority", "Title", "UpdatedAt" },
                values: new object[] { 1, new DateTime(2026, 5, 14, 0, 0, 0, 0, DateTimeKind.Utc), "Configure GitHub Actions to run tests and deploy to Azure on push to main.", new DateTime(2026, 5, 20, 0, 0, 0, 0, DateTimeKind.Utc), "High", "Set up CI/CD pipeline", new DateTime(2026, 5, 14, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "AssignedToId", "CreatedAt", "Description", "DueDate", "Priority", "Status", "Title", "UpdatedAt" },
                values: new object[] { 1, new DateTime(2026, 5, 13, 0, 0, 0, 0, DateTimeKind.Utc), "Finalize the entity relationships and run migrations.", new DateTime(2026, 5, 14, 0, 0, 0, 0, DateTimeKind.Utc), "High", "Done", "Design database schema", new DateTime(2026, 5, 14, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "AssignedToId", "CreatedAt", "Description", "DueDate", "Priority", "Status", "Title", "UpdatedAt" },
                values: new object[] { 2, new DateTime(2026, 5, 14, 0, 0, 0, 0, DateTimeKind.Utc), "Cover CRUD endpoints and auth flows with xUnit.", new DateTime(2026, 5, 18, 0, 0, 0, 0, DateTimeKind.Utc), "Medium", "InProgress", "Write API integration tests", new DateTime(2026, 5, 14, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "CreatedAt", "Description", "DueDate", "Priority", "Title", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 5, 14, 0, 0, 0, 0, DateTimeKind.Utc), "React drag-and-drop board with columns for each workflow state.", new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Utc), "Medium", "Build Kanban board UI", new DateTime(2026, 5, 14, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "AssignedToId", "CreatedAt", "Description", "DueDate", "Priority", "Status", "Title", "UpdatedAt" },
                values: new object[] { 1, new DateTime(2026, 5, 13, 0, 0, 0, 0, DateTimeKind.Utc), "Register and login endpoints, protect all task routes.", new DateTime(2026, 5, 17, 0, 0, 0, 0, DateTimeKind.Utc), "High", "Review", "Add JWT authentication", new DateTime(2026, 5, 14, 0, 0, 0, 0, DateTimeKind.Utc) });
        }
    }
}
