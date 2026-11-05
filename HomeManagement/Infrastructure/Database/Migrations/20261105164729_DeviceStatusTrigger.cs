using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeManagement.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class _20261105164729_DeviceStatusTrigger : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "TriggerDeviceId",
            table: "WorkflowTriggerCondition",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "TriggerDeviceStatus",
            table: "WorkflowTriggerCondition",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_WorkflowTriggerCondition_TriggerDeviceId",
            table: "WorkflowTriggerCondition",
            column: "TriggerDeviceId");

        migrationBuilder.AddForeignKey(
            name: "FK_WorkflowTriggerCondition_Devices_TriggerDeviceId",
            table: "WorkflowTriggerCondition",
            column: "TriggerDeviceId",
            principalTable: "Devices",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_WorkflowTriggerCondition_Devices_TriggerDeviceId",
            table: "WorkflowTriggerCondition");

        migrationBuilder.DropIndex(
            name: "IX_WorkflowTriggerCondition_TriggerDeviceId",
            table: "WorkflowTriggerCondition");

        migrationBuilder.DropColumn(
            name: "TriggerDeviceId",
            table: "WorkflowTriggerCondition");

        migrationBuilder.DropColumn(
            name: "TriggerDeviceStatus",
            table: "WorkflowTriggerCondition");
    }
}
