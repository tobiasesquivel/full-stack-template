using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TemplateApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdempotencyKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IdempotencyKeys",
                columns: table => new
                {
                    OwnerId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Key = table.Column<Guid>(type: "uuid", nullable: false),
                    Fingerprint = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StatusCode = table.Column<int>(type: "integer", nullable: true),
                    Location = table.Column<string>(type: "text", nullable: true),
                    ContentType = table.Column<string>(type: "text", nullable: true),
                    Body = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyKeys", x => new { x.OwnerId, x.Key });
                    table.ForeignKey(
                        name: "FK_IdempotencyKeys_UserProfiles_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "UserProfiles",
                        principalColumn: "AuthId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyKeys_CreatedAt",
                table: "IdempotencyKeys",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdempotencyKeys");
        }
    }
}
