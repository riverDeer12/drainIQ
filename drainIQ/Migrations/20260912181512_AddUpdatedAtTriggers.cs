using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace drainIQ.Migrations
{
    /// <inheritdoc />
    public partial class AddUpdatedAtTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // schema.sql always documented this trigger, but the real schema comes from EF
            // migrations (which don't know about triggers/functions - they're invisible to
            // the C# model), so it was never actually applied. Without it, updated_at only
            // ever got set on insert, never on update.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION set_updated_at()
                RETURNS TRIGGER AS $$
                BEGIN
                    NEW.updated_at = now();
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_devices_updated_at
                    BEFORE UPDATE ON devices
                    FOR EACH ROW
                    EXECUTE FUNCTION set_updated_at();
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_alarm_rules_updated_at
                    BEFORE UPDATE ON alarm_rules
                    FOR EACH ROW
                    EXECUTE FUNCTION set_updated_at();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_alarm_rules_updated_at ON alarm_rules;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_devices_updated_at ON devices;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS set_updated_at();");
        }
    }
}
