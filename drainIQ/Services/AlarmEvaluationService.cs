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
            if (!IsSatisfied(rule, measurement.WaterLevelFromTopCm))
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
