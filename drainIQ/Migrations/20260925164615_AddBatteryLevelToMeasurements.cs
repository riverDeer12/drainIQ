using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace drainIQ.Migrations
{
    /// <inheritdoc />
    public partial class AddBatteryLevelToMeasurements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "battery_level_pct",
                table: "measurements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_measurements_battery_level_pct",
                table: "measurements",
                sql: "battery_level_pct IS NULL OR battery_level_pct BETWEEN 0 AND 100");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_measurements_battery_level_pct",
                table: "measurements");

            migrationBuilder.DropColumn(
                name: "battery_level_pct",
                table: "measurements");
        }
    }
}
