using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaskFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Priority = table.Column<string>(type: "TEXT", nullable: false),
                    DueDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AssignedToId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tasks_Users_AssignedToId",
                        column: x => x.AssignedToId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.InsertData(
                table: "Tasks",
                columns: new[] { "Id", "AssignedToId", "CreatedAt", "Description", "DueDate", "Priority", "Status", "Title", "UpdatedAt" },
                values: new object[] { 4, null, new DateTime(2026, 5, 14, 0, 0, 0, 0, DateTimeKind.Utc), "React drag-and-drop board with columns for each workflow state.", new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Utc), "Medium", "Todo", "Build Kanban board UI", new DateTime(2026, 5, 14, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "CreatedAt", "Email", "Name", "PasswordHash" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 5, 14, 0, 0, 0, 0, DateTimeKind.Utc), "andrew@taskflow.dev", "Andrew Van Delden", "placeholder" },
                    { 2, new DateTime(2026, 5, 14, 0, 0, 0, 0, DateTimeKind.Utc), "demo@taskflow.dev", "Demo User", "placeholder" }
                });

            migrationBuilder.InsertData(
                table: "Tasks",
                columns: new[] { "Id", "AssignedToId", "CreatedAt", "Description", "DueDate", "Priority", "Status", "Title", "UpdatedAt" },
                values: new object[,]
                {
                    { 1, 1, new DateTime(2026, 5, 14, 0, 0, 0, 0, DateTimeKind.Utc), "Configure GitHub Actions to run tests and deploy to Azure on push to main.", new DateTime(2026, 5, 20, 0, 0, 0, 0, DateTimeKind.Utc), "High", "Todo", "Set up CI/CD pipeline", new DateTime(2026, 5, 14, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 2, 1, new DateTime(2026, 5, 13, 0, 0, 0, 0, DateTimeKind.Utc), "Finalize the entity relationships and run migrations.", new DateTime(2026, 5, 14, 0, 0, 0, 0, DateTimeKind.Utc), "High", "Done", "Design database schema", new DateTime(2026, 5, 14, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 3, 2, new DateTime(2026, 5, 14, 0, 0, 0, 0, DateTimeKind.Utc), "Cover CRUD endpoints and auth flows with xUnit.", new DateTime(2026, 5, 18, 0, 0, 0, 0, DateTimeKind.Utc), "Medium", "InProgress", "Write API integration tests", new DateTime(2026, 5, 14, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 5, 1, new DateTime(2026, 5, 13, 0, 0, 0, 0, DateTimeKind.Utc), "Register and login endpoints, protect all task routes.", new DateTime(2026, 5, 17, 0, 0, 0, 0, DateTimeKind.Utc), "High", "Review", "Add JWT authentication", new DateTime(2026, 5, 14, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_AssignedToId",
                table: "Tasks",
                column: "AssignedToId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Tasks");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
