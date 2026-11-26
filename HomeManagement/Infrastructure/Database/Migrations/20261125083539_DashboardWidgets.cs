using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeManagement.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class _20261125083539_DashboardWidgets : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DashboardWidgets",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                DeviceActionId = table.Column<int>(type: "INTEGER", nullable: false),
                Title = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                PropertyPath = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                Unit = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                Order = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DashboardWidgets", x => x.Id);
                table.ForeignKey(
                    name: "FK_DashboardWidgets_DeviceActions_DeviceActionId",
                    column: x => x.DeviceActionId,
                    principalTable: "DeviceActions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_DashboardWidgets_DeviceActionId",
            table: "DashboardWidgets",
            column: "DeviceActionId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "DashboardWidgets");
    }
}
