using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TemplateApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskListIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TaskItems_OwnerId",
                table: "TaskItems");

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_OwnerId_CreatedAt_Id",
                table: "TaskItems",
                columns: new[] { "OwnerId", "CreatedAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TaskItems_OwnerId_CreatedAt_Id",
                table: "TaskItems");

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_OwnerId",
                table: "TaskItems",
                column: "OwnerId");
        }
    }
}
