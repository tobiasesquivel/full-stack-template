using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TemplateApp.Features.Tasks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerHolds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OwnerHolds",
                schema: "tasks",
                columns: table => new
                {
                    OwnerId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ClosureId = table.Column<Guid>(type: "uuid", nullable: false),
                    HeldAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnerHolds", x => x.OwnerId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OwnerHolds",
                schema: "tasks");
        }
    }
}
