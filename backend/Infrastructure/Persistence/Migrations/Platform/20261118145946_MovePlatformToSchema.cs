using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TemplateApp.Infrastructure.Persistence.Migrations.Platform
{
    /// <inheritdoc />
    public partial class MovePlatformToSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "platform");

            migrationBuilder.RenameTable(
                name: "IdempotencyKeys",
                newName: "IdempotencyKeys",
                newSchema: "platform");

            migrationBuilder.RenameTable(
                name: "AuditEntries",
                newName: "AuditEntries",
                newSchema: "platform");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "IdempotencyKeys",
                schema: "platform",
                newName: "IdempotencyKeys",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "AuditEntries",
                schema: "platform",
                newName: "AuditEntries",
                newSchema: "public");
        }
    }
}
