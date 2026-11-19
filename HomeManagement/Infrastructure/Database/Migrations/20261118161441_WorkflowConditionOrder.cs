using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeManagement.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class _20261118161441_WorkflowConditionOrder : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "Order",
            table: "WorkflowTriggerCondition",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0);

        // Existing conditions keep the order they were added in.
        migrationBuilder.Sql(
            """
            UPDATE "WorkflowTriggerCondition"
            SET "Order" = (
                SELECT COUNT(*)
                FROM "WorkflowTriggerCondition" AS previous
                WHERE previous."WorkflowId" = "WorkflowTriggerCondition"."WorkflowId"
                  AND previous."Id" < "WorkflowTriggerCondition"."Id");
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Order",
            table: "WorkflowTriggerCondition");
    }
}
