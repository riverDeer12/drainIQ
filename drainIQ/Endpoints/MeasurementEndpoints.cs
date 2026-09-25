using drainIQ.Data;
using drainIQ.Models;
using drainIQ.Services;
using Microsoft.EntityFrameworkCore;

namespace drainIQ.Endpoints;

public static class MeasurementEndpoints
{
    public static void MapMeasurementEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/measurements").WithTags("Measurements");

        // Deliberately NOT behind .RequireAuthorization(): this is a device-to-server
        // ingestion endpoint, and field devices don't hold a user JWT. Needs its own
        // auth (e.g. a per-device API key) before going to production.
        // Core ingestion endpoint: a device posts a reading, we store it,
        // then immediately check it against that device's active alarm rules.
        group.MapPost("/", async (CreateMeasurementRequest request, ApplicationDbContext db, AlarmEvaluationService alarmEvaluation) =>
        {
            if (request.BatteryLevelPct is < 0 or > 100)
            {
                return Results.BadRequest("batteryLevelPct must be between 0 and 100.");
            }

            var deviceExists = await db.Devices.AnyAsync(d => d.DeviceId == request.DeviceId && d.IsActive);
            if (!deviceExists)
            {
                return Results.NotFound($"Active device {request.DeviceId} not found.");
            }

            var measurement = new Measurement
            {
                DeviceId = request.DeviceId,
                SentAt = request.SentAt ?? DateTimeOffset.UtcNow,
                WaterLevelFromTopCm = request.WaterLevelFromTopCm,
                BatteryLevelPct = request.BatteryLevelPct
            };

            db.Measurements.Add(measurement);
            await db.SaveChangesAsync();

            var triggeredAlarms = await alarmEvaluation.EvaluateAsync(measurement);

            return Results.Created($"/measurements/{measurement.MeasurementId}", new
            {
                measurement,
                triggeredAlarms
            });
        });
    }
}

public record CreateMeasurementRequest(int DeviceId, decimal WaterLevelFromTopCm, DateTimeOffset? SentAt, int? BatteryLevelPct = null);
