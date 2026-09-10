using drainIQ.Models;
using drainIQ.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace drainIQ.Data.Seed;

/// <summary>
/// Dev-only demo data: a handful of Rijeka manhole devices, a threshold rule per
/// device, ~3 days of hourly measurements each, and a couple of alarm instances
/// in different lifecycle states - enough shape for frontend work without a real
/// device fleet. Skips entirely if any device already exists.
/// </summary>
public static class DbSeeder
{
    private const string DemoEmail = "test@drainiq.hr";
    private const string DemoPassword = "Test123!@#";

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        if (await db.Devices.AnyAsync())
        {
            return;
        }

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var alarmEvaluation = scope.ServiceProvider.GetRequiredService<AlarmEvaluationService>();

        var user = await EnsureDemoUserAsync(userManager);

        var devices = new List<Device>
        {
            new() { DeviceName = "Korzo", Lat = 45.3271m, Long = 14.4422m, Description = "Manhole sensor - Korzo, gradski centar" },
            new() { DeviceName = "Riva - Ponte Rosso", Lat = 45.3255m, Long = 14.4407m, Description = "Manhole sensor - luka, blizina mora" },
            new() { DeviceName = "Mlaka", Lat = 45.3222m, Long = 14.4241m, Description = "Manhole sensor - Mlaka" },
            new() { DeviceName = "Podmurvice", Lat = 45.3315m, Long = 14.4614m, Description = "Manhole sensor - Podmurvice" },
            new() { DeviceName = "Škurinje", Lat = 45.3172m, Long = 14.4763m, Description = "Manhole sensor - Škurinje" }
        };
        db.Devices.AddRange(devices);
        await db.SaveChangesAsync();

        db.UserDevices.AddRange(devices.Select(d => new UserDevice { UserId = user.Id, DeviceId = d.DeviceId }));

        var rules = devices.ToDictionary(d => d.DeviceId, d => new AlarmRule
        {
            DeviceId = d.DeviceId,
            AlarmName = "Visoka razina vode",
            AlarmType = "water_level",
            ThresholdValue = 15m,
            Comparator = "<="
        });
        db.AlarmRules.AddRange(rules.Values);
        await db.SaveChangesAsync();

        var random = new Random(42);
        const int readingCount = 72; // hourly for the last 3 days
        var start = DateTimeOffset.UtcNow.AddHours(-readingCount);

        // Every device dips under threshold twice: an older breach (to be marked
        // acknowledged/resolved) and a recent one (left "triggered" - active alarm).
        var oldBreach = (from: 20, to: 23);
        var recentBreach = (from: readingCount - 6, to: readingCount - 4);

        foreach (var device in devices)
        {
            var measurements = new List<Measurement>();
            var baseline = 35m + random.Next(0, 20); // this device's normal distance from sensor top, cm

            for (var i = 0; i < readingCount; i++)
            {
                // Noise stays centered on the baseline (not a cumulative random walk) so it
                // can't drift into "alarm" territory by chance outside the two flood windows.
                var level = i >= oldBreach.from && i <= oldBreach.to || i >= recentBreach.from && i <= recentBreach.to
                    ? 6m + random.Next(0, 6) // simulated flood spike
                    : baseline + random.Next(-5, 6);

                measurements.Add(new Measurement
                {
                    DeviceId = device.DeviceId,
                    SentAt = start.AddHours(i),
                    WaterLevelFromTopCm = Math.Round(level, 1)
                });
            }

            db.Measurements.AddRange(measurements);
            await db.SaveChangesAsync();

            var triggeredForOldBreach = new List<AlarmInstance>();

            foreach (var measurement in measurements)
            {
                var triggered = await alarmEvaluation.EvaluateAsync(measurement);
                if (triggered.Count > 0 && measurement.SentAt <= start.AddHours(oldBreach.to))
                {
                    triggeredForOldBreach.AddRange(triggered);
                }
            }

            // Older alarm burst: first instance acknowledged, rest resolved - shows both
            // lifecycle states on the frontend. The recent burst is left untouched
            // ("triggered"), representing an alarm that's still active right now.
            for (var j = 0; j < triggeredForOldBreach.Count; j++)
            {
                var instance = triggeredForOldBreach[j];
                instance.Status = j == 0 ? AlarmStatus.Acknowledged : AlarmStatus.Resolved;
                instance.AcknowledgedAt = instance.TriggeredAt.AddMinutes(15);
                if (instance.Status == AlarmStatus.Resolved)
                {
                    instance.ResolvedAt = instance.TriggeredAt.AddMinutes(40);
                }
            }

            if (triggeredForOldBreach.Count > 0)
            {
                await db.SaveChangesAsync();
            }
        }
    }

    private static async Task<ApplicationUser> EnsureDemoUserAsync(UserManager<ApplicationUser> userManager)
    {
        var existing = await userManager.FindByEmailAsync(DemoEmail);
        if (existing is not null)
        {
            return existing;
        }

        var user = new ApplicationUser { UserName = DemoEmail, Email = DemoEmail };
        var result = await userManager.CreateAsync(user, DemoPassword);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to create demo user: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }

        return user;
    }
}
