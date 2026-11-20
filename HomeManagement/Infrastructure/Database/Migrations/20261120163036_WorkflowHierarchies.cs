using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeManagement.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class _20261120163036_WorkflowHierarchies : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Steps and triggers became one class per kind. Rows that miss data their kind needs could never run, so they are removed.
        migrationBuilder.Sql("""
            DELETE FROM WorkflowStep
            WHERE StepType NOT IN (0, 1, 2)
               OR (StepType = 0 AND DeviceActionId IS NULL)
               OR (StepType = 1 AND (DelaySeconds IS NULL OR DelaySeconds <= 0))
               OR (StepType = 2 AND trim(coalesce(NotifyMessageTemplate, '')) = '');
            UPDATE WorkflowStep SET NotifyTitle = 'Workflow Notification' WHERE StepType = 2 AND trim(coalesce(NotifyTitle, '')) = '';
            UPDATE WorkflowStep SET DeviceActionId = NULL WHERE StepType <> 0;
            UPDATE WorkflowStep SET DelaySeconds = NULL WHERE StepType <> 1;
            UPDATE WorkflowStep SET NotifyTitle = NULL, NotifyMessageTemplate = NULL WHERE StepType <> 2;
            """);

        migrationBuilder.DropForeignKey(
            name: "FK_DeviceAction_Devices_DeviceId",
            table: "DeviceAction");

        migrationBuilder.DropForeignKey(
            name: "FK_WorkflowStep_DeviceAction_DeviceActionId",
            table: "WorkflowStep");

        migrationBuilder.DropForeignKey(
            name: "FK_WorkflowStep_Workflows_WorkflowId",
            table: "WorkflowStep");

        migrationBuilder.DropPrimaryKey(
            name: "PK_WorkflowStep",
            table: "WorkflowStep");

        migrationBuilder.DropPrimaryKey(
            name: "PK_DeviceAction",
            table: "DeviceAction");

        migrationBuilder.RenameTable(
            name: "WorkflowStep",
            newName: "WorkflowSteps");

        migrationBuilder.RenameTable(
            name: "DeviceAction",
            newName: "DeviceActions");

        migrationBuilder.RenameColumn(
            name: "LastConditionMatched",
            table: "Workflows",
            newName: "WasMatched");

        migrationBuilder.RenameColumn(
            name: "NotifyTitle",
            table: "WorkflowSteps",
            newName: "Title");

        migrationBuilder.RenameColumn(
            name: "NotifyMessageTemplate",
            table: "WorkflowSteps",
            newName: "MessageTemplate");

        migrationBuilder.RenameIndex(
            name: "IX_WorkflowStep_WorkflowId",
            table: "WorkflowSteps",
            newName: "IX_WorkflowSteps_WorkflowId");

        migrationBuilder.RenameIndex(
            name: "IX_WorkflowStep_DeviceActionId",
            table: "WorkflowSteps",
            newName: "IX_WorkflowSteps_DeviceActionId");

        migrationBuilder.RenameIndex(
            name: "IX_DeviceAction_DeviceId",
            table: "DeviceActions",
            newName: "IX_DeviceActions_DeviceId");

        migrationBuilder.AddPrimaryKey(
            name: "PK_WorkflowSteps",
            table: "WorkflowSteps",
            column: "Id");

        migrationBuilder.AddPrimaryKey(
            name: "PK_DeviceActions",
            table: "DeviceActions",
            column: "Id");

        migrationBuilder.CreateTable(
            name: "WorkflowTriggers",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Order = table.Column<int>(type: "INTEGER", nullable: false),
                WorkflowId = table.Column<int>(type: "INTEGER", nullable: false),
                TriggerType = table.Column<int>(type: "INTEGER", nullable: false),
                Operator = table.Column<int>(type: "INTEGER", nullable: true),
                Date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                DateTo = table.Column<DateOnly>(type: "TEXT", nullable: true),
                DeviceActionId = table.Column<int>(type: "INTEGER", nullable: true),
                PropertyPath = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                ExpectedValue = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                ValueType = table.Column<int>(type: "INTEGER", nullable: true),
                WasMatched = table.Column<bool>(type: "INTEGER", nullable: true),
                DeviceId = table.Column<int>(type: "INTEGER", nullable: true),
                ExpectedStatus = table.Column<int>(type: "INTEGER", nullable: true),
                HadStatus = table.Column<bool>(type: "INTEGER", nullable: true),
                LastMatchedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                FromTime = table.Column<TimeOnly>(type: "TEXT", nullable: true),
                FromOffsetMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                FromReference = table.Column<int>(type: "INTEGER", nullable: true),
                ToTime = table.Column<TimeOnly>(type: "TEXT", nullable: true),
                ToOffsetMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                ToReference = table.Column<int>(type: "INTEGER", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WorkflowTriggers", x => x.Id);
                table.ForeignKey(
                    name: "FK_WorkflowTriggers_DeviceActions_DeviceActionId",
                    column: x => x.DeviceActionId,
                    principalTable: "DeviceActions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_WorkflowTriggers_Devices_DeviceId",
                    column: x => x.DeviceId,
                    principalTable: "Devices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_WorkflowTriggers_Workflows_WorkflowId",
                    column: x => x.WorkflowId,
                    principalTable: "Workflows",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_WorkflowTriggers_DeviceActionId",
            table: "WorkflowTriggers",
            column: "DeviceActionId");

        migrationBuilder.CreateIndex(
            name: "IX_WorkflowTriggers_DeviceId",
            table: "WorkflowTriggers",
            column: "DeviceId");

        migrationBuilder.CreateIndex(
            name: "IX_WorkflowTriggers_WorkflowId",
            table: "WorkflowTriggers",
            column: "WorkflowId");

        // Copies the triggers with their new column names. Operator 6 and 7 are Between and NotBetween, time reference 0 is a fixed time.
        migrationBuilder.Sql("""
            INSERT INTO WorkflowTriggers (
                Id, "Order", WorkflowId, TriggerType, Operator, Date, DateTo,
                DeviceActionId, PropertyPath, ExpectedValue, ValueType, WasMatched,
                DeviceId, ExpectedStatus, HadStatus, LastMatchedDate,
                FromReference, FromTime, FromOffsetMinutes, ToReference, ToTime, ToOffsetMinutes)
            SELECT
                Id, "Order", WorkflowId, TriggerType, TriggerOperator,
                CASE WHEN TriggerType = 1 THEN TriggerDate END,
                CASE WHEN TriggerType = 1 AND TriggerOperator IN (6, 7) THEN TriggerDateTo END,
                CASE WHEN TriggerType = 3 THEN TriggerSourceActionId END,
                CASE WHEN TriggerType = 3 THEN TriggerPropertyPath END,
                CASE WHEN TriggerType = 3 THEN TriggerExpectedValue END,
                CASE WHEN TriggerType = 3 THEN coalesce(TriggerValueType, 0) END,
                CASE WHEN TriggerType = 3 THEN LastConditionMatched END,
                CASE WHEN TriggerType = 4 THEN TriggerDeviceId END,
                CASE WHEN TriggerType = 4 THEN TriggerDeviceStatus END,
                CASE WHEN TriggerType = 4 THEN LastConditionMatched END,
                CASE WHEN TriggerType = 2 THEN LastTriggerDateLocal END,
                CASE WHEN TriggerType = 2 THEN TriggerTimeReference END,
                CASE WHEN TriggerType = 2 AND TriggerTimeReference = 0 THEN TriggerTime END,
                CASE WHEN TriggerType = 2 THEN CASE WHEN TriggerTimeReference = 0 THEN 0 ELSE coalesce(TriggerTimeOffsetMinutes, 0) END END,
                CASE WHEN TriggerType = 2 AND TriggerOperator IN (6, 7) THEN TriggerTimeToReference END,
                CASE WHEN TriggerType = 2 AND TriggerOperator IN (6, 7) AND TriggerTimeToReference = 0 THEN TriggerTimeTo END,
                CASE WHEN TriggerType = 2 AND TriggerOperator IN (6, 7) AND TriggerTimeToReference IS NOT NULL
                     THEN CASE WHEN TriggerTimeToReference = 0 THEN 0 ELSE coalesce(TriggerTimeToOffsetMinutes, 0) END END
            FROM WorkflowTriggerCondition
            WHERE (TriggerType = 1 AND TriggerOperator IS NOT NULL AND TriggerDate IS NOT NULL)
               OR (TriggerType = 2 AND TriggerOperator IS NOT NULL AND TriggerTimeReference IS NOT NULL
                   AND (TriggerTimeReference <> 0 OR TriggerTime IS NOT NULL))
               OR (TriggerType = 3 AND TriggerOperator IS NOT NULL AND TriggerSourceActionId IS NOT NULL
                   AND trim(coalesce(TriggerPropertyPath, '')) <> '' AND trim(coalesce(TriggerExpectedValue, '')) <> '')
               OR (TriggerType = 4 AND TriggerDeviceId IS NOT NULL AND TriggerDeviceStatus IS NOT NULL);
            """);

        migrationBuilder.DropTable(
            name: "WorkflowTriggerCondition");

        migrationBuilder.AddForeignKey(
            name: "FK_DeviceActions_Devices_DeviceId",
            table: "DeviceActions",
            column: "DeviceId",
            principalTable: "Devices",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_WorkflowSteps_DeviceActions_DeviceActionId",
            table: "WorkflowSteps",
            column: "DeviceActionId",
            principalTable: "DeviceActions",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_WorkflowSteps_Workflows_WorkflowId",
            table: "WorkflowSteps",
            column: "WorkflowId",
            principalTable: "Workflows",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_DeviceActions_Devices_DeviceId",
            table: "DeviceActions");

        migrationBuilder.DropForeignKey(
            name: "FK_WorkflowSteps_DeviceActions_DeviceActionId",
            table: "WorkflowSteps");

        migrationBuilder.DropForeignKey(
            name: "FK_WorkflowSteps_Workflows_WorkflowId",
            table: "WorkflowSteps");

        migrationBuilder.DropTable(
            name: "WorkflowTriggers");

        migrationBuilder.DropPrimaryKey(
            name: "PK_WorkflowSteps",
            table: "WorkflowSteps");

        migrationBuilder.DropPrimaryKey(
            name: "PK_DeviceActions",
            table: "DeviceActions");

        migrationBuilder.RenameTable(
            name: "WorkflowSteps",
            newName: "WorkflowStep");

        migrationBuilder.RenameTable(
            name: "DeviceActions",
            newName: "DeviceAction");

        migrationBuilder.RenameColumn(
            name: "WasMatched",
            table: "Workflows",
            newName: "LastConditionMatched");

        migrationBuilder.RenameColumn(
            name: "Title",
            table: "WorkflowStep",
            newName: "NotifyTitle");

        migrationBuilder.RenameColumn(
            name: "MessageTemplate",
            table: "WorkflowStep",
            newName: "NotifyMessageTemplate");

        migrationBuilder.RenameIndex(
            name: "IX_WorkflowSteps_WorkflowId",
            table: "WorkflowStep",
            newName: "IX_WorkflowStep_WorkflowId");

        migrationBuilder.RenameIndex(
            name: "IX_WorkflowSteps_DeviceActionId",
            table: "WorkflowStep",
            newName: "IX_WorkflowStep_DeviceActionId");

        migrationBuilder.RenameIndex(
            name: "IX_DeviceActions_DeviceId",
            table: "DeviceAction",
            newName: "IX_DeviceAction_DeviceId");

        migrationBuilder.AddPrimaryKey(
            name: "PK_WorkflowStep",
            table: "WorkflowStep",
            column: "Id");

        migrationBuilder.AddPrimaryKey(
            name: "PK_DeviceAction",
            table: "DeviceAction",
            column: "Id");

        migrationBuilder.CreateTable(
            name: "WorkflowTriggerCondition",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                TriggerDeviceId = table.Column<int>(type: "INTEGER", nullable: true),
                TriggerSourceActionId = table.Column<int>(type: "INTEGER", nullable: true),
                LastConditionMatched = table.Column<bool>(type: "INTEGER", nullable: true),
                LastTriggerDateLocal = table.Column<DateTime>(type: "TEXT", nullable: true),
                Order = table.Column<int>(type: "INTEGER", nullable: false),
                TriggerDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                TriggerDateTo = table.Column<DateOnly>(type: "TEXT", nullable: true),
                TriggerDeviceStatus = table.Column<int>(type: "INTEGER", nullable: true),
                TriggerExpectedValue = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                TriggerOperator = table.Column<int>(type: "INTEGER", nullable: true),
                TriggerPropertyPath = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                TriggerTime = table.Column<TimeOnly>(type: "TEXT", nullable: true),
                TriggerTimeOffsetMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                TriggerTimeReference = table.Column<int>(type: "INTEGER", nullable: true),
                TriggerTimeTo = table.Column<TimeOnly>(type: "TEXT", nullable: true),
                TriggerTimeToOffsetMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                TriggerTimeToReference = table.Column<int>(type: "INTEGER", nullable: true),
                TriggerType = table.Column<int>(type: "INTEGER", nullable: false),
                TriggerValueType = table.Column<int>(type: "INTEGER", nullable: true),
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
                    name: "FK_WorkflowTriggerCondition_Devices_TriggerDeviceId",
                    column: x => x.TriggerDeviceId,
                    principalTable: "Devices",
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
            name: "IX_WorkflowTriggerCondition_TriggerDeviceId",
            table: "WorkflowTriggerCondition",
            column: "TriggerDeviceId");

        migrationBuilder.CreateIndex(
            name: "IX_WorkflowTriggerCondition_TriggerSourceActionId",
            table: "WorkflowTriggerCondition",
            column: "TriggerSourceActionId");

        migrationBuilder.CreateIndex(
            name: "IX_WorkflowTriggerCondition_WorkflowId",
            table: "WorkflowTriggerCondition",
            column: "WorkflowId");

        migrationBuilder.AddForeignKey(
            name: "FK_DeviceAction_Devices_DeviceId",
            table: "DeviceAction",
            column: "DeviceId",
            principalTable: "Devices",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_WorkflowStep_DeviceAction_DeviceActionId",
            table: "WorkflowStep",
            column: "DeviceActionId",
            principalTable: "DeviceAction",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_WorkflowStep_Workflows_WorkflowId",
            table: "WorkflowStep",
            column: "WorkflowId",
            principalTable: "Workflows",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);
    }
}
