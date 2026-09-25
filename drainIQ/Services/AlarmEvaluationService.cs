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

            // e.g. a low_battery rule on a measurement that didn't report battery -
            // nothing to compare against, so this rule just doesn't fire this time.
            if (value is null || !IsSatisfied(rule, value.Value))
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

        if (triggered.Count > 0)
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

    private static bool IsSatisfied(AlarmRule rule, decimal value) => rule.Comparator switch
    {
        "<" => value < rule.ThresholdValue,
        "<=" => value <= rule.ThresholdValue,
        ">" => value > rule.ThresholdValue,
        ">=" => value >= rule.ThresholdValue,
        "=" => value == rule.ThresholdValue,
        _ => false
    };
}
