using System.Diagnostics;
using BusPlanner.Api.Data;
using BusPlanner.Api.Options;
using BusPlanner.Engine;
using BusPlanner.Engine.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Options;

namespace BusPlanner.Api.Functions;

public sealed record JourneyResponse(
    string JourneyLabel,
    string StartingStage,
    string StartingStopName,
    int ApproachMinutes,
    int TransferMinutes,
    int DefaultOriginApproachMinutes,
    int DefaultTransferMinutes,
    bool AlertsAvailable,
    bool HasTransfers,
    IReadOnlyList<JourneyStartingStage> AvailableStartingStages,
    JourneyTimingMetrics Timing,
    JourneyPlanResult Plan);

public sealed record JourneyStartingStage(string Key, string Label);

public sealed record JourneyTimingMetrics(
    double ScheduleWaitMs,
    double ScheduleCacheLoadMs,
    double ScheduleCacheDownloadMs,
    double ScheduleCacheParseMs,
    double RealtimeLoadMs,
    double PlanningMs,
    double TotalServerMs);

public sealed class JourneyFunction(
    TransitDataService transitData,
    IOptions<BusPlannerOptions> options)
{
    private readonly BusPlannerOptions _options = options.Value;

    [Function("Journey")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "journey")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        var requestTimer = Stopwatch.StartNew();
        var profileKey = request.Query["journey"].FirstOrDefault();
        var profile = ResolveProfile(profileKey);
        if (profile is null)
        {
            return new BadRequestObjectResult(new { error = "The requested journey profile is not configured." });
        }

        var transferStopCodes = profile.Branches
            .SelectMany(branch => branch.Legs.Skip(1).Select(leg => leg.BoardStopCode))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var stage = request.Query["stage"].FirstOrDefault() ?? "origin";
        var transferStageIndex = string.Equals(stage, "interchange", StringComparison.OrdinalIgnoreCase)
            ? 0
            : stage.StartsWith("transfer-", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(stage.AsSpan("transfer-".Length), out var parsedIndex)
                    ? parsedIndex
                    : -1;
        var isTransferStart = transferStageIndex >= 0;
        if (!string.Equals(stage, "origin", StringComparison.OrdinalIgnoreCase)
            && !isTransferStart)
        {
            return new BadRequestObjectResult(new { error = "stage must be 'origin' or a configured transfer stage." });
        }

        if (isTransferStart && transferStageIndex >= transferStopCodes.Length)
        {
            return new BadRequestObjectResult(new { error = "The requested transfer starting point is not configured." });
        }

        var defaultApproach = isTransferStart ? 0 : profile.DefaultApproachMinutes;
        if (!TryReadMinutes(request, "approachMinutes", defaultApproach, 0, 120, out var approachMinutes))
        {
            return new BadRequestObjectResult(new { error = "approachMinutes must be a whole number from 0 to 120." });
        }

        if (!TryReadMinutes(request, "transferMinutes", profile.DefaultTransferMinutes, 0, 120, out var transferMinutes))
        {
            return new BadRequestObjectResult(new { error = "transferMinutes must be a whole number from 0 to 120." });
        }

        try
        {
            var stepTimer = Stopwatch.StartNew();
            var scheduleResult = await transitData.GetScheduleAsync(cancellationToken);
            var schedule = scheduleResult.Schedule;
            var scheduleWaitMs = stepTimer.Elapsed.TotalMilliseconds;
            var destinationStopId = schedule.ResolveStopId(profile.DestinationStopCode);
            var startStopCode = isTransferStart ? transferStopCodes[transferStageIndex] : profile.OriginStopCode;
            var startStopId = schedule.ResolveStopId(startStopCode);
            var branchLegs = profile.Branches.Select(branch => branch.Legs.Select(leg => new JourneyLeg(
                    schedule.ResolveStopId(leg.BoardStopCode),
                    schedule.ResolveStopId(leg.AlightStopCode),
                    leg.AllowedRouteShortNames.ToHashSet(StringComparer.OrdinalIgnoreCase))).ToArray()).ToArray();
            var paths = !isTransferStart
                ? branchLegs
                : profile.Branches.Select((branch, branchIndex) => (branch, branchIndex))
                    .SelectMany(item => item.branch.Legs.Select((leg, legIndex) => (leg, legIndex, item.branchIndex))
                        .Where(item => item.legIndex > 0 && string.Equals(item.leg.BoardStopCode, startStopCode, StringComparison.OrdinalIgnoreCase))
                        .Select(item => branchLegs[item.branchIndex].Skip(item.legIndex).ToArray()))
                    .ToArray();

            if (paths.Length == 0 || paths.Any(path => path.Length == 0))
            {
                return new BadRequestObjectResult(new { error = "The selected journey profile has no configured route legs." });
            }

            stepTimer.Restart();
            var realtime = await transitData.GetRealtimeAsync(cancellationToken);
            var realtimeLoadMs = stepTimer.Elapsed.TotalMilliseconds;
            var prefixes = _options.RealtimeRouteIdPrefixes.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<string>)pair.Value,
                StringComparer.OrdinalIgnoreCase);
            var planner = new JourneyPlanner(
                schedule,
                TimeSpan.FromSeconds(Math.Max(1, _options.MaximumRealtimeAgeSeconds)),
                prefixes);
            var requestTime = DateTimeOffset.UtcNow;
            stepTimer.Restart();
            var plans = paths.Select(path => planner.Plan(new JourneyPlanRequest(
                path,
                requestTime,
                TimeSpan.FromMinutes(approachMinutes),
                isTransferStart ? Enumerable.Repeat(TimeSpan.FromMinutes(profile.DefaultTransferMinutes), path.Length - 1).ToArray()
                    : Enumerable.Repeat(TimeSpan.FromMinutes(transferMinutes), path.Length - 1).ToArray()),
                realtime.Snapshot)).ToArray();
            var options = plans.SelectMany(plan => plan.FirstLegOptions)
                .OrderBy(option => option.Trip.EstimatedDeparture)
                .ThenBy(option => option.Trip.TripId, StringComparer.Ordinal)
                .Take(2).ToArray();
            var planResult = plans[0] with
            {
                FirstLegOptions = options,
                Notices = plans.SelectMany(plan => plan.Notices)
                    .Where(notice => !notice.StartsWith("Only one eligible first-leg", StringComparison.Ordinal))
                    .Distinct(StringComparer.Ordinal).ToArray()
            };
            var planningMs = stepTimer.Elapsed.TotalMilliseconds;

            return new OkObjectResult(new JourneyResponse(
                profile.Label,
                isTransferStart ? $"transfer-{transferStageIndex}" : "origin",
                schedule.Stops[startStopId].Name,
                approachMinutes,
                transferMinutes,
                profile.DefaultApproachMinutes,
                profile.DefaultTransferMinutes,
                realtime.AlertsAvailable,
                profile.Branches.Any(branch => branch.Legs.Count > 1),
                transferStopCodes.Select((stopCode, index) => new JourneyStartingStage(
                    $"transfer-{index}", schedule.Stops[schedule.ResolveStopId(stopCode)].Name)).ToArray(),
                new JourneyTimingMetrics(
                    scheduleWaitMs,
                    scheduleResult.TotalMs,
                    scheduleResult.DownloadMs,
                    scheduleResult.ParseMs,
                    realtimeLoadMs,
                    planningMs,
                    requestTimer.Elapsed.TotalMilliseconds),
                planResult));
        }
        catch (KeyNotFoundException ex)
        {
            return new ObjectResult(new { error = $"Journey configuration references a stop not found in the schedule: {ex.Message}" })
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or IOException)
        {
            return new ObjectResult(new { error = "Transit schedule data is temporarily unavailable. Please retry shortly." })
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable
            };
        }
    }

    private JourneyProfileOptions? ResolveProfile(string? requestedKey)
    {
        if (_options.Journeys.Count == 0) return null;
        if (string.IsNullOrWhiteSpace(requestedKey)) return _options.Journeys.Values.First();
        if (_options.Journeys.TryGetValue(requestedKey, out var exact)) return exact;

        static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).ToArray());
        var normalized = Normalize(requestedKey);
        var quickLink = _options.Journeys.FirstOrDefault(pair =>
            !string.IsNullOrWhiteSpace(pair.Value.QuickLink)
            && Normalize(pair.Value.QuickLink).Equals(normalized, StringComparison.OrdinalIgnoreCase));
        if (quickLink.Value is not null) return quickLink.Value;

        return _options.Journeys.FirstOrDefault(pair =>
            Normalize(pair.Key).Equals(normalized, StringComparison.OrdinalIgnoreCase)
            || Normalize(pair.Key).Replace("Journey", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Equals(normalized, StringComparison.OrdinalIgnoreCase)
            || Normalize(pair.Value.Label).Equals(normalized, StringComparison.OrdinalIgnoreCase)).Value;
    }

    private static bool TryReadMinutes(HttpRequest request, string key, int fallback, int minimum, int maximum, out int value)
    {
        var text = request.Query[key].FirstOrDefault();
        if (text is null)
        {
            value = fallback;
            return value >= minimum && value <= maximum;
        }

        return int.TryParse(text, out value) && value >= minimum && value <= maximum;
    }
}
