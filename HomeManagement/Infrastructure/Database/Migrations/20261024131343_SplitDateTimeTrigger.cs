using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeManagement.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class _20261024131343_SplitDateTimeTrigger : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateOnly>(
            name: "TriggerDate",
            table: "WorkflowTriggerCondition",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "TriggerDateTo",
            table: "WorkflowTriggerCondition",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "TriggerTimeReference",
            table: "WorkflowTriggerCondition",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<TimeOnly>(
            name: "TriggerTime",
            table: "WorkflowTriggerCondition",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "TriggerTimeOffsetMinutes",
            table: "WorkflowTriggerCondition",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "TriggerTimeToReference",
            table: "WorkflowTriggerCondition",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<TimeOnly>(
            name: "TriggerTimeTo",
            table: "WorkflowTriggerCondition",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "TriggerTimeToOffsetMinutes",
            table: "WorkflowTriggerCondition",
            type: "INTEGER",
            nullable: true);

        // Former trigger types: Manual = 0, DateTime = 1, Sunrise = 2, Sunset = 3, DeviceResult = 4.
        // New trigger types: Manual = 0, Date = 1, Time = 2, DeviceResult = 3.
        // Time references: Fixed = 0, Sunrise = 1, Sunset = 2. Operator Equal = 4 means "at".

        // Sunrise and Sunset conditions become "Time at Sunrise" and "Time at Sunset", keeping their once-per-day state.
        // The removed global SunriseAdjustmentMinutes (-30) and SunsetAdjustmentMinutes (30) settings move into the condition offset.
        migrationBuilder.Sql("""
            UPDATE WorkflowTriggerCondition
            SET TriggerTimeReference = TriggerType - 1,
                TriggerTimeOffsetMinutes = CASE TriggerType WHEN 2 THEN -30 ELSE 30 END,
                TriggerOperator = 4,
                TriggerType = 2
            WHERE TriggerType IN (2, 3);
            """);
        migrationBuilder.Sql("UPDATE WorkflowTriggerCondition SET TriggerType = 3 WHERE TriggerType = 4;");

        // Each DateTime condition becomes "Date on <date>" plus "Time at <time>" (operator Equal = 4, reference Fixed = 0).
        migrationBuilder.Sql("""
            INSERT INTO WorkflowTriggerCondition (TriggerType, TriggerOperator, TriggerTimeReference, TriggerTime, WorkflowId)
            SELECT 2, 4, 0, time(ScheduledAt, 'localtime'), WorkflowId
            FROM WorkflowTriggerCondition
            WHERE TriggerType = 1 AND ScheduledAt IS NOT NULL;
            """);
        migrationBuilder.Sql("""
            UPDATE WorkflowTriggerCondition
            SET TriggerOperator = 4, TriggerDate = date(ScheduledAt, 'localtime')
            WHERE TriggerType = 1 AND ScheduledAt IS NOT NULL;
            """);

        // Native DROP COLUMN keeps the migration inside its transaction, unlike EF's table rebuild.
        migrationBuilder.Sql("ALTER TABLE WorkflowTriggerCondition DROP COLUMN ScheduledAt;");
        migrationBuilder.Sql("ALTER TABLE WorkflowTriggerCondition DROP COLUMN LastTriggeredAtUtc;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ScheduledAt",
            table: "WorkflowTriggerCondition",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "LastTriggeredAtUtc",
            table: "WorkflowTriggerCondition",
            type: "TEXT",
            nullable: true);

        // Only "Time at Sunrise/Sunset" maps back (to Sunrise = 2, Sunset = 3); other Date and Time conditions cannot be represented.
        migrationBuilder.Sql("""
            DELETE FROM WorkflowTriggerCondition
            WHERE TriggerType = 1
               OR (TriggerType = 2 AND NOT (TriggerOperator = 4 AND TriggerTimeReference IN (1, 2)));
            """);
        migrationBuilder.Sql("UPDATE WorkflowTriggerCondition SET TriggerType = 4 WHERE TriggerType = 3;");
        migrationBuilder.Sql("UPDATE WorkflowTriggerCondition SET TriggerType = TriggerTimeReference + 1 WHERE TriggerType = 2;");
        migrationBuilder.Sql("ALTER TABLE WorkflowTriggerCondition DROP COLUMN TriggerDate;");
        migrationBuilder.Sql("ALTER TABLE WorkflowTriggerCondition DROP COLUMN TriggerDateTo;");
        migrationBuilder.Sql("ALTER TABLE WorkflowTriggerCondition DROP COLUMN TriggerTimeReference;");
        migrationBuilder.Sql("ALTER TABLE WorkflowTriggerCondition DROP COLUMN TriggerTime;");
        migrationBuilder.Sql("ALTER TABLE WorkflowTriggerCondition DROP COLUMN TriggerTimeOffsetMinutes;");
        migrationBuilder.Sql("ALTER TABLE WorkflowTriggerCondition DROP COLUMN TriggerTimeToReference;");
        migrationBuilder.Sql("ALTER TABLE WorkflowTriggerCondition DROP COLUMN TriggerTimeTo;");
        migrationBuilder.Sql("ALTER TABLE WorkflowTriggerCondition DROP COLUMN TriggerTimeToOffsetMinutes;");
    }
}
