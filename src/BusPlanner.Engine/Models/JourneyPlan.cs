namespace BusPlanner.Engine.Models;

/// <summary>Defines one configured ride between two stops.</summary>
/// <param name="BoardStopId">The identifier of the stop where the passenger boards.</param>
/// <param name="AlightStopId">The identifier of the stop where the passenger leaves the vehicle.</param>
/// <param name="AllowedRouteShortNames">Route labels eligible for this leg.</param>
/// <param name="DirectionId">An optional feed direction identifier used to narrow eligible trips.</param>
public sealed record JourneyLeg(
    string BoardStopId,
    string AlightStopId,
    IReadOnlySet<string> AllowedRouteShortNames,
    int? DirectionId = null);

/// <summary>Inputs for calculating upcoming trips across a sequence of journey legs.</summary>
/// <param name="Legs">The ordered journey legs, from the initial boarding stop to the destination.</param>
/// <param name="Now">The instant at which the calculation is made.</param>
/// <param name="RemainingApproachTime">Time remaining before the passenger can board the first leg.</param>
/// <param name="TransferAllowances">Minimum transfer time for each change between consecutive legs.</param>
public sealed record JourneyPlanRequest(
    IReadOnlyList<JourneyLeg> Legs,
    DateTimeOffset Now,
    TimeSpan RemainingApproachTime,
    IReadOnlyList<TimeSpan> TransferAllowances);

/// <summary>Identifies whether an estimated event time comes from the timetable or real-time data.</summary>
public enum EstimateSource
{
    /// <summary>The time is based on the static schedule.</summary>
    Scheduled,
    /// <summary>The time is based on a usable real-time prediction.</summary>
    Realtime
}

/// <summary>Describes the availability and age of the real-time feed used for a plan.</summary>
public enum FeedFreshness
{
    /// <summary>No real-time feed was supplied.</summary>
    Unavailable,
    /// <summary>The feed has a timestamp within the accepted age limit.</summary>
    Fresh,
    /// <summary>The feed timestamp is outside the accepted age limit.</summary>
    Stale,
    /// <summary>A feed was supplied without a usable feed timestamp.</summary>
    MissingTimestamp
}

/// <summary>Scheduled and estimated details for one ride in a journey.</summary>
/// <param name="LegIndex">Zero-based position of this ride in the configured journey.</param>
/// <param name="TripId">The feed's trip identifier.</param>
/// <param name="RouteId">The feed's route identifier.</param>
/// <param name="RouteShortName">The passenger-facing route label.</param>
/// <param name="HeadSign">The trip's destination or direction text, when supplied.</param>
/// <param name="BoardStopId">Identifier of the boarding stop.</param>
/// <param name="BoardStopName">Passenger-facing boarding stop name.</param>
/// <param name="AlightStopId">Identifier of the alighting stop.</param>
/// <param name="AlightStopName">Passenger-facing alighting stop name.</param>
/// <param name="ServiceDate">Local date on which the trip's service begins.</param>
/// <param name="ScheduledDeparture">Timetabled departure instant at the boarding stop.</param>
/// <param name="EstimatedDeparture">Best available departure estimate.</param>
/// <param name="DepartureSource">Source used for the departure estimate.</param>
/// <param name="ScheduledArrival">Timetabled arrival instant at the alighting stop.</param>
/// <param name="EstimatedArrival">Best available arrival estimate.</param>
/// <param name="ArrivalSource">Source used for the arrival estimate.</param>
/// <param name="TransferReadyAt">Earliest instant the passenger is ready to board the next leg, when applicable.</param>
/// <param name="Warnings">Service notices that apply to this ride.</param>
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

/// <summary>A trip option and its feasible onward options for the remaining journey.</summary>
/// <param name="Trip">The estimate for this branch's current ride.</param>
/// <param name="NextLegOptions">Upcoming ride options reachable after this ride.</param>
/// <param name="IsComplete">Whether this branch reaches the final configured leg.</param>
/// <param name="StatusMessage">An explanation when no onward service can complete the branch.</param>
public sealed record JourneyBranch(
    TripEstimate Trip,
    IReadOnlyList<JourneyBranch> NextLegOptions,
    bool IsComplete,
    string? StatusMessage);

/// <summary>The calculated journey choices and data-quality notices for one request.</summary>
/// <param name="CalculatedAt">The instant used as the basis of the calculation.</param>
/// <param name="FeedFreshness">Freshness classification of the supplied real-time feed.</param>
/// <param name="FeedTimestamp">Timestamp reported by the feed, when available.</param>
/// <param name="FirstLegOptions">The earliest candidate rides and their onward connection trees.</param>
/// <param name="Notices">Plan-wide notices, such as stale data or unavailable services.</param>
public sealed record JourneyPlanResult(
    DateTimeOffset CalculatedAt,
    FeedFreshness FeedFreshness,
    DateTimeOffset? FeedTimestamp,
    IReadOnlyList<JourneyBranch> FirstLegOptions,
    IReadOnlyList<string> Notices);
