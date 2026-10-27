namespace BusPlanner.Engine.Models;

public sealed record JourneyLeg(
    string BoardStopId,
    string AlightStopId,
    IReadOnlySet<string> AllowedRouteShortNames,
    int? DirectionId = null);

public sealed record JourneyPlanRequest(
    IReadOnlyList<JourneyLeg> Legs,
    DateTimeOffset Now,
    TimeSpan RemainingApproachTime,
    IReadOnlyList<TimeSpan> TransferAllowances);

public enum EstimateSource
{
    Scheduled,
    Realtime
}

public enum FeedFreshness
{
    Unavailable,
    Fresh,
    Stale,
    MissingTimestamp
}

public sealed record TripEstimate(
    int LegIndex,
    string TripId,
    string RouteId,
    string RouteShortName,
    string? HeadSign,
    string BoardStopId,
    string BoardStopName,
    string AlightStopId,
    string AlightStopName,
    DateOnly ServiceDate,
    DateTimeOffset ScheduledDeparture,
    DateTimeOffset EstimatedDeparture,
    EstimateSource DepartureSource,
    DateTimeOffset ScheduledArrival,
    DateTimeOffset EstimatedArrival,
    EstimateSource ArrivalSource,
    DateTimeOffset? TransferReadyAt,
    IReadOnlyList<string> Warnings);

public sealed record JourneyBranch(
    TripEstimate Trip,
    IReadOnlyList<JourneyBranch> NextLegOptions,
    bool IsComplete,
    string? StatusMessage);

public sealed record JourneyPlanResult(
    DateTimeOffset CalculatedAt,
    FeedFreshness FeedFreshness,
    DateTimeOffset? FeedTimestamp,
    IReadOnlyList<JourneyBranch> FirstLegOptions,
    IReadOnlyList<string> Notices);

