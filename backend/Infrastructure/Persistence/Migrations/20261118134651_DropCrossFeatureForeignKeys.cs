using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TemplateApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropCrossFeatureForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_IdempotencyKeys_UserProfiles_OwnerId",
                table: "IdempotencyKeys");

            migrationBuilder.DropForeignKey(
                name: "FK_TaskItems_UserProfiles_OwnerId",
                table: "TaskItems");

            migrationBuilder.DropForeignKey(
                name: "FK_UserProfiles_AspNetUsers_AuthId",
                table: "UserProfiles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_IdempotencyKeys_UserProfiles_OwnerId",
                table: "IdempotencyKeys",
                column: "OwnerId",
                principalTable: "UserProfiles",
                principalColumn: "AuthId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TaskItems_UserProfiles_OwnerId",
                table: "TaskItems",
                column: "OwnerId",
                principalTable: "UserProfiles",
                principalColumn: "AuthId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserProfiles_AspNetUsers_AuthId",
                table: "UserProfiles",
                column: "AuthId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
