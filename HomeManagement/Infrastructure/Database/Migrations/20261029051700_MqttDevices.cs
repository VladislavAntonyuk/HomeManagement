using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeManagement.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class _20261029051700_MqttDevices : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "DeviceConfiguration");

        // Native DROP COLUMN (SQLite 3.35+) instead of DropColumn, which rebuilds the table outside of the migration transaction.
        migrationBuilder.Sql("ALTER TABLE \"DeviceAction\" DROP COLUMN \"CommandArgs\";");

        migrationBuilder.RenameColumn(
            name: "Address",
            table: "Devices",
            newName: "DeviceId");

        migrationBuilder.CreateIndex(
            name: "IX_Devices_DeviceId",
            table: "Devices",
            column: "DeviceId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Devices_DeviceId",
            table: "Devices");

        migrationBuilder.RenameColumn(
            name: "DeviceId",
            table: "Devices",
            newName: "Address");

        migrationBuilder.AddColumn<string>(
            name: "CommandArgs",
            table: "DeviceAction",
            type: "TEXT",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "DeviceConfiguration",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                DeviceId = table.Column<int>(type: "INTEGER", nullable: false),
                Name = table.Column<string>(type: "TEXT", nullable: false),
                Value = table.Column<string>(type: "TEXT", nullable: false)
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

        migrationBuilder.CreateIndex(
            name: "IX_DeviceConfiguration_DeviceId",
            table: "DeviceConfiguration",
            column: "DeviceId");
    }
}
