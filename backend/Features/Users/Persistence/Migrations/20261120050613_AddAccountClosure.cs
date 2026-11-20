using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TemplateApp.Features.Users.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountClosure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClosureChangedAt",
                schema: "users",
                table: "UserProfiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClosureId",
                schema: "users",
                table: "UserProfiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosureReason",
                schema: "users",
                table: "UserProfiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosureStatus",
                schema: "users",
                table: "UserProfiles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.CreateTable(
                name: "InboxMessages",
                schema: "users",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Consumer = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxMessages", x => new { x.MessageId, x.Consumer });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InboxMessages",
                schema: "users");

            migrationBuilder.DropColumn(
                name: "ClosureChangedAt",
                schema: "users",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "ClosureId",
                schema: "users",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "ClosureReason",
                schema: "users",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "ClosureStatus",
                schema: "users",
                table: "UserProfiles");
        }
    }
}
