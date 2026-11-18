using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TemplateApp.Features.Tasks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MoveTasksToSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tasks");

            migrationBuilder.RenameTable(
                name: "TaskItems",
                newName: "TaskItems",
                newSchema: "tasks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "TaskItems",
                schema: "tasks",
                newName: "TaskItems",
                newSchema: "public");
        }
    }
}
