using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TemplateApp.Features.Tasks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskProjectId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                schema: "tasks",
                table: "TaskItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_ProjectId",
                schema: "tasks",
                table: "TaskItems",
                column: "ProjectId")
                .Annotation("Npgsql:CreatedConcurrently", true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TaskItems_ProjectId",
                schema: "tasks",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                schema: "tasks",
                table: "TaskItems");
        }
    }
}
