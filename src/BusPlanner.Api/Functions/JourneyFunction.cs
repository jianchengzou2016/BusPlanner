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
    JourneyPlanResult Plan);

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
        var journey = _options.Journey;
        var stage = request.Query["stage"].FirstOrDefault() ?? "origin";
        if (!string.Equals(stage, "origin", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(stage, "interchange", StringComparison.OrdinalIgnoreCase))
        {
            return new BadRequestObjectResult(new { error = "stage must be 'origin' or 'interchange'." });
        }

        var isInterchangeStart = string.Equals(stage, "interchange", StringComparison.OrdinalIgnoreCase);
        var defaultApproach = isInterchangeStart ? 0 : journey.DefaultApproachMinutes;
        if (!TryReadMinutes(request, "approachMinutes", defaultApproach, 0, 120, out var approachMinutes))
        {
            return new BadRequestObjectResult(new { error = "approachMinutes must be a whole number from 0 to 120." });
        }

        if (!TryReadMinutes(request, "transferMinutes", journey.DefaultTransferMinutes, 0, 120, out var transferMinutes))
        {
            return new BadRequestObjectResult(new { error = "transferMinutes must be a whole number from 0 to 120." });
        }

        try
        {
            var schedule = await transitData.GetScheduleAsync(cancellationToken);
            var originStopId = schedule.ResolveStopId(journey.OriginStopCode);
            var interchangeArrivalStopId = schedule.ResolveStopId(journey.InterchangeArrivalStopCode);
            var interchangeBoardingStopId = schedule.ResolveStopId(journey.InterchangeBoardingStopCode);
            var destinationStopId = schedule.ResolveStopId(journey.DestinationStopCode);

            var legs = isInterchangeStart
                ? new[]
                {
                    new JourneyLeg(interchangeBoardingStopId, destinationStopId,
                        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { journey.OnwardRouteShortName })
                }
                : new[]
                {
                    new JourneyLeg(originStopId, interchangeArrivalStopId,
                        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { journey.FirstRouteShortName }),
                    new JourneyLeg(interchangeBoardingStopId, destinationStopId,
                        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { journey.OnwardRouteShortName })
                };

            var realtime = await transitData.GetRealtimeAsync(cancellationToken);
            var prefixes = journey.RealtimeRouteIdPrefixes.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<string>)pair.Value,
                StringComparer.OrdinalIgnoreCase);
            var planner = new JourneyPlanner(
                schedule,
                TimeSpan.FromSeconds(Math.Max(1, _options.MaximumRealtimeAgeSeconds)),
                prefixes);
            var requestTime = DateTimeOffset.UtcNow;
            var plan = planner.Plan(new JourneyPlanRequest(
                legs,
                requestTime,
                TimeSpan.FromMinutes(approachMinutes),
                isInterchangeStart ? Array.Empty<TimeSpan>() : new[] { TimeSpan.FromMinutes(transferMinutes) }),
                realtime.Snapshot);

            return new OkObjectResult(new JourneyResponse(
                journey.Label,
                isInterchangeStart ? "interchange" : "origin",
                schedule.Stops[legs[0].BoardStopId].Name,
                approachMinutes,
                transferMinutes,
                journey.DefaultApproachMinutes,
                journey.DefaultTransferMinutes,
                realtime.AlertsAvailable,
                plan));
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

    private static bool TryReadMinutes(
        HttpRequest request,
        string key,
        int fallback,
        int minimum,
        int maximum,
        out int value)
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
