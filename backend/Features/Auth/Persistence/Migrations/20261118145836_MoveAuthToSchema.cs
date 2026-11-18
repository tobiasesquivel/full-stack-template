using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TemplateApp.Features.Auth.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MoveAuthToSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "auth");

            migrationBuilder.RenameTable(
                name: "Sessions",
                newName: "Sessions",
                newSchema: "auth");

            migrationBuilder.RenameTable(
                name: "AspNetUserTokens",
                newName: "AspNetUserTokens",
                newSchema: "auth");

            migrationBuilder.RenameTable(
                name: "AspNetUsers",
                newName: "AspNetUsers",
                newSchema: "auth");

            migrationBuilder.RenameTable(
                name: "AspNetUserRoles",
                newName: "AspNetUserRoles",
                newSchema: "auth");

            migrationBuilder.RenameTable(
                name: "AspNetUserLogins",
                newName: "AspNetUserLogins",
                newSchema: "auth");

            migrationBuilder.RenameTable(
                name: "AspNetUserClaims",
                newName: "AspNetUserClaims",
                newSchema: "auth");

            migrationBuilder.RenameTable(
                name: "AspNetRoles",
                newName: "AspNetRoles",
                newSchema: "auth");

            migrationBuilder.RenameTable(
                name: "AspNetRoleClaims",
                newName: "AspNetRoleClaims",
                newSchema: "auth");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "Sessions",
                schema: "auth",
                newName: "Sessions",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "AspNetUserTokens",
                schema: "auth",
                newName: "AspNetUserTokens",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "AspNetUsers",
                schema: "auth",
                newName: "AspNetUsers",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "AspNetUserRoles",
                schema: "auth",
                newName: "AspNetUserRoles",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "AspNetUserLogins",
                schema: "auth",
                newName: "AspNetUserLogins",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "AspNetUserClaims",
                schema: "auth",
                newName: "AspNetUserClaims",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "AspNetRoles",
                schema: "auth",
                newName: "AspNetRoles",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "AspNetRoleClaims",
                schema: "auth",
                newName: "AspNetRoleClaims",
                newSchema: "public");
        }
    }
}
