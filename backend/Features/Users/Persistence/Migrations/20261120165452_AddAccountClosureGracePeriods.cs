using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TemplateApp.Features.Users.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountClosureGracePeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClosesAt",
                schema: "users",
                table: "UserProfiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AccountClosureGracePeriods",
                schema: "users",
                columns: table => new
                {
                    ClosureId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosesAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RemindersSent = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountClosureGracePeriods", x => x.ClosureId);
                });

            migrationBuilder.CreateTable(
                name: "AccountClosureHistory",
                schema: "users",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    ClosureId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Actor = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Detail = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountClosureHistory", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountClosureGracePeriods_UserId",
                schema: "users",
                table: "AccountClosureGracePeriods",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountClosureHistory_ClosureId_Kind_Detail",
                schema: "users",
                table: "AccountClosureHistory",
                columns: new[] { "ClosureId", "Kind", "Detail" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountClosureGracePeriods",
                schema: "users");

            migrationBuilder.DropTable(
                name: "AccountClosureHistory",
                schema: "users");

            migrationBuilder.DropColumn(
                name: "ClosesAt",
                schema: "users",
                table: "UserProfiles");
        }
    }
}
