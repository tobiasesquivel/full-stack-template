using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TemplateApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameRefreshTokenUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RefreshTokens_AspNetUsers_AppIdentityUserId",
                table: "RefreshTokens");

            migrationBuilder.RenameColumn(
                name: "AppIdentityUserId",
                table: "RefreshTokens",
                newName: "UserId");

            migrationBuilder.RenameIndex(
                name: "IX_RefreshTokens_AppIdentityUserId_RevokedAt",
                table: "RefreshTokens",
                newName: "IX_RefreshTokens_UserId_RevokedAt");

            migrationBuilder.AddForeignKey(
                name: "FK_RefreshTokens_AspNetUsers_UserId",
                table: "RefreshTokens",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RefreshTokens_AspNetUsers_UserId",
                table: "RefreshTokens");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "RefreshTokens",
                newName: "AppIdentityUserId");

            migrationBuilder.RenameIndex(
                name: "IX_RefreshTokens_UserId_RevokedAt",
                table: "RefreshTokens",
                newName: "IX_RefreshTokens_AppIdentityUserId_RevokedAt");

            migrationBuilder.AddForeignKey(
                name: "FK_RefreshTokens_AspNetUsers_AppIdentityUserId",
                table: "RefreshTokens",
                column: "AppIdentityUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
