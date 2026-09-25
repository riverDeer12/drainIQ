using drainIQ.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace drainIQ.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Measurement> Measurements => Set<Measurement>();
    public DbSet<UserDevice> UserDevices => Set<UserDevice>();
    public DbSet<AlarmRule> AlarmRules => Set<AlarmRule>();
    public DbSet<AlarmInstance> AlarmInstances => Set<AlarmInstance>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Device>(entity =>
        {
            entity.ToTable("devices");
            entity.Property(d => d.DeviceId).HasColumnName("device_id");
            entity.Property(d => d.DeviceName).HasColumnName("device_name").HasMaxLength(255).IsRequired();
            entity.Property(d => d.Lat).HasColumnName("lat").HasColumnType("decimal(9,6)");
            entity.Property(d => d.Long).HasColumnName("long").HasColumnType("decimal(9,6)");
            entity.Property(d => d.Description).HasColumnName("description");
            entity.Property(d => d.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            entity.Property(d => d.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            // trg_devices_updated_at sets this on every UPDATE too, not just insert - tell EF
            // to re-read it back after both so responses don't return a stale value.
            entity.Property(d => d.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();
        });

        builder.Entity<Measurement>(entity =>
        {
            entity.ToTable("measurements", tb =>
                tb.HasCheckConstraint("CK_measurements_battery_level_pct", "battery_level_pct IS NULL OR battery_level_pct BETWEEN 0 AND 100"));

            entity.Property(m => m.MeasurementId).HasColumnName("measurement_id");
            entity.Property(m => m.DeviceId).HasColumnName("device_id");
            entity.Property(m => m.SentAt).HasColumnName("sent_at");
            entity.Property(m => m.WaterLevelFromTopCm).HasColumnName("water_level_from_top_cm").HasColumnType("decimal(6,2)");
            entity.Property(m => m.BatteryLevelPct).HasColumnName("battery_level_pct");
            entity.Property(m => m.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

            entity.HasOne(m => m.Device)
                .WithMany(d => d.Measurements)
                .HasForeignKey(m => m.DeviceId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(m => new { m.DeviceId, m.SentAt }).HasDatabaseName("idx_measurements_device_sent_at");
        });

        builder.Entity<UserDevice>(entity =>
        {
            entity.ToTable("user_device");
            entity.HasKey(ud => new { ud.UserId, ud.DeviceId });
            entity.Property(ud => ud.UserId).HasColumnName("user_id");
            entity.Property(ud => ud.DeviceId).HasColumnName("device_id");
            entity.Property(ud => ud.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

            entity.HasOne(ud => ud.User)
                .WithMany(u => u.UserDevices)
                .HasForeignKey(ud => ud.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(ud => ud.Device)
                .WithMany(d => d.UserDevices)
                .HasForeignKey(ud => ud.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AlarmRule>(entity =>
        {
            entity.ToTable("alarm_rules", tb =>
                tb.HasCheckConstraint("CK_alarm_rules_comparator", "comparator IN ('<', '<=', '>', '>=', '=')"));

            entity.HasKey(r => r.RuleId);
            entity.Property(r => r.RuleId).HasColumnName("rule_id");
            entity.Property(r => r.DeviceId).HasColumnName("device_id");
            entity.Property(r => r.AlarmName).HasColumnName("alarm_name").HasMaxLength(255).IsRequired();
            entity.Property(r => r.AlarmType).HasColumnName("alarm_type").HasMaxLength(100).IsRequired();
            entity.Property(r => r.ThresholdValue).HasColumnName("threshold_value").HasColumnType("decimal(6,2)");
            entity.Property(r => r.Comparator).HasColumnName("comparator").HasMaxLength(2).IsRequired();
            entity.Property(r => r.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            entity.Property(r => r.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            // trg_alarm_rules_updated_at sets this on every UPDATE too, not just insert.
            entity.Property(r => r.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();

            entity.HasOne(r => r.Device)
                .WithMany(d => d.AlarmRules)
                .HasForeignKey(r => r.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(r => r.DeviceId).HasDatabaseName("idx_alarm_rules_device");
        });

        builder.Entity<AlarmInstance>(entity =>
        {
            entity.ToTable("alarm_instances", tb =>
                tb.HasCheckConstraint("CK_alarm_instances_status", "status IN ('triggered', 'acknowledged', 'resolved')"));

            entity.HasKey(a => a.AlarmId);
            entity.Property(a => a.AlarmId).HasColumnName("alarm_id");
            entity.Property(a => a.RuleId).HasColumnName("rule_id");
            entity.Property(a => a.MeasurementId).HasColumnName("measurement_id");
            entity.Property(a => a.TriggeredAt).HasColumnName("triggered_at").HasDefaultValueSql("now()");
            entity.Property(a => a.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue(AlarmStatus.Triggered);
            entity.Property(a => a.AcknowledgedAt).HasColumnName("acknowledged_at");
            entity.Property(a => a.ResolvedAt).HasColumnName("resolved_at");

            entity.HasOne(a => a.Rule)
                .WithMany(r => r.AlarmInstances)
                .HasForeignKey(a => a.RuleId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(a => a.Measurement)
                .WithMany(m => m.AlarmInstances)
                .HasForeignKey(a => a.MeasurementId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(a => new { a.RuleId, a.TriggeredAt }).HasDatabaseName("idx_alarm_instances_rule_triggered_at");
        });
    }
}
