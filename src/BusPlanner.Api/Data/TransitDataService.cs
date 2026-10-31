using System.IO.Compression;
using System.Diagnostics;
using BusPlanner.Api.Options;
using BusPlanner.Engine.Data;
using BusPlanner.Engine.Models;
using BusPlanner.Engine.Realtime;
using Microsoft.Extensions.Options;

namespace BusPlanner.Api.Data;

public sealed record RealtimeDataResult(RealtimeSnapshot? Snapshot, bool AlertsAvailable);
public sealed record ScheduleLoadResult(GtfsSchedule Schedule, double DownloadMs, double ParseMs)
{
    public double TotalMs => DownloadMs + ParseMs;
}

/// <summary>Loads the public schedule once per function instance and caches realtime data on demand.</summary>
public sealed class TransitDataService(
    IHttpClientFactory httpClientFactory,
    IOptions<BusPlannerOptions> options)
{
    private readonly BusPlannerOptions _options = options.Value;
    private readonly GtfsScheduleLoader _scheduleLoader = new();
    private readonly GtfsRealtimeParser _realtimeParser = new();
    private readonly SemaphoreSlim _realtimeRefresh = new(1, 1);
    private readonly object _scheduleLock = new();
    private Task<ScheduleLoadResult>? _scheduleTask;
    private RealtimeDataResult? _cachedRealtime;
    private DateTimeOffset _realtimeFetchedAt;

    public async Task<ScheduleLoadResult> GetScheduleAsync(CancellationToken cancellationToken)
    {
        Task<ScheduleLoadResult> task;
        lock (_scheduleLock)
        {
            _scheduleTask ??= LoadScheduleAsync();
            task = _scheduleTask;
        }

        try
        {
            return await task.WaitAsync(cancellationToken);
        }
        catch when (task.IsFaulted)
        {
            lock (_scheduleLock)
            {
                if (ReferenceEquals(_scheduleTask, task))
                {
                    _scheduleTask = null;
                }
            }

            throw;
        }
    }

    public async Task<RealtimeDataResult> GetRealtimeAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (_cachedRealtime is not null
            && now - _realtimeFetchedAt < TimeSpan.FromSeconds(Math.Max(1, _options.RealtimeCacheSeconds)))
        {
            return _cachedRealtime;
        }

        await _realtimeRefresh.WaitAsync(cancellationToken);
        try
        {
            now = DateTimeOffset.UtcNow;
            if (_cachedRealtime is not null
                && now - _realtimeFetchedAt < TimeSpan.FromSeconds(Math.Max(1, _options.RealtimeCacheSeconds)))
            {
                return _cachedRealtime;
            }

            try
            {
                var client = httpClientFactory.CreateClient(nameof(TransitDataService));
                var tripUpdatesTask = DownloadRealtimeFeedAsync(client, _options.TripUpdatesUrl, cancellationToken);
                var alertsTask = DownloadAlertsAsync(client, cancellationToken);
                await Task.WhenAll(tripUpdatesTask, alertsTask);

                var tripUpdates = tripUpdatesTask.Result;
                var alerts = alertsTask.Result;
                var snapshot = new RealtimeSnapshot(
                    tripUpdates.FeedTimestamp,
                    tripUpdates.TripUpdates,
                    alerts.Alerts);
                _cachedRealtime = new RealtimeDataResult(snapshot, alerts.Available);
                _realtimeFetchedAt = DateTimeOffset.UtcNow;
                return _cachedRealtime;
            }
            catch (Exception) when (_cachedRealtime is not null)
            {
                // Keep the last snapshot so the planner can mark its source timestamp stale
                // and fall back to the timetable instead of losing all context on an outage.
                return _cachedRealtime;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A first-request realtime outage must not prevent a timetable-only plan.
                _cachedRealtime = new RealtimeDataResult(null, AlertsAvailable: false);
                _realtimeFetchedAt = DateTimeOffset.UtcNow;
                return _cachedRealtime;
            }
        }
        finally
        {
            _realtimeRefresh.Release();
        }
    }

    private async Task<ScheduleLoadResult> LoadScheduleAsync()
    {
        var client = httpClientFactory.CreateClient(nameof(TransitDataService));
        var timer = Stopwatch.StartNew();
        await using var source = await client.GetStreamAsync(_options.StaticScheduleUrl);
        await using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer);
        var downloadMs = timer.Elapsed.TotalMilliseconds;
        buffer.Position = 0;
        timer.Restart();
        var routeShortNames = _options.Journeys.Values
            .SelectMany(profile => profile.Branches.SelectMany(branch => branch.Legs)
                .SelectMany(leg => leg.AllowedRouteShortNames))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        routeShortNames.RemoveWhere(string.IsNullOrWhiteSpace);
        var schedule = _scheduleLoader.Load(buffer, routeShortNames);
        return new ScheduleLoadResult(schedule, downloadMs, timer.Elapsed.TotalMilliseconds);
    }

    private async Task<RealtimeSnapshot> DownloadRealtimeFeedAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        await using var stream = await client.GetStreamAsync(url, cancellationToken);
        return _realtimeParser.Parse(stream);
    }

    private async Task<(IReadOnlyList<RealtimeServiceAlert> Alerts, bool Available)> DownloadAlertsAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await DownloadRealtimeFeedAsync(client, _options.AlertsUrl, cancellationToken);
            return (snapshot.Alerts, true);
        }
        catch (Exception)
        {
            return (Array.Empty<RealtimeServiceAlert>(), false);
        }
    }
}
