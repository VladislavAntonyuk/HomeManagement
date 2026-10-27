using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeManagement.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class _20261027055057_Initial : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AppSettings",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Value = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                ValueType = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AppSettings", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Devices",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Address = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Description = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Devices", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Workflows",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Description = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                ConditionOperator = table.Column<int>(type: "INTEGER", nullable: false),
                RepeatWhileMatched = table.Column<bool>(type: "INTEGER", nullable: false),
                LastTriggeredAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                LastConditionMatched = table.Column<bool>(type: "INTEGER", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Workflows", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "DeviceAction",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Action = table.Column<string>(type: "TEXT", nullable: false),
                CommandType = table.Column<int>(type: "INTEGER", nullable: false),
                Command = table.Column<string>(type: "TEXT", nullable: false),
                CommandArgs = table.Column<string>(type: "TEXT", nullable: true),
                DeviceId = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DeviceAction", x => x.Id);
                table.ForeignKey(
                    name: "FK_DeviceAction_Devices_DeviceId",
                    column: x => x.DeviceId,
                    principalTable: "Devices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "DeviceConfiguration",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", nullable: false),
                Value = table.Column<string>(type: "TEXT", nullable: false),
                DeviceId = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DeviceConfiguration", x => x.Id);
                table.ForeignKey(
                    name: "FK_DeviceConfiguration_Devices_DeviceId",
                    column: x => x.DeviceId,
                    principalTable: "Devices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "WorkflowStep",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Order = table.Column<int>(type: "INTEGER", nullable: false),
                StepType = table.Column<int>(type: "INTEGER", nullable: false),
                DeviceActionId = table.Column<int>(type: "INTEGER", nullable: true),
                DelaySeconds = table.Column<int>(type: "INTEGER", nullable: true),
                NotifyTitle = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                NotifyMessageTemplate = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                WorkflowId = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WorkflowStep", x => x.Id);
                table.ForeignKey(
                    name: "FK_WorkflowStep_DeviceAction_DeviceActionId",
                    column: x => x.DeviceActionId,
                    principalTable: "DeviceAction",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_WorkflowStep_Workflows_WorkflowId",
                    column: x => x.WorkflowId,
                    principalTable: "Workflows",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "WorkflowTriggerCondition",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                TriggerType = table.Column<int>(type: "INTEGER", nullable: false),
                TriggerDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                TriggerDateTo = table.Column<DateOnly>(type: "TEXT", nullable: true),
                TriggerTimeReference = table.Column<int>(type: "INTEGER", nullable: true),
                TriggerTime = table.Column<TimeOnly>(type: "TEXT", nullable: true),
                TriggerTimeOffsetMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                TriggerTimeToReference = table.Column<int>(type: "INTEGER", nullable: true),
                TriggerTimeTo = table.Column<TimeOnly>(type: "TEXT", nullable: true),
                TriggerTimeToOffsetMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                TriggerSourceActionId = table.Column<int>(type: "INTEGER", nullable: true),
                TriggerPropertyPath = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                TriggerExpectedValue = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                TriggerValueType = table.Column<int>(type: "INTEGER", nullable: true),
                TriggerOperator = table.Column<int>(type: "INTEGER", nullable: true),
                LastTriggerDateLocal = table.Column<DateTime>(type: "TEXT", nullable: true),
                LastConditionMatched = table.Column<bool>(type: "INTEGER", nullable: true),
                WorkflowId = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WorkflowTriggerCondition", x => x.Id);
                table.ForeignKey(
                    name: "FK_WorkflowTriggerCondition_DeviceAction_TriggerSourceActionId",
                    column: x => x.TriggerSourceActionId,
                    principalTable: "DeviceAction",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_WorkflowTriggerCondition_Workflows_WorkflowId",
                    column: x => x.WorkflowId,
                    principalTable: "Workflows",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AppSettings_Name",
            table: "AppSettings",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_DeviceAction_DeviceId",
            table: "DeviceAction",
            column: "DeviceId");

        migrationBuilder.CreateIndex(
            name: "IX_DeviceConfiguration_DeviceId",
            table: "DeviceConfiguration",
            column: "DeviceId");

        migrationBuilder.CreateIndex(
            name: "IX_Devices_Name",
            table: "Devices",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Workflows_Name",
            table: "Workflows",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_WorkflowStep_DeviceActionId",
            table: "WorkflowStep",
            column: "DeviceActionId");

        migrationBuilder.CreateIndex(
            name: "IX_WorkflowStep_WorkflowId",
            table: "WorkflowStep",
            column: "WorkflowId");

        migrationBuilder.CreateIndex(
            name: "IX_WorkflowTriggerCondition_TriggerSourceActionId",
            table: "WorkflowTriggerCondition",
            column: "TriggerSourceActionId");

        migrationBuilder.CreateIndex(
            name: "IX_WorkflowTriggerCondition_WorkflowId",
            table: "WorkflowTriggerCondition",
            column: "WorkflowId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AppSettings");

        migrationBuilder.DropTable(
            name: "DeviceConfiguration");

        migrationBuilder.DropTable(
            name: "WorkflowStep");

        migrationBuilder.DropTable(
            name: "WorkflowTriggerCondition");

        migrationBuilder.DropTable(
            name: "DeviceAction");

        migrationBuilder.DropTable(
            name: "Workflows");

        migrationBuilder.DropTable(
            name: "Devices");
    }
}
