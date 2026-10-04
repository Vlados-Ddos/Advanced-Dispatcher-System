using System;
using System.Linq;

namespace AdvancedDispatcherSystem.Core;

/// <summary>
/// Creates an explicit stale copy when a game-side job capture is invalidated.
/// The copy is required because the previous DTO may still be queued for IPC
/// serialization on another thread.
/// </summary>
public static class JobSnapshotQuality
{
    // A non-suspended job that previously referenced real cars is no longer
    // authoritative when a sweep cannot resolve any of those cars. Omitting it
    // from a replaceJobs snapshot removes a ghost order; jobs with no cars yet
    // (available/generated) and explicit Persistent Jobs suspension remain.
    public static bool KeepMissingRuntime(JobState previous, string[] resolvedCars, bool explicitlySuspended)
    {
        if (explicitlySuspended) return true;
        if (resolvedCars != null && resolvedCars.Length > 0) return true;
        return previous == null || previous.cars == null || previous.cars.Length == 0;
    }
    public static JobState[] MarkStale(JobState[] jobs)
    {
        if (jobs == null || jobs.Length == 0) return jobs ?? Array.Empty<JobState>();
        return jobs.Select(CloneStale).ToArray();
    }

    private static JobState CloneStale(JobState source)
    {
        if (source == null) return null;
        return new JobState
        {
            assignedPlayerId = source.assignedPlayerId,
            assignedPlayerName = source.assignedPlayerName,
            assignedPlayerKey = source.assignedPlayerKey,
            id = source.id,
            origin = source.origin,
            destination = source.destination,
            type = source.type,
            state = source.state,
            owner = source.owner,
            ownerKey = source.ownerKey,
            ownerStatus = source.ownerStatus,
            startedGameDate = source.startedGameDate,
            typeColor = source.typeColor,
            typeSource = source.typeSource,
            integrationStatus = source.integrationStatus,
            typeName = source.typeName,
            typeLanguage = source.typeLanguage,
            dataQuality = "stale",
            active = source.active,
            elapsedKnown = false,
            massKnown = source.massKnown,
            length = source.length,
            mass = source.mass,
            payment = source.payment,
            bonus = source.bonus,
            elapsedSeconds = source.elapsedSeconds,
            bonusLimitSeconds = source.bonusLimitSeconds,
            sampledGameTime = source.sampledGameTime,
            sampledAt = source.sampledAt,
            tasksDone = source.tasksDone,
            tasksTotal = source.tasksTotal,
            cars = (source.cars ?? Array.Empty<string>()).ToArray(),
            cargo = (source.cargo ?? Array.Empty<string>()).ToArray(),
            licenses = (source.licenses ?? Array.Empty<string>()).ToArray(),
            legs = (source.legs ?? Array.Empty<JobLeg>()).Select(CloneLeg).ToArray(),
        };
    }

    private static JobLeg CloneLeg(JobLeg source)
    {
        if (source == null) return null;
        return new JobLeg
        {
            id = source.id,
            progress = source.progress,
            operation = source.operation,
            passengerStop = source.passengerStop,
            from = source.from,
            to = source.to,
            fromTrack = source.fromTrack,
            toTrack = source.toTrack,
            type = source.type,
            state = source.state,
            station = source.station,
            source = source.source,
            couplingRequired = source.couplingRequired,
            handbrakeRequired = source.handbrakeRequired,
            cargo = (source.cargo ?? Array.Empty<string>()).ToArray(),
            cargoAmount = source.cargoAmount,
            cars = (source.cars ?? Array.Empty<string>()).ToArray(),
        };
    }
}
