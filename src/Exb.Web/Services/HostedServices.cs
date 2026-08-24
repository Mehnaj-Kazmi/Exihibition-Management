using Exb.Data;
using Exb.Data.Entities;
using Exb.Data.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Exb.Web.Services;

/// <summary>
/// Drives the tracking stack: solve, attribute, persist, broadcast.
///
/// Solving runs on the locator interval, but the live frame is pushed once a
/// second regardless. Those are different rates on purpose — the floor plan does
/// not need to redraw four times a second, and a wall display in a hall with
/// three hundred badges on it should not be asked to.
/// </summary>
public sealed class TrackingHostedService(
    TrackingRuntime runtime,
    SettingsStore settings,
    FacilityProvider facility,
    BadgeDirectory badges,
    IHubContext<LiveHub> hub,
    IDbContextFactory<ExhibitionDbContext> factory,
    ILogger<TrackingHostedService> logger) : BackgroundService
{
    private DateTime _lastBroadcast = DateTime.MinValue;
    private DateTime _lastBadgeRefresh = DateTime.MinValue;
    private IReadOnlyDictionary<int, int> _interestedToday = new Dictionary<int, int>();
    private DateTime _lastInterestRefresh = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Rebuild the floor and restart the drivers whenever an admin changes
        // halls, stands or antenna rules.
        settings.Changed += OnSettingsChanged;
        facility.Rebuilt += _ => { };

        try
        {
            await runtime.RestartAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tracking failed to start. The site will run without live tracking.");
        }

        while (!ct.IsCancellationRequested)
        {
            int interval = Math.Max(100, settings.Current.Tracking.Locator.IntervalMs);

            try
            {
                await runtime.TickAsync(ct);

                var now = DateTime.UtcNow;

                if ((now - _lastBadgeRefresh).TotalMinutes >= 2)
                {
                    _lastBadgeRefresh = now;
                    await badges.RefreshAsync(ct);
                }

                if ((now - _lastInterestRefresh).TotalSeconds >= 30)
                {
                    _lastInterestRefresh = now;
                    _interestedToday = await InterestedTodayAsync(ct);
                }

                if ((now - _lastBroadcast).TotalMilliseconds >= 1000)
                {
                    _lastBroadcast = now;
                    var frame = LiveFrameBuilder.Build(runtime, _interestedToday);
                    await hub.Clients.All.SendAsync("frame", frame, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Tracking tick failed.");
                await Task.Delay(2000, ct);
            }

            await Task.Delay(interval, ct);
        }

        settings.Changed -= OnSettingsChanged;
        await runtime.StopAsync();
    }

    private async Task<IReadOnlyDictionary<int, int>> InterestedTodayAsync(CancellationToken ct)
    {
        var day = TrackingRuntime.LocalDate(settings.Current.Exhibition);
        await using var db = await factory.CreateDbContextAsync(ct);

        return await db.Visits
            .AsNoTracking()
            .Where(v => v.EventDate == day && v.Level >= InterestLevel.Interested)
            .GroupBy(v => v.KioskId)
            .Select(g => new { g.Key, Count = g.Select(v => v.VisitorId).Distinct().Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
    }

    private void OnSettingsChanged(string key, AppSettings snapshot)
    {
        if (key is not (SettingsKeys.Tracking or SettingsKeys.Simulator)) return;

        logger.LogInformation("Settings '{Key}' changed; restarting tracking.", key);
        _ = Task.Run(async () =>
        {
            try { await runtime.RestartAsync(); }
            catch (Exception ex) { logger.LogError(ex, "Restart after a settings change failed."); }
        });
    }
}

/// <summary>Sends whatever is waiting in the Outbox, on the schedule the mail settings allow.</summary>
public sealed class MailDispatchHostedService(
    MailQueue queue,
    SettingsStore settings,
    IMailTransportSelector transports,
    ILogger<MailDispatchHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Nothing is queued in the first moments of startup; do not race the migration.
        await Task.Delay(TimeSpan.FromSeconds(20), ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var mailSettings = settings.Current.Mail;
                var transport = transports.Resolve(mailSettings);
                var (sent, failed, held) = await queue.DispatchAsync(transport, mailSettings, 25, ct);

                if (sent > 0 || failed > 0)
                    logger.LogInformation("Outbox: {Sent} sent, {Failed} failed.", sent, failed);
                else if (held > 0)
                    logger.LogDebug("Outbox: {Held} message(s) held; mail provider is '{Provider}'.", held, mailSettings.Provider);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox dispatch failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }
    }
}

/// <summary>
/// Runs the evening pipeline once, at the configured local time.
///
/// It checks the clock rather than sleeping until a computed instant, so that a
/// machine suspended over lunch, a time zone change or a settings edit does not
/// leave the run stranded. Whether a day has been processed is recorded on the
/// EventDay row, so a restart at 19:30 does not re-send everything.
/// </summary>
public sealed class EndOfDayHostedService(
    EndOfDayService endOfDay,
    SettingsStore settings,
    IDbContextFactory<ExhibitionDbContext> factory,
    ILogger<EndOfDayHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var exhibition = settings.Current.Exhibition;
                if (exhibition.AutoRunEndOfDay)
                {
                    var localNow = TrackingRuntime.LocalNow(exhibition);
                    var today = DateOnly.FromDateTime(localNow);

                    if (TimeOnly.FromDateTime(localNow) >= exhibition.EndOfDayAt && !await IsClosedAsync(today, ct))
                    {
                        logger.LogInformation("End-of-day time reached for {Day}; running.", today);
                        var result = await endOfDay.RunAsync(today, ct: ct);

                        foreach (string problem in result.Problems)
                            logger.LogWarning("End of day: {Problem}", problem);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "End-of-day run failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(2), ct);
        }
    }

    private async Task<bool> IsClosedAsync(DateOnly day, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.EventDays.AnyAsync(d => d.Date == day && d.Closed, ct);
    }
}
