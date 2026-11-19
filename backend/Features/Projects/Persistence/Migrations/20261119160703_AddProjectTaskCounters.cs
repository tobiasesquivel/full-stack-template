using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TemplateApp.Features.Projects.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectTaskCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InboxMessages",
                schema: "projects",
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

            migrationBuilder.CreateTable(
                name: "ProjectTaskCounters",
                schema: "projects",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskCount = table.Column<int>(type: "integer", nullable: false),
                    CompletedTaskCount = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectTaskCounters", x => x.ProjectId);
                    table.ForeignKey(
                        name: "FK_ProjectTaskCounters_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "projects",
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InboxMessages",
                schema: "projects");

            migrationBuilder.DropTable(
                name: "ProjectTaskCounters",
                schema: "projects");
        }
    }
}
