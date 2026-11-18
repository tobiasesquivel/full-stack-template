using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TemplateApp.Features.Users.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MoveUsersToSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "users");

            migrationBuilder.RenameTable(
                name: "UserProfiles",
                newName: "UserProfiles",
                newSchema: "users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "UserProfiles",
                schema: "users",
                newName: "UserProfiles",
                newSchema: "public");
        }
    }
}
