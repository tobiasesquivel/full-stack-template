using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Dune.Analyzers.Tests;

public sealed class MigrationSafetyAnalyzerTests
{
    private const string Folder = "backend/Infrastructure/Persistence/Migrations/";
    private const string TasksFolder = "backend/Features/Tasks/Persistence/Migrations/";

    [Fact]
    public async Task ExpandMigrationsContractMigrationsAndMigrationsBeforeTheRuleAreAccepted()
    {
        var diagnostics = await AnalyzeAsync(
            (Folder + "20261118000001_ExpandTaskCompletedAt.cs", """
                using System;
                using Microsoft.EntityFrameworkCore.Migrations;
                namespace TemplateApp.Infrastructure.Persistence.Migrations;
                [Migration("20261118000001_ExpandTaskCompletedAt")]
                public partial class ExpandTaskCompletedAt : Migration
                {
                    protected override void Up(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.AddColumn<DateTimeOffset>(
                            name: "CompletedAt",
                            table: "TaskItems",
                            type: "timestamp with time zone",
                            nullable: true);

                        migrationBuilder.CreateTable(
                            name: "Labels",
                            columns: table => new
                            {
                                Id = table.Column<Guid>(type: "uuid", nullable: false),
                            },
                            constraints: table => table.PrimaryKey("PK_Labels", x => x.Id));

                        migrationBuilder.CreateIndex(
                            name: "IX_Labels_Id",
                            table: "Labels",
                            column: "Id");

                        migrationBuilder.CreateIndex(
                            name: "IX_TaskItems_CompletedAt",
                            table: "TaskItems",
                            column: "CompletedAt")
                            .Annotation("Npgsql:CreatedConcurrently", true);

                        migrationBuilder.AlterColumn<bool>(
                            name: "IsCompleted",
                            table: "TaskItems",
                            type: "boolean",
                            nullable: false,
                            defaultValue: false,
                            oldClrType: typeof(bool),
                            oldType: "boolean");

                        migrationBuilder.AlterColumn<string>(
                            name: "Title",
                            table: "TaskItems",
                            nullable: true,
                            oldClrType: typeof(string),
                            oldNullable: false);

                        migrationBuilder.Sql("UPDATE \"TaskItems\" SET \"CompletedAt\" = \"CreatedAt\" WHERE \"IsCompleted\";");
                    }

                    protected override void Down(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.DropIndex(name: "IX_TaskItems_CompletedAt", table: "TaskItems");
                        migrationBuilder.DropTable(name: "Labels");
                        migrationBuilder.DropColumn(name: "CompletedAt", table: "TaskItems");
                    }
                }
                """),
            (Folder + "20261118000002_ContractTaskIsCompleted.cs", """
                using Microsoft.EntityFrameworkCore.Migrations;
                namespace TemplateApp.Infrastructure.Persistence.Migrations;
                [Migration("20261118000002_ContractTaskIsCompleted")]
                public partial class ContractTaskIsCompleted : Migration
                {
                    protected override void Up(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.DropForeignKey(name: "FK_TaskItems_UserProfiles_OwnerId", table: "TaskItems");
                        migrationBuilder.DropIndex(name: "IX_TaskItems_OwnerId", table: "TaskItems");
                        migrationBuilder.DropColumn(name: "IsCompleted", table: "TaskItems");
                        migrationBuilder.DropTable(name: "RefreshTokens");
                    }

                    protected override void Down(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.AddColumn<bool>(name: "IsCompleted", table: "TaskItems", nullable: false, defaultValue: false);
                    }
                }
                """),
            (Folder + "20261117132830_AddTaskListIndex.cs", """
                using Microsoft.EntityFrameworkCore.Migrations;
                namespace TemplateApp.Infrastructure.Persistence.Migrations;
                [Migration("20261117132830_AddTaskListIndex")]
                public partial class AddTaskListIndex : Migration
                {
                    protected override void Up(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.DropIndex(name: "IX_TaskItems_OwnerId", table: "TaskItems");
                        migrationBuilder.CreateIndex(name: "IX_TaskItems_OwnerId_CreatedAt_Id", table: "TaskItems", columns: new[] { "OwnerId", "CreatedAt", "Id" });
                    }

                    protected override void Down(MigrationBuilder migrationBuilder)
                    {
                    }
                }
                """));

        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Id == "DUNE0018"));
    }

    [Fact]
    public async Task EachUnsafeOperationInANewMigrationIsReportedWhereItHappens()
    {
        var diagnostics = await AnalyzeAsync(
            (Folder + "20261118000003_ReplaceTaskListIndex.cs", """
                using Microsoft.EntityFrameworkCore.Migrations;
                namespace TemplateApp.Infrastructure.Persistence.Migrations;
                [Migration("20261118000003_ReplaceTaskListIndex")]
                public partial class ReplaceTaskListIndex : Migration
                {
                    protected override void Up(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.DropIndex(name: "IX_TaskItems_OwnerId", table: "TaskItems");
                        migrationBuilder.CreateIndex(name: "IX_TaskItems_OwnerId_CreatedAt_Id", table: "TaskItems", columns: new[] { "OwnerId", "CreatedAt", "Id" });
                    }

                    protected override void Down(MigrationBuilder migrationBuilder)
                    {
                    }
                }
                """),
            (Folder + "20261118000004_RenameTitle.cs", """
                using System;
                using Microsoft.EntityFrameworkCore.Migrations;
                namespace TemplateApp.Infrastructure.Persistence.Migrations;
                [Migration("20261118000004_RenameTitle")]
                public partial class RenameTitle : Migration
                {
                    protected override void Up(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.RenameColumn(name: "Title", table: "TaskItems", newName: "Name");
                        migrationBuilder.RenameTable(name: "TaskItems", newName: "Tasks");
                        migrationBuilder.DropColumn(name: "IsCompleted", table: "Tasks");
                        migrationBuilder.AlterColumn<DateTimeOffset>(
                            name: "UpdatedAt",
                            table: "Tasks",
                            nullable: false,
                            defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                            oldClrType: typeof(DateTimeOffset),
                            oldNullable: true);
                        migrationBuilder.AlterColumn<string>(name: "OwnerId", table: "Tasks", type: "uuid", oldClrType: typeof(string), oldType: "character varying(450)");
                    }

                    protected override void Down(MigrationBuilder migrationBuilder)
                    {
                    }
                }
                """),
            (Folder + "20261118000005_ContractAndExpand.cs", """
                using System;
                using Microsoft.EntityFrameworkCore.Migrations;
                namespace TemplateApp.Infrastructure.Persistence.Migrations;
                [Migration("20261118000005_ContractAndExpand")]
                public partial class ContractAndExpand : Migration
                {
                    protected override void Up(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.DropColumn(name: "IsCompleted", table: "TaskItems");
                        migrationBuilder.AddColumn<DateTimeOffset>(name: "CompletedAt", table: "TaskItems", nullable: true);
                        migrationBuilder.RenameColumn(name: "Title", table: "TaskItems", newName: "Name");
                    }

                    protected override void Down(MigrationBuilder migrationBuilder)
                    {
                    }
                }
                """));

        Assert.Equal(
            new[]
            {
                ("20261118000003_ReplaceTaskListIndex.cs", 8, "DropIndex"),
                ("20261118000003_ReplaceTaskListIndex.cs", 9, "CreateIndex"),
                ("20261118000004_RenameTitle.cs", 9, "RenameColumn"),
                ("20261118000004_RenameTitle.cs", 10, "RenameTable"),
                ("20261118000004_RenameTitle.cs", 11, "DropColumn"),
                ("20261118000004_RenameTitle.cs", 12, "AlterColumn"),
                ("20261118000004_RenameTitle.cs", 19, "AlterColumn"),
                ("20261118000005_ContractAndExpand.cs", 10, "AddColumn"),
                ("20261118000005_ContractAndExpand.cs", 11, "RenameColumn"),
            },
            Reported(diagnostics));
    }

    [Fact]
    public async Task AMoveMigrationThatOnlyChangesTheSchemaAndMovesItBackInDownIsAccepted()
    {
        var diagnostics = await AnalyzeAsync(
            (TasksFolder + "20261118200000_MoveTasksToSchema.cs", """
                using Microsoft.EntityFrameworkCore.Migrations;
                namespace TemplateApp.Features.Tasks.Persistence.Migrations;
                [Migration("20261118200000_MoveTasksToSchema")]
                public partial class MoveTasksToSchema : Migration
                {
                    protected override void Up(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.EnsureSchema(name: "tasks");
                        migrationBuilder.RenameTable(name: "TaskItems", newName: "TaskItems", newSchema: "tasks");
                        migrationBuilder.RenameTable(name: "Labels", newSchema: "tasks");
                    }

                    protected override void Down(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.RenameTable(name: "TaskItems", schema: "tasks", newName: "TaskItems", newSchema: "public");
                        migrationBuilder.RenameTable(name: "Labels", schema: "tasks", newSchema: "public");
                    }
                }
                """));

        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Id == "DUNE0018"));
    }

    [Fact]
    public async Task ASchemaMoveOutsideAMoveMigrationOrAMoveMigrationThatDoesMoreOrHasNoWayBackIsReported()
    {
        var diagnostics = await AnalyzeAsync(
            (TasksFolder + "20261118200001_MoveTasksAndRename.cs", """
                using Microsoft.EntityFrameworkCore.Migrations;
                namespace TemplateApp.Features.Tasks.Persistence.Migrations;
                [Migration("20261118200001_MoveTasksAndRename")]
                public partial class MoveTasksAndRename : Migration
                {
                    protected override void Up(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.EnsureSchema(name: "tasks");
                        migrationBuilder.RenameTable(name: "TaskItems", newName: "Tasks", newSchema: "tasks");
                    }

                    protected override void Down(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.RenameTable(name: "Tasks", schema: "tasks", newName: "Tasks", newSchema: "public");
                    }
                }
                """),
            (TasksFolder + "20261118200002_MoveTasksAndAddLabel.cs", """
                using Microsoft.EntityFrameworkCore.Migrations;
                namespace TemplateApp.Features.Tasks.Persistence.Migrations;
                [Migration("20261118200002_MoveTasksAndAddLabel")]
                public partial class MoveTasksAndAddLabel : Migration
                {
                    protected override void Up(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.EnsureSchema(name: "tasks");
                        migrationBuilder.RenameTable(name: "TaskItems", newName: "TaskItems", newSchema: "tasks");
                        migrationBuilder.AddColumn<string>(name: "Label", table: "TaskItems", schema: "tasks", nullable: true);
                    }

                    protected override void Down(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.RenameTable(name: "TaskItems", schema: "tasks", newName: "TaskItems", newSchema: "public");
                    }
                }
                """),
            (TasksFolder + "20261118200003_ContractTasksSchema.cs", """
                using Microsoft.EntityFrameworkCore.Migrations;
                namespace TemplateApp.Features.Tasks.Persistence.Migrations;
                [Migration("20261118200003_ContractTasksSchema")]
                public partial class ContractTasksSchema : Migration
                {
                    protected override void Up(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.RenameTable(name: "TaskItems", newName: "TaskItems", newSchema: "tasks");
                    }

                    protected override void Down(MigrationBuilder migrationBuilder)
                    {
                    }
                }
                """),
            (TasksFolder + "20261118200004_TasksSchema.cs", """
                using Microsoft.EntityFrameworkCore.Migrations;
                namespace TemplateApp.Features.Tasks.Persistence.Migrations;
                [Migration("20261118200004_TasksSchema")]
                public partial class TasksSchema : Migration
                {
                    protected override void Up(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.EnsureSchema(name: "tasks");
                        migrationBuilder.RenameTable(name: "TaskItems", newName: "TaskItems", newSchema: "tasks");
                    }

                    protected override void Down(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.RenameTable(name: "TaskItems", schema: "tasks", newName: "TaskItems", newSchema: "public");
                    }
                }
                """),
            (TasksFolder + "20261118200005_MoveTasksWithGeneratedDown.cs", """
                using Microsoft.EntityFrameworkCore.Migrations;
                namespace TemplateApp.Features.Tasks.Persistence.Migrations;
                [Migration("20261118200005_MoveTasksWithGeneratedDown")]
                public partial class MoveTasksWithGeneratedDown : Migration
                {
                    protected override void Up(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.EnsureSchema(name: "tasks");
                        migrationBuilder.RenameTable(name: "TaskItems", newName: "TaskItems", newSchema: "tasks");
                    }

                    protected override void Down(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.RenameTable(name: "TaskItems", schema: "tasks", newName: "TaskItems");
                    }
                }
                """),
            (TasksFolder + "20261118200006_MoveTasksWithoutDown.cs", """
                using Microsoft.EntityFrameworkCore.Migrations;
                namespace TemplateApp.Features.Tasks.Persistence.Migrations;
                [Migration("20261118200006_MoveTasksWithoutDown")]
                public partial class MoveTasksWithoutDown : Migration
                {
                    protected override void Up(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.EnsureSchema(name: "tasks");
                        migrationBuilder.RenameTable(name: "TaskItems", newName: "TaskItems", newSchema: "tasks");
                    }

                    protected override void Down(MigrationBuilder migrationBuilder)
                    {
                    }
                }
                """));

        Assert.Equal(
            new[]
            {
                ("20261118200001_MoveTasksAndRename.cs", 9, "RenameTable", "Migración que renombra"),
                ("20261118200001_MoveTasksAndRename.cs", 14, "RenameTable", "Migración Move sin vuelta en el Down"),
                ("20261118200002_MoveTasksAndAddLabel.cs", 10, "AddColumn", "Migración Move con otras operaciones"),
                ("20261118200003_ContractTasksSchema.cs", 8, "RenameTable", "Cambio de schema fuera de una migración Move"),
                ("20261118200004_TasksSchema.cs", 9, "RenameTable", "Cambio de schema fuera de una migración Move"),
                ("20261118200005_MoveTasksWithGeneratedDown.cs", 14, "RenameTable", "Migración Move sin vuelta en el Down"),
                ("20261118200006_MoveTasksWithoutDown.cs", 9, "RenameTable", "Migración Move sin vuelta en el Down"),
            },
            ReportedWithTitle(diagnostics));
    }

    [Fact]
    public async Task AMigrationWhoseNameOnlyStartsWithTheLettersMoveAndAMoveFromAnExplicitSchemaThatReturnsThereAreAccepted()
    {
        var diagnostics = await AnalyzeAsync(
            (TasksFolder + "20261118200010_MovedAtTaskItems.cs", """
                using Microsoft.EntityFrameworkCore.Migrations;
                namespace TemplateApp.Features.Tasks.Persistence.Migrations;
                [Migration("20261118200010_MovedAtTaskItems")]
                public partial class MovedAtTaskItems : Migration
                {
                    protected override void Up(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.AddColumn<string>(name: "MovedAt", table: "TaskItems", nullable: true);
                    }

                    protected override void Down(MigrationBuilder migrationBuilder)
                    {
                    }
                }
                """),
            (TasksFolder + "20261118200011_MoveTasksFromLegacy.cs", """
                using Microsoft.EntityFrameworkCore.Migrations;
                namespace TemplateApp.Features.Tasks.Persistence.Migrations;
                [Migration("20261118200011_MoveTasksFromLegacy")]
                public partial class MoveTasksFromLegacy : Migration
                {
                    protected override void Up(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.EnsureSchema(name: "tasks");
                        migrationBuilder.RenameTable(name: "TaskItems", schema: "legacy", newName: "TaskItems", newSchema: "tasks");
                    }

                    protected override void Down(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.RenameTable(name: "TaskItems", schema: "tasks", newName: "TaskItems", newSchema: "legacy");
                    }
                }
                """));

        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Id == "DUNE0018"));
    }

    [Fact]
    public async Task AMoveWhoseDownSendsTheTableElsewhereOrDoesMoreOrWhoseUpDoesNotEnsureTheSchemaIsReported()
    {
        var diagnostics = await AnalyzeAsync(
            (TasksFolder + "20261118200012_MoveTasksBackToUsers.cs", """
                using Microsoft.EntityFrameworkCore.Migrations;
                namespace TemplateApp.Features.Tasks.Persistence.Migrations;
                [Migration("20261118200012_MoveTasksBackToUsers")]
                public partial class MoveTasksBackToUsers : Migration
                {
                    protected override void Up(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.EnsureSchema(name: "tasks");
                        migrationBuilder.RenameTable(name: "TaskItems", newName: "TaskItems", newSchema: "tasks");
                    }

                    protected override void Down(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.RenameTable(name: "TaskItems", schema: "tasks", newName: "TaskItems", newSchema: "users");
                    }
                }
                """),
            (TasksFolder + "20261118200013_MoveTasksFromLegacyBackToPublic.cs", """
                using Microsoft.EntityFrameworkCore.Migrations;
                namespace TemplateApp.Features.Tasks.Persistence.Migrations;
                [Migration("20261118200013_MoveTasksFromLegacyBackToPublic")]
                public partial class MoveTasksFromLegacyBackToPublic : Migration
                {
                    protected override void Up(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.EnsureSchema(name: "tasks");
                        migrationBuilder.RenameTable(name: "TaskItems", schema: "legacy", newName: "TaskItems", newSchema: "tasks");
                    }

                    protected override void Down(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.RenameTable(name: "TaskItems", schema: "tasks", newName: "TaskItems", newSchema: "public");
                    }
                }
                """),
            (TasksFolder + "20261118200014_MoveTasksBackAndDrop.cs", """
                using Microsoft.EntityFrameworkCore.Migrations;
                namespace TemplateApp.Features.Tasks.Persistence.Migrations;
                [Migration("20261118200014_MoveTasksBackAndDrop")]
                public partial class MoveTasksBackAndDrop : Migration
                {
                    protected override void Up(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.EnsureSchema(name: "tasks");
                        migrationBuilder.RenameTable(name: "TaskItems", newName: "TaskItems", newSchema: "tasks");
                    }

                    protected override void Down(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.RenameTable(name: "TaskItems", schema: "tasks", newName: "TaskItems", newSchema: "public");
                        migrationBuilder.DropTable(name: "TaskItems");
                    }
                }
                """),
            (TasksFolder + "20261118200015_MoveTasksWithoutSchema.cs", """
                using Microsoft.EntityFrameworkCore.Migrations;
                namespace TemplateApp.Features.Tasks.Persistence.Migrations;
                [Migration("20261118200015_MoveTasksWithoutSchema")]
                public partial class MoveTasksWithoutSchema : Migration
                {
                    protected override void Up(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.EnsureSchema(name: "users");
                        migrationBuilder.RenameTable(name: "TaskItems", newName: "TaskItems", newSchema: "tasks");
                    }

                    protected override void Down(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.RenameTable(name: "TaskItems", schema: "tasks", newName: "TaskItems", newSchema: "public");
                    }
                }
                """));

        Assert.Equal(
            new[]
            {
                ("20261118200012_MoveTasksBackToUsers.cs", 14, "RenameTable", "Migración Move sin vuelta en el Down"),
                ("20261118200013_MoveTasksFromLegacyBackToPublic.cs", 14, "RenameTable", "Migración Move sin vuelta en el Down"),
                ("20261118200014_MoveTasksBackAndDrop.cs", 15, "DropTable", "Migración Move con otras operaciones"),
                ("20261118200015_MoveTasksWithoutSchema.cs", 9, "RenameTable", "Migración Move sin su schema"),
            },
            ReportedWithTitle(diagnostics));
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(params (string Path, string Source)[] files) =>
        AnalyzerHost.AnalyzeAsync(new List<(string, string)>(files), AnalyzerHost.Only<MigrationSafetyAnalyzer>());

    private static (string File, int Line, string Operation)[] Reported(ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics
            .Where(diagnostic => diagnostic.Id == "DUNE0018")
            .Select(diagnostic => (
                System.IO.Path.GetFileName(diagnostic.Location.SourceTree!.FilePath),
                diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1,
                diagnostic.Properties["operation"]!))
            .OrderBy(location => location.Item1, System.StringComparer.Ordinal)
            .ThenBy(location => location.Item2)
            .ToArray();

    private static (string File, int Line, string Operation, string Title)[] ReportedWithTitle(ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics
            .Where(diagnostic => diagnostic.Id == "DUNE0018")
            .Select(diagnostic => (
                System.IO.Path.GetFileName(diagnostic.Location.SourceTree!.FilePath),
                diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1,
                diagnostic.Properties["operation"]!,
                diagnostic.Descriptor.Title.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .OrderBy(location => location.Item1, System.StringComparer.Ordinal)
            .ThenBy(location => location.Item2)
            .ToArray();
}
