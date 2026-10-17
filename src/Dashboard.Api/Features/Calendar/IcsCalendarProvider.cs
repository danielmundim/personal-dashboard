using Dashboard.Api.Infrastructure;
using Ical.Net.DataTypes;
using Microsoft.Extensions.Options;
using IcalCalendar = Ical.Net.Calendar;
using IcalEvent = Ical.Net.CalendarComponents.CalendarEvent;

namespace Dashboard.Api.Features.Calendar;

/// <summary>
/// Reads each person's iCal links (Google "secret address", Outlook "publish calendar", …), expands
/// recurring events with Ical.Net and merges every feed into one timeline. A feed that fails is
/// reported in <see cref="CalendarAgenda.FailedSources"/> instead of hiding the others.
/// </summary>
public sealed class IcsCalendarProvider(
    HttpClient http,
    LocalClock clock,
    IOptions<DashboardOptions> options,
    ILogger<IcsCalendarProvider> logger) : ICalendarProvider
{
    public async Task<CalendarAgenda> GetAgendaAsync(string personId, int days, CancellationToken cancellationToken)
    {
        var feeds = (options.Value.Calendars.GetValueOrDefault(personId) ?? [])
            .Where(f => !string.IsNullOrWhiteSpace(f.Url))
            .ToList();
        if (feeds.Count == 0)
            return new CalendarAgenda(personId, [], clock.Now, Configured: false);

        var from = clock.At(clock.Today, 0);
        var to = clock.At(clock.Today.AddDays(days), 0);

        var results = await Task.WhenAll(feeds.Select(f => ReadFeedAsync(f, from, to, cancellationToken)));
        var failed = feeds.Zip(results).Where(x => x.Second is null).Select(x => x.First.Name).ToList();

        // The same meeting can arrive from two calendars (invited on both accounts): keep one copy.
        var events = results
            .Where(r => r is not null)
            .SelectMany(r => r!)
            .GroupBy(e => (e.Title.Trim().ToLowerInvariant(), e.Start, e.AllDay))
            .Select(g => g.OrderBy(e => e.Busy).First()) // prefer the copy with details
            .OrderBy(e => e.Start)
            .ToList();

        return new CalendarAgenda(personId, events, clock.Now, Configured: true, FailedSources: failed);
    }

    private async Task<List<CalendarEvent>?> ReadFeedAsync(CalendarFeedOptions feed, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        try
        {
            var text = await http.GetStringAsync(ToHttps(feed.Url), ct);
            var calendar = IcalCalendar.Load(text);
            var list = new List<CalendarEvent>();

            foreach (var occurrence in calendar.GetOccurrences(new CalDateTime(from.UtcDateTime, "UTC"), new CalDateTime(to.UtcDateTime, "UTC")))
            {
                if (occurrence.Source is not IcalEvent ev) continue;
                if (string.Equals(ev.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase)) continue;

                var allDay = ev.IsAllDay;
                var start = ToLocal(occurrence.Period.StartTime, allDay);
                var end = occurrence.Period.EndTime is { } e
                    ? ToLocal(e, allDay)
                    : start + (occurrence.Period.Duration > TimeSpan.Zero ? occurrence.Period.Duration : TimeSpan.FromHours(allDay ? 24 : 1));
                if (end <= from || start >= to) continue;

                list.Add(new CalendarEvent(
                    feed.BusyOnly ? "Ocupado" : (string.IsNullOrWhiteSpace(ev.Summary) ? "(sem título)" : ev.Summary.Trim()),
                    start,
                    end,
                    allDay,
                    feed.BusyOnly || string.IsNullOrWhiteSpace(ev.Location) ? null : ev.Location.Trim(),
                    feed.Name,
                    feed.BusyOnly));
            }

            return list;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or FormatException or ArgumentException or InvalidOperationException)
        {
            // Never log the URL: for Google it is the secret that grants read access.
            logger.LogWarning("Calendar feed '{Name}' could not be read: {Error}", feed.Name, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// iPhone/iCloud "Share Link" gives webcal:// addresses; webcal is just iCal over HTTPS, so fetch it as such.
    /// </summary>
    internal static string ToHttps(string url) =>
        url.Trim() is var u && u.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase) ? "https://" + u["webcal://".Length..] : u.Trim();

    /// <summary>
    /// All-day dates are floating (no zone): pin them to local midnight. Timed events carry their own zone
    /// (or UTC), which Ical.Net converts to UTC; then show them in the household zone.
    /// </summary>
    private DateTimeOffset ToLocal(IDateTime value, bool allDay) =>
        allDay
            ? clock.At(DateOnly.FromDateTime(value.Value), 0)
            : TimeZoneInfo.ConvertTime(new DateTimeOffset(DateTime.SpecifyKind(value.AsUtc, DateTimeKind.Utc)), clock.TimeZone);
}
