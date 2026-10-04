using System;

namespace AdvancedDispatcherSystem.Core
{
    // Wire records are owned by the producer until publication, and never mutated afterwards.
    // They contain no Unity, lazy iterators or third party integration objects.
    public static class Protocol
    {
        public const int Version = 5;
        public const int MaxFrameBytes = 16 * 1024 * 1024;
        public const int MaxCommandBytes = 8192;
        public const int MaxInternalCommandBytes = 512 * 1024;
        public static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
    public sealed class Link
    {
        public string track;
        public int end;
        public string junction;
        public int branch = -1;
        public string turntable;
        public int position = -1;
        public double crossingLength;
    }
    public sealed class TrackDef
    {
        public string id, name;
        public double length;
        // Penalty derived from native station/warehouse spawn metadata. It is
        // a routing preference only; it never makes a track unreachable.
        public double routePenalty;
        public double[] points;
        public double[] spans = new double[0];
        public Link[] a = new Link[0], b = new Link[0];
    }
    public sealed class JunctionDef
    {
        public string id, name, incoming;
        public string[] branches;
        public double x, z;
    }
    public sealed class Topology
    {
        public string epoch;
        public long revision;
        public TrackDef[] tracks;
        public JunctionDef[] junctions;
        public StationDef[] stations = new StationDef[0];
        public TurntableDef[] turntables = new TurntableDef[0];
        public TopologyCaptureInfo capture;
    }
    public sealed class TopologyCaptureInfo
    {
        public int registeredTracks, exportedTracks, filteredLinks;
        public string[] missingGeometry = new string[0];
    }
    public sealed class TurntableEnd
    {
        public string track;
        public int end;
        public double angle;
    }
    public sealed class TurntableDef
    {
        public string id, name, track;
        public double x, z, radius;
        public TurntableEnd[] ends = new TurntableEnd[0];
    }
    public sealed class TurntableState
    {
        public string id, front, rear, reason;
        public int frontEnd = -1, rearEnd = -1;
        public double angle, target;
        public double[] points = new double[0];
        public bool moving, available;
        public long revision, sampledAt;
    }
    public sealed class TurntableStep
    {
        public string id, from, to;
        public int position;
        public int fromEnd = -1, toEnd = -1;
    }
    public sealed class StationDef
    {
        public string nameEn, nameRu;
        public string id, name, type, source, parent, code, platform, platformLabel, trackGroup, color;
        public string[] searchNames = new string[0];
        public double x, z;
        public bool industry, city, passenger;
        public string[] tracks = new string[0];
        // Authoritative native track membership for the location. `tracks`
        // remains the compact routing identity list; this projection carries
        // metadata for the location inspector without parsing display names.
        public StationTrackDef[] stationTracks = new StationTrackDef[0];
        // Native station tracks used by warehouse/job car generation. Kept
        // separate from all station tracks so through rails remain preferred
        // when the game exposes an explicit operational spawn track.
        public string[] spawnTracks = new string[0];
    }
    public sealed class StationTrackDef
    {
        public string id, name, fullName, group;
        public int direction;
    }
    public sealed class SignState
    {
        public string id, track, junction, source;
        public double x, z, span;
        public int direction;
        public int[] speeds = new int[0], branches = new int[0];
        public string[] types = new string[0];
        public bool advance;
    }
    public struct SwitchState
    {
        public string id;
        public int branch;
        public long revision, sampledAt;
    }
    public sealed class CargoSummary {
        public bool complete;
        public int cars;
        public string[] types = new string[0];
        // Physical coupler-chain facts, captured atomically with cargo on Unity's thread.
        public bool lengthKnown, membershipComplete;
        public double length;
        public string[] members = new string[0];
    }
    public struct CarState
    {
        public string id, name, consist, type, track1, track2, job, destination;
        public string cargo, model, modelLanguage, catalogModel, catalogColor, vehicleCategory;
        public string availability;
        public string[] searchNames;
        public bool couplersKnown;
        public string coupledFront, coupledRear;
        public bool nativeTrainset;
        public bool cargoKnown;
        public CargoSummary consistCargo;
        public double cargoAmount;
        public double x, z, yaw, length, width, span1, span2, speed;
        public int direction, order, frontCount, rearCount;
        public bool locomotive, derailed, controllable, slipping, canCouple;
        // Authoritative mass values captured from TrainMassController. Values
        // are expressed in metric tonnes on the wire; zero is valid only when
        // massKnown is false (for suspended/uninitialised runtime objects).
        public bool massKnown;
        public double mass, consistMass;
        public bool tractionKnown;
        public double availableTraction;
        public float throttle, trainBrake, independentBrake, reverser, brakePipe;
        public long revision, sampledAt;
    }
    public struct PlayerState
    {
        public string identityKey;
        public string id, name, car;
        public double x, z, yaw, carX, carZ, carYaw;
        public bool carPoseKnown;
        public bool host;
        public long sampledAt;
    }
    public struct CarMotion
    {
        public string id, track1, track2;
        public double x, z, yaw, speed, span1, span2;
        public int direction;
        public bool derailed;
        public long sampledAt;
        public static CarMotion From(CarState c) => new CarMotion { id = c.id, track1 = c.track1, track2 = c.track2, x = c.x, z = c.z, yaw = c.yaw, speed = c.speed, span1 = c.span1, span2 = c.span2, direction = c.direction, derailed = c.derailed, sampledAt = c.sampledAt };
        public CarState Apply(CarState c) { c.x = x; c.z = z; c.yaw = yaw; c.speed = speed; c.span1 = span1; c.span2 = span2; c.track1 = track1; c.track2 = track2; c.direction = direction; c.derailed = derailed; c.sampledAt = sampledAt; return c; }
    }
    public sealed class SignalLamp
    {
        // Coordinates in the owning SignalDefinition face, not world offsets.
        public string id, color;
        public bool phaseKnown, phaseOn;
        public double x, y;
        public bool on, blinking, brightnessKnown;
        public double brightness;
        public bool indicationKnown, indicated, indicationBlinking;
    }
    public sealed class SignalAspectInfo
    {
        public string id, reason;
        public bool stop;
        public float speed = -1;
    }
    public sealed class SignalPart
    {
        public string id, kind, text, color;
        public int order;
        public double x, y, worldX, worldZ;
        public bool positioned, active, stateKnown;
        public bool visible = true;
    }
    public sealed class SignalState
    {
        // Native controller's StartingTrack, distinct from the mast's placement track.
        public string authorityTrack;
        // Native junction gate, including single-head controllers and reversed mouths.
        public string routeIncoming;
        public string[] routeBranches = new string[0];
        public int routeIncomingDirection;
        public int[] routeBranchDirections = new int[0];
        public bool routeBindingRequired;
        public string displayLayer;
        public SignalLamp[] lampLayout = new SignalLamp[0];
        public SignalPart[] parts = new SignalPart[0];
        public string objectKind = "signal", signKind;
        public SignalAspectInfo[] aspectInfo = new SignalAspectInfo[0];
        public string controller;
        public int displayOrder;
        public bool reservable = true;
        public string id, name, aspect, type, mode, track, block, parent, visualKind, visualState;
        public string[] lamps = new string[0], aspects = new string[0];
        public bool classificationKnown;
        public bool[] blinkingLamps = new bool[0];
        public int aspectIndex, overrideIndex, direction, aspectCount;
        public double x, z, yaw, span;
        public bool stop, off, shunting, shuntingSignal, reserved, turntableConnected, turntableStateKnown;
        public float reservationSeconds, passingSpeed;
        public long revision, sampledAt;
    }
    public sealed class BlockState
    {
        public string id, name, source, signal;
        public string[] tracks = new string[0], extraTracks = new string[0];
        public string[] trains = new string[0];
        public int[] directions = new int[0];
        public double length;
        public bool occupied, reserved, deadEnd;
        public string quality = "ready";
        public long sampledAt;
    }
    public struct OccupancyState
    {
        public string id;
        public bool occupied;
        public string[] cars;
        public bool carsKnown;
        public long sampledAt;
    }
    public sealed class JobLeg
    {
        public string id, progress = "unknown", operation;
        public bool passengerStop;
        public string from, to, fromTrack, toTrack, type, state, station, source;
        public bool couplingRequired, handbrakeRequired;
        public string[] cargo = new string[0];
        public double cargoAmount;
        public string[] cars = new string[0];
    }
    public sealed class JobState
    {
        public string assignedPlayerId, assignedPlayerName, assignedPlayerKey;
        public string id, origin, destination, type, state, owner, ownerKey, ownerStatus, startedGameDate;
        public string typeColor, typeSource, integrationStatus, typeName, typeLanguage;
        public string dataQuality = "ready";
        public bool active, elapsedKnown, massKnown;
        public double length, mass, payment, bonus, elapsedSeconds, bonusLimitSeconds, sampledGameTime;
        public long sampledAt;
        public int tasksDone, tasksTotal;
        public string[] cars = new string[0], cargo = new string[0];
        public string[] licenses = new string[0];
        public JobLeg[] legs = new JobLeg[0];
    }
    public sealed class CapabilityState
    {
        public string passengerStatus = "absent", passengerVersion = "";
        public string mode = "loading", status = "loading", signalsStatus = "absent", multiplayerVersion = "", language = "en", signsStatus = "loading";
        public double gameTime, clockRate;
        // Authoritative current time-of-day from DV.TimeKeeping.WorldClockController.
        // gameTime is the job timer and is deliberately kept separate from this
        // clock value so the Weather panel never treats elapsed job seconds as a
        // wall-clock time.
        public double gameTimeOfDay;
        public bool gameTimeOfDayKnown;
        public bool authority, signals, signalCommands, locoControls, protectedReservations;
        public int trackCount, carCount, signalCount;
        public double captureMs;
        public long sampledAt;
        public WeatherState weather = new WeatherState();
        // Settings that affect the captured multiplayer game are owned by the
        // game host.  They are published as state so remote browser clients
        // can display the effective values without treating them as local
        // display preferences. Transport credentials are never included.
        public HostSettingsState hostSettings;
    }
    public sealed class HostSettingsState
    {
        public bool readOnly, showUndiscovered, adminControls;
        public double captureBudgetMs;
        public bool remoteLanAccess;
        public int port;
        public string publicHost;
    }
    public sealed class WeatherState
    {
        // Values come from DV.WeatherSystem at runtime. No external weather
        // service or fabricated forecast is used by the dispatcher.
        public string state = "unknown", dataQuality = "unavailable", icon;
        public bool rainKnown, wetnessKnown, thunderKnown, fogKnown, cloudinessKnown;
        public double rain, wetness, thunder, fog, cloudiness;
        // Published only from the native WeatherForecaster's valid daily
        // interpretedData, retaining its current/upcoming buckets.
        public bool forecastKnown;
        public WeatherForecastEntry[] forecast = Array.Empty<WeatherForecastEntry>();
        public long sampledAt;
    }
    public sealed class WeatherForecastEntry
    {
        public string state = "unknown", icon;
        public int hourStart, hourEnd;
        public bool durationKnown, timeKnown;
        public double durationSeconds, startsInSeconds;
    }
    public sealed class GameBatch
    {
        public StationDef[] locations;
        public bool replaceLocations;
        public RouteRuntimeState[] routeStates;
        public string epoch;
        public long topologyRevision;
        public bool reset;
        public SwitchState[] switches;
        public TurntableState[] turntables;
        public CarState[] cars;
        public System.Collections.Generic.List<CarMotion> motions;
        public PlayerState[] players;
        public SignalState[] signals;
        public BlockState[] blocks;
        public OccupancyState[] occupancy;
        public JobState[] jobs;
        public SignState[] signs;
        public bool replaceSigns;
        public EventEntry[] events;
        public string[] removedCars, removedSignals, removedBlocks;
        public bool replacePlayers, replaceJobs;
        public CapabilityState capabilities;
    }
    public sealed class Command
    {
        public string reservationMode, routeId, routeEditId;
        // Planner preference: when false, a path may cross another route's
        // reservation and the reservation boundary will require an explicit
        // normal-mode confirmation. Protected native reservations remain
        // authoritative and cannot be bypassed.
        public bool avoidReservations = true, forceReservation;
        // Optional linkage for a route requested on behalf of one concrete
        // order task. The task remains authoritative in the game; a route is
        // only the current movement plan for it.
        public string jobId, taskId;
        public int taskIndex = -1;
        // Only the authenticated Host builds this internal execution plan.
        public RoutePlan route;
        public string id, epoch, kind, target, action, reason, from, to, train;
        // Ordered track ids that the route must visit between from and to.
        public string[] via = new string[0];
        public long topologyRevision, expectedRevision, deadline;
        public int branch, index;
        public int fromEnd = -1, toEnd = -1;
        public float value;
        // Set by the authenticated server, never taken from a browser identity claim.
        public string actor, role;
    }
    public sealed class CommandResult
    {
        // Authoritative route snapshot for the initiating client. Optional so
        // older clients and non-route receipts keep the existing protocol.
        public RoutePlan route;
        public string id, status, code, target;
        public long revision;
        public string[] warnings = new string[0];
    }
    public sealed class WireFrame
    {
        public int protocol = Protocol.Version;
        // Metadata and the immediately following compact motion frame form
        // one capture transaction. This IPC-only marker never enters Web DTOs.
        public int motionCount;
        public string kind, token, proof;
        public Topology topology;
        public GameBatch batch;
        public Command command;
        public CommandResult result;
    }
    public sealed class RoutePlan
    {
        // Path arrays/steps are replaced as a whole by route transactions. A
        // record copy freezes scalar fields and array references for IPC replay.
        public RoutePlan Snapshot() => (RoutePlan)MemberwiseClone();
        public string lifecycle = "active", reservationMode = "none", reservationState = "none", reason, trainCar;
        public string recalculationState = "none";
        public bool canRecalculate;
        public string invalidationReason, nativeReason;
        public string editVersion, recalculationKey;
        public int recalculationAttempts;
        // A recalculation is an edit of this route. The preview is kept on the
        // authoritative route record until the dispatcher confirms it, so the
        // old path/reservation remains intact while the proposed path is shown.
        public RouteEditPreview editPreview;
        public string[] trainCars = new string[0], reservedSignals = new string[0], reservationTracks = new string[0];
        // Native signal blocks are released as the consist tail clears them.
        // These fields retain route identity/progress without rewriting the
        // logical path (which may legitimately reuse a track in reverse).
        public string[] releasedSignals = new string[0], releasedTracks = new string[0];
        public long endedAt;
        public string id, name, owner, reservationOwner, assignedPlayerKey, assignedPlayerName, from, to, train, status = "planned";
        // Order/task linkage is metadata only and survives route edits. It
        // never turns a route into the order itself.
        public string jobId, taskId;
        public int taskIndex = -1;
        public bool passengerRoute;
        // A continuous reservation cannot safely hold a physical rail twice
        // (or two different switch positions).  In that case the logical
        // route remains one record and exposes independently reservable legs.
        public bool staged;
        public string stageStatus = "single", stagedReason, stagedReservationMode;
        public int stageIndex;
        public string activeFrom, activeTo;
        public RouteStage[] stages = new RouteStage[0];
        public string[] tracks, warnings;
        // Ordered intermediate track ids requested by the dispatcher.
        public string[] via = new string[0], manualVia = new string[0];
        public int[] directions;
        public RouteStep[] switches;
        public TurntableStep[] turntables = new TurntableStep[0];
        public double length;
        public long createdAt;
        public double startSpan, remaining;
        public RouteConflict[] conflicts = new RouteConflict[0];
        public RouteConflict[] history = new RouteConflict[0];
        public RoutePoint[] itinerary = new RoutePoint[0];
    }
    public sealed class RouteStage
    {
        public string id, from, to, status = "pending";
        public string reservationMode = "none", reservationState = "none";
        public string[] tracks = new string[0], reservedSignals = new string[0], reservationTracks = new string[0];
        public int[] directions = new int[0];
        public RouteStep[] switches = new RouteStep[0];
        public TurntableStep[] turntables = new TurntableStep[0];
        public double length, remaining, startSpan;
    }
    public sealed class RouteEditPreview
    {
        // Missing in previews persisted before VIA editing was introduced.
        public string[] via, manualVia;
        public string id, reason, affectedTrack, topologyEpoch;
        public bool staged;
        public string stageStatus, stagedReason, stagedReservationMode, activeFrom, activeTo;
        public int stageIndex;
        public RouteStage[] stages = new RouteStage[0];
        public long topologyRevision, createdAt;
        public string[] tracks = new string[0], reservationTracks = new string[0], reservedSignals = new string[0], warnings = new string[0];
        public int[] directions = new int[0];
        public RouteStep[] switches = new RouteStep[0];
        public TurntableStep[] turntables = new TurntableStep[0];
        public RoutePoint[] itinerary = new RoutePoint[0];
        public double length, startSpan, remaining;
    }
    public sealed class RouteConflict
    {
        public string id, route;
        public long firstSeen, lastSeen, resolvedAt;
        public string code, kind, target, track, train;
    }
    public sealed class RouteRuntimeState
    {
        public string id, lifecycle = "active", mode = "none", reservation = "none", reason;
        public string[] signals = new string[0];
        // The authoritative game plan lets a Host recover the path after a
        // reconnect even if the replacement transaction crossed a restart.
        public RoutePlan plan;
        public long time;
    }
    public sealed class RoutePoint
    {
        public bool boundary;
        public string kind, id, track;
        public double distance;
        public bool current;
    }
    public sealed class RouteStep
    {
        public string id;
        public int branch;
    }
    public sealed class EventEntry
    {
        public long time;
        public string id, code, actor, actorId, target, targetKind, detail, type, severity, source;
    }
}
