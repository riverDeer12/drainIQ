using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace drainIQ.Migrations
{
    /// <inheritdoc />
    public partial class AddAlarmTypeCheckConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_alarm_rules_alarm_type",
                table: "alarm_rules",
                sql: "alarm_type IN ('water_level', 'low_battery')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_alarm_rules_alarm_type",
                table: "alarm_rules");
        }
    }
}
