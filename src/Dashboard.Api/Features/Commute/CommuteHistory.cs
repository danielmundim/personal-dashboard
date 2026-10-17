using System.Text.Json;
using Dashboard.Api.Infrastructure;

namespace Dashboard.Api.Features.Commute;

/// <summary>
/// Remembers past drive-time readings so "usual" means usual for us: the median for the same hour
/// on the same kind of day (weekday or weekend). Getting a typical-day curve from Google directly
/// would cost one paid request per hour of the day, so we build it up from readings we already make.
/// </summary>
public sealed class CommuteHistory(DataDirectory dataDirectory, LocalClock clock)
{
    private const int KeepDays = 60;
    private const int MinSamples = 3;
    private const int MinDays = 2; // one busy afternoon of readings shouldn't define "usual"
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<Reading>? _readings;

    private string FilePath => Path.Combine(dataDirectory.Root, "commute-history.json");

    public async Task RecordAsync(DateTimeOffset at, int minutes, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var readings = await LoadAsync(ct);
            readings.Add(new Reading(at, minutes));
            readings.RemoveAll(r => r.At < at.AddDays(-KeepDays));
            var tmp = FilePath + ".tmp";
            await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(readings, Json), ct);
            File.Move(tmp, FilePath, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Median minutes for this hour on this kind of day, or null without enough samples.</summary>
    public async Task<int?> UsualAtAsync(DateTimeOffset at, CancellationToken ct)
    {
        var bucket = await SamplesAsync(IsWeekend(at), at.Hour, ct);
        return Enough(bucket) ? Median(bucket.Select(b => b.Minutes).ToList()) : null;
    }

    /// <summary>Typical curve for today's kind of day, one point per hour that has enough samples.</summary>
    public async Task<List<TypicalTime>> TypicalDayAsync(int fromHour, int toHour, CancellationToken ct)
    {
        var today = clock.Today;
        var weekend = IsWeekend(clock.Now);
        var curve = new List<TypicalTime>();
        for (var h = fromHour; h <= toHour; h++)
        {
            var bucket = await SamplesAsync(weekend, h, ct);
            if (Enough(bucket)) curve.Add(new TypicalTime(clock.At(today, h), Median(bucket.Select(b => b.Minutes).ToList())));
        }
        return curve;
    }

    private static bool Enough(List<(DateOnly Day, int Minutes)> bucket) =>
        bucket.Count >= MinSamples && bucket.Select(b => b.Day).Distinct().Count() >= MinDays;

    private async Task<List<(DateOnly Day, int Minutes)>> SamplesAsync(bool weekend, int hour, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return (await LoadAsync(ct))
                .Select(r => (Local: TimeZoneInfo.ConvertTime(r.At, clock.TimeZone), r.Minutes))
                .Where(r => IsWeekend(r.Local) == weekend && r.Local.Hour == hour)
                .Select(r => (DateOnly.FromDateTime(r.Local.DateTime), r.Minutes))
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<Reading>> LoadAsync(CancellationToken ct)
    {
        if (_readings is not null) return _readings;
        if (!File.Exists(FilePath)) return _readings = [];
        await using var stream = File.OpenRead(FilePath);
        return _readings = await JsonSerializer.DeserializeAsync<List<Reading>>(stream, Json, ct) ?? [];
    }

    private static bool IsWeekend(DateTimeOffset at) => at.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    private static int Median(List<int> values)
    {
        var sorted = values.Order().ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (int)Math.Round((sorted[mid - 1] + sorted[mid]) / 2.0);
    }

    private sealed record Reading(DateTimeOffset At, int Minutes);
}
