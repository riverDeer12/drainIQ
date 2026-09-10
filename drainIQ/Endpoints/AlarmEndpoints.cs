using drainIQ.Data;
using drainIQ.Models;
using Microsoft.EntityFrameworkCore;

namespace drainIQ.Endpoints;

public static class AlarmEndpoints
{
    public static void MapAlarmEndpoints(this WebApplication app)
    {
        var rules = app.MapGroup("/api/alarm-rules").WithTags("AlarmRules").RequireAuthorization();

        rules.MapGet("/", async (int? deviceId, ApplicationDbContext db) =>
        {
            var query = db.AlarmRules.Where(r => r.IsActive);
            if (deviceId is not null)
            {
                query = query.Where(r => r.DeviceId == deviceId);
            }

            return Results.Ok(await query.ToListAsync());
        });

        rules.MapPost("/", async (CreateAlarmRuleRequest request, ApplicationDbContext db) =>
        {
            if (!AlarmComparators.Allowed.Contains(request.Comparator))
            {
                return Results.BadRequest($"Comparator must be one of: {string.Join(", ", AlarmComparators.Allowed)}");
            }

            var deviceExists = await db.Devices.AnyAsync(d => d.DeviceId == request.DeviceId && d.IsActive);
            if (!deviceExists)
            {
                return Results.NotFound($"Active device {request.DeviceId} not found.");
            }

            var rule = new AlarmRule
            {
                DeviceId = request.DeviceId,
                AlarmName = request.AlarmName,
                AlarmType = request.AlarmType,
                ThresholdValue = request.ThresholdValue,
                Comparator = request.Comparator
            };

            db.AlarmRules.Add(rule);
            await db.SaveChangesAsync();

            return Results.Created($"/api/alarm-rules/{rule.RuleId}", rule);
        });

        var instances = app.MapGroup("/api/alarms").WithTags("Alarms").RequireAuthorization();

        instances.MapGet("/", async (int? deviceId, string? status, ApplicationDbContext db) =>
        {
            var query = db.AlarmInstances.Include(a => a.Rule).AsQueryable();

            if (deviceId is not null)
            {
                query = query.Where(a => a.Rule.DeviceId == deviceId);
            }

            if (status is not null)
            {
                query = query.Where(a => a.Status == status);
            }

            return Results.Ok(await query.OrderByDescending(a => a.TriggeredAt).ToListAsync());
        });

        instances.MapPost("/{id:long}/acknowledge", async (long id, ApplicationDbContext db) =>
        {
            var alarm = await db.AlarmInstances.FindAsync(id);
            if (alarm is null)
            {
                return Results.NotFound();
            }

            alarm.Status = AlarmStatus.Acknowledged;
            alarm.AcknowledgedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            return Results.Ok(alarm);
        });

        instances.MapPost("/{id:long}/resolve", async (long id, ApplicationDbContext db) =>
        {
            var alarm = await db.AlarmInstances.FindAsync(id);
            if (alarm is null)
            {
                return Results.NotFound();
            }

            alarm.Status = AlarmStatus.Resolved;
            alarm.ResolvedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            return Results.Ok(alarm);
        });
    }
}

public record CreateAlarmRuleRequest(int DeviceId, string AlarmName, string AlarmType, decimal ThresholdValue, string Comparator);
