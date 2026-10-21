using System.Data;
using Microsoft.EntityFrameworkCore;
using SharedLib.Db;

namespace SharedLib.Services;

/// <summary>Reads the schedule managed in Backoffice. Missing rows preserve the JSON defaults.</summary>
public sealed record AutomationRuntimeSchedule(
    bool Enabled,
    TimeOnly? StartTime,
    TimeOnly? EndTime,
    int? IntervalMinutes,
    int? BatchSize,
    DateTime? ManualRunRequestedAt)
{
    public static async Task<AutomationRuntimeSchedule?> ReadAsync(AppDbContext db, string key, CancellationToken cancellationToken)
    {
        try
        {
            var connection = db.Database.GetDbConnection();
            var closeAfterRead = connection.State != ConnectionState.Open;
            if (closeAfterRead) await connection.OpenAsync(cancellationToken);
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT Enabled, StartTime, EndTime, IntervalMinutes, BatchSize, ManualRunRequestedAt FROM dbo.AutomationRuntimeSettings WHERE AutomationKey = @key";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@key";
                parameter.Value = key;
                command.Parameters.Add(parameter);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken)) return null;

                return new AutomationRuntimeSchedule(
                    reader.GetBoolean(0),
                    reader.IsDBNull(1) ? null : TimeOnly.FromTimeSpan(reader.GetFieldValue<TimeSpan>(1)),
                    reader.IsDBNull(2) ? null : TimeOnly.FromTimeSpan(reader.GetFieldValue<TimeSpan>(2)),
                    reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    reader.IsDBNull(5) ? null : reader.GetDateTime(5));
            }
            finally
            {
                if (closeAfterRead) await connection.CloseAsync();
            }
        }
        catch
        {
            // During deployment the table may not exist yet; the JSON schedule remains active.
            return null;
        }
    }

    public bool IsInWindow(DateTime now)
    {
        if (!StartTime.HasValue || !EndTime.HasValue) return true;
        var current = TimeOnly.FromDateTime(now);
        return StartTime <= EndTime
            ? current >= StartTime && current <= EndTime
            : current >= StartTime || current <= EndTime;
    }

    public static async Task<bool> TryConsumeManualRunAsync(AppDbContext db, string key, CancellationToken cancellationToken)
    {
        try
        {
            var connection = db.Database.GetDbConnection();
            var closeAfterRead = connection.State != ConnectionState.Open;
            if (closeAfterRead) await connection.OpenAsync(cancellationToken);
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "UPDATE dbo.AutomationRuntimeSettings SET ManualRunRequestedAt = NULL WHERE AutomationKey = @key AND ManualRunRequestedAt IS NOT NULL";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@key";
                parameter.Value = key;
                command.Parameters.Add(parameter);
                return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
            }
            finally
            {
                if (closeAfterRead) await connection.CloseAsync();
            }
        }
        catch
        {
            return false;
        }
    }
}
