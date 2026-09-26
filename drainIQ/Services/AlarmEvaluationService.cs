using drainIQ.Data;
using drainIQ.Models;
using Microsoft.EntityFrameworkCore;

namespace drainIQ.Services;

/// <summary>
/// Checks a newly-saved measurement against the active alarm rules for its
/// device and records any alarm_instances that fire as a result.
/// </summary>
public class AlarmEvaluationService(ApplicationDbContext db)
{
    public async Task<List<AlarmInstance>> EvaluateAsync(Measurement measurement)
    {
        var activeRules = await db.AlarmRules
            .Where(r => r.DeviceId == measurement.DeviceId && r.IsActive)
            .ToListAsync();

        var triggered = new List<AlarmInstance>();

        foreach (var rule in activeRules)
        {
            var value = GetMetricValue(rule, measurement);

            // e.g. a low_battery rule on a measurement that didn't report battery, or a
            // device_offline rule (which GetMetricValue never resolves here - it's only
            // ever evaluated periodically by DeviceOfflineCheckService, since "no data
            // arrived" can't be caught by a handler that only runs when data DOES arrive).
            if (value is null || !AlarmComparators.IsSatisfied(rule.Comparator, value.Value, rule.ThresholdValue))
            {
                continue;
            }

            var instance = new AlarmInstance
            {
                RuleId = rule.RuleId,
                MeasurementId = measurement.MeasurementId
            };

            db.AlarmInstances.Add(instance);
            triggered.Add(instance);
        }

        // A measurement just arrived, so the device clearly isn't offline anymore -
        // auto-resolve any standing device_offline alarms for it rather than leaving
        // them open until someone notices and resolves them by hand.
        var recoveredOfflineAlarms = await db.AlarmInstances
            .Where(a => a.Rule.DeviceId == measurement.DeviceId
                && a.Rule.AlarmType == AlarmTypes.DeviceOffline
                && a.Status != AlarmStatus.Resolved)
            .ToListAsync();

        foreach (var alarm in recoveredOfflineAlarms)
        {
            alarm.Status = AlarmStatus.Resolved;
            alarm.ResolvedAt = DateTimeOffset.UtcNow;
        }

        if (triggered.Count > 0 || recoveredOfflineAlarms.Count > 0)
        {
            await db.SaveChangesAsync();
        }

        return triggered;
    }

    private static decimal? GetMetricValue(AlarmRule rule, Measurement measurement) => rule.AlarmType switch
    {
        AlarmTypes.WaterLevel => measurement.WaterLevelFromTopCm,
        AlarmTypes.LowBattery => measurement.BatteryLevelPct,
        _ => null
    };
}
