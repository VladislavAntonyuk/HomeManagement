using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeManagement.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class _20261125090753_DashboardWidgetProperties : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Properties",
            table: "DashboardWidgets",
            type: "TEXT",
            nullable: false,
            defaultValue: "[]");

        // A widget that showed one property keeps it, with its unit.
        migrationBuilder.Sql("""
            UPDATE "DashboardWidgets"
            SET "Properties" = json_array(json_object('Path', "PropertyPath", 'Label', NULL, 'Unit', "Unit"))
            WHERE "PropertyPath" IS NOT NULL;
            """);

        migrationBuilder.DropColumn(
            name: "PropertyPath",
            table: "DashboardWidgets");

        migrationBuilder.DropColumn(
            name: "Unit",
            table: "DashboardWidgets");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PropertyPath",
            table: "DashboardWidgets",
            type: "TEXT",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Unit",
            table: "DashboardWidgets",
            type: "TEXT",
            maxLength: 20,
            nullable: true);

        // Only the first property fits the old columns.
        migrationBuilder.Sql("""
            UPDATE "DashboardWidgets"
            SET "PropertyPath" = json_extract("Properties", '$[0].Path'),
                "Unit" = json_extract("Properties", '$[0].Unit');
            """);

        migrationBuilder.DropColumn(
            name: "Properties",
            table: "DashboardWidgets");
    }
}
