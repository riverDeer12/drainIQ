-- =====================================================================
-- Schema: Water level monitoring system (drainIQ)
-- Database: PostgreSQL 13+
-- =====================================================================

CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- ---------------------------------------------------------------------
-- Helper function to auto-update the updated_at column
-- (same pattern Laravel migrations normally handle at the ORM level,
-- here moved to the database level via a trigger)
-- ---------------------------------------------------------------------
CREATE OR REPLACE FUNCTION set_updated_at()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = now();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- =====================================================================
-- AspNetUsers - matches the table ASP.NET Core Identity generates by
-- default (IdentityUser<Guid>) when scaffolded with Npgsql/EF Core.
-- In practice this table (plus AspNetRoles/AspNetUserRoles/etc.) will
-- be created by EF Core Identity migrations, not by this script - kept
-- here for reference/documentation of the shape.
-- =====================================================================
CREATE TABLE "AspNetUsers" (
    "Id"                    UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "UserName"              VARCHAR(256) NULL,
    "NormalizedUserName"    VARCHAR(256) NULL,
    "Email"                 VARCHAR(256) NULL,
    "NormalizedEmail"       VARCHAR(256) NULL,
    "EmailConfirmed"        BOOLEAN NOT NULL DEFAULT false,
    "PasswordHash"          TEXT NULL,
    "SecurityStamp"         TEXT NULL,
    "ConcurrencyStamp"      TEXT NULL,
    "PhoneNumber"           VARCHAR(32) NULL,
    "PhoneNumberConfirmed"  BOOLEAN NOT NULL DEFAULT false,
    "TwoFactorEnabled"      BOOLEAN NOT NULL DEFAULT false,
    "LockoutEnd"            TIMESTAMPTZ NULL,
    "LockoutEnabled"        BOOLEAN NOT NULL DEFAULT true,
    "AccessFailedCount"     INTEGER NOT NULL DEFAULT 0
);

CREATE UNIQUE INDEX "UserNameIndex" ON "AspNetUsers" ("NormalizedUserName");
CREATE INDEX "EmailIndex" ON "AspNetUsers" ("NormalizedEmail");

-- =====================================================================
-- DEVICES
-- is_active is a soft-delete flag: measurements/alarm history reference
-- devices with ON DELETE RESTRICT (see below), so decommissioning a
-- device is done by flipping is_active rather than deleting the row -
-- this preserves historical measurements/alarms for that device.
-- =====================================================================
CREATE TABLE devices (
    device_id           SERIAL PRIMARY KEY,
    device_name         VARCHAR(255) NOT NULL,
    lat                 DECIMAL(9,6) NOT NULL,
    long                DECIMAL(9,6) NOT NULL,
    description         TEXT,
    is_active           BOOLEAN NOT NULL DEFAULT true,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TRIGGER trg_devices_updated_at
    BEFORE UPDATE ON devices
    FOR EACH ROW
    EXECUTE FUNCTION set_updated_at();

-- =====================================================================
-- MEASUREMENTS
-- water_level_from_top_cm is the distance from the sensor top to the
-- water surface, expressed in centimeters.
-- device_id is ON DELETE RESTRICT: measurement history is evidentiary
-- data (flood-warning audit trail) and must not disappear as a side
-- effect of removing a device - decommission via devices.is_active
-- instead.
-- =====================================================================
CREATE TABLE measurements (
    measurement_id           BIGSERIAL PRIMARY KEY,
    device_id                INTEGER NOT NULL REFERENCES devices(device_id) ON DELETE RESTRICT,
    sent_at                  TIMESTAMPTZ NOT NULL,
    water_level_from_top_cm  DECIMAL(6,2) NOT NULL,
    created_at                TIMESTAMPTZ NOT NULL DEFAULT now()
);

COMMENT ON COLUMN measurements.water_level_from_top_cm IS 'Distance from sensor top to water surface, in centimeters (cm)';

-- Composite index for fast queries like "last 24h for device X"
CREATE INDEX idx_measurements_device_sent_at
    ON measurements (device_id, sent_at DESC);

-- =====================================================================
-- USER_DEVICE (pivot table: which user follows which device)
-- =====================================================================
CREATE TABLE user_device (
    user_id              UUID NOT NULL REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,
    device_id            INTEGER NOT NULL REFERENCES devices(device_id) ON DELETE CASCADE,
    created_at           TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (user_id, device_id)
);

-- =====================================================================
-- ALARM_RULES (alarm definitions / thresholds, per device)
-- device_id stays ON DELETE CASCADE: rules are configuration, not
-- historical evidence, so it's fine for them to disappear with the
-- device (which is normally soft-deleted anyway - see devices.is_active).
-- =====================================================================
CREATE TABLE alarm_rules (
    rule_id              SERIAL PRIMARY KEY,
    device_id            INTEGER NOT NULL REFERENCES devices(device_id) ON DELETE CASCADE,
    alarm_name           VARCHAR(255) NOT NULL,
    alarm_type           VARCHAR(100) NOT NULL,
    threshold_value      DECIMAL(6,2) NOT NULL,
    comparator           VARCHAR(2) NOT NULL CHECK (comparator IN ('<', '<=', '>', '>=', '=')),
    is_active            BOOLEAN NOT NULL DEFAULT true,
    created_at           TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at           TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TRIGGER trg_alarm_rules_updated_at
    BEFORE UPDATE ON alarm_rules
    FOR EACH ROW
    EXECUTE FUNCTION set_updated_at();

CREATE INDEX idx_alarm_rules_device ON alarm_rules (device_id);

-- =====================================================================
-- ALARM_INSTANCES (alarms that actually fired, referencing the rule and
-- measurement that satisfied the condition)
-- rule_id / measurement_id are ON DELETE RESTRICT: an alarm instance is
-- the audit trail proving a flood warning fired, so it must not vanish
-- as a side effect of deleting the rule that defined it or the
-- measurement that triggered it.
-- status tracks the alarm lifecycle: triggered -> acknowledged -> resolved.
-- =====================================================================
CREATE TABLE alarm_instances (
    alarm_id             BIGSERIAL PRIMARY KEY,
    rule_id              INTEGER NOT NULL REFERENCES alarm_rules(rule_id) ON DELETE RESTRICT,
    measurement_id        BIGINT NOT NULL REFERENCES measurements(measurement_id) ON DELETE RESTRICT,
    triggered_at           TIMESTAMPTZ NOT NULL DEFAULT now(),
    status                VARCHAR(20) NOT NULL DEFAULT 'triggered' CHECK (status IN ('triggered', 'acknowledged', 'resolved')),
    acknowledged_at       TIMESTAMPTZ NULL,
    resolved_at           TIMESTAMPTZ NULL
);

-- Composite index for queries like "alarms for a rule in the last N days"
CREATE INDEX idx_alarm_instances_rule_triggered_at
    ON alarm_instances (rule_id, triggered_at DESC);

-- =====================================================================
-- End of script
-- =====================================================================
