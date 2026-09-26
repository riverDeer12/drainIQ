using drainIQ.Data;
using drainIQ.Models;
using Microsoft.EntityFrameworkCore;

namespace drainIQ.Services;

/// <summary>
/// Periodically checks device_offline alarm rules against each device's most recent
/// measurement. Unlike AlarmEvaluationService (which reacts to a new measurement
/// arriving), "no data arrived" can only be noticed by a clock, not an event.
/// </summary>
public class DeviceOfflineCheckService(IServiceScopeFactory scopeFactory, ILogger<DeviceOfflineCheckService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval);

        do
        {
            try
            {
                await CheckAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Device offline check failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task CheckAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var rules = await db.AlarmRules
            .Where(r => r.IsActive && r.AlarmType == AlarmTypes.DeviceOffline)
            .ToListAsync(cancellationToken);

        var createdCount = 0;

        foreach (var rule in rules)
        {
            var lastMeasurement = await db.Measurements
                .Where(m => m.DeviceId == rule.DeviceId)
                .OrderByDescending(m => m.SentAt)
                .FirstOrDefaultAsync(cancellationToken);

            // Never reported anything at all - nothing to measure staleness against yet.
            if (lastMeasurement is null)
            {
                continue;
            }

            var minutesSinceLastReading = (decimal)(DateTimeOffset.UtcNow - lastMeasurement.SentAt).TotalMinutes;

            if (!AlarmComparators.IsSatisfied(rule.Comparator, minutesSinceLastReading, rule.ThresholdValue))
            {
                continue;
            }

            // Don't re-fire every 5 minutes while the device stays silent - only alert
            // again once the previous device_offline alarm for this rule was resolved
            // (which happens automatically, in AlarmEvaluationService, once data resumes).
            var hasOpenAlarm = await db.AlarmInstances
                .AnyAsync(a => a.RuleId == rule.RuleId && a.Status != AlarmStatus.Resolved, cancellationToken);

            if (hasOpenAlarm)
            {
                continue;
            }

            db.AlarmInstances.Add(new AlarmInstance
            {
                RuleId = rule.RuleId,
                MeasurementId = lastMeasurement.MeasurementId
            });
            createdCount++;
        }

        if (createdCount > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
