using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskKindAndProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Tasks",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceName",
                table: "Tasks",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceSection",
                table: "Tasks",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Kind", "SourceName", "SourceSection" },
                values: new object[] { "Generic", null, null });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Kind", "SourceName", "SourceSection" },
                values: new object[] { "Generic", null, null });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "Kind", "SourceName", "SourceSection" },
                values: new object[] { "Generic", null, null });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "Kind", "SourceName", "SourceSection" },
                values: new object[] { "Generic", null, null });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "Kind", "SourceName", "SourceSection" },
                values: new object[] { "Generic", null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "SourceName",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "SourceSection",
                table: "Tasks");
        }
    }
}
