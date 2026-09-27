using AdvancedDispatcherSystem.Core;
namespace AdvancedDispatcherSystem.Host;

// Explicit developer fixture. Never selected by the game mod; labelled DEMO by the UI.
public static class DemoFixture
{
    public static async Task Run(StateHub hub, CancellationToken cancel)
    {
        const int count = 2500; var tracks = new TrackDef[count]; var junctions = new List<JunctionDef>();
        for (int i = 0; i < count; i++)
        {
            int col = i % 100, row = i / 100; double x = col * 180, z = row * 120 + Math.Sin(col * 0.15) * 35;
            tracks[i] = new TrackDef { id = "t" + i, name = "Line " + (row + 1) + " · " + (col + 1), length = 180, points = [x, z, x + 60, z, x + 120, z + 4, x + 180, row * 120 + Math.Sin((col + 1) * 0.15) * 35], a = col == 0 ? [] : [new Link { track = "t" + (i - 1), end = 1 }], b = col == 99 ? [] : [new Link { track = "t" + (i + 1), end = 0 }] };
            if (col > 0 && col < 98 && col % 5 == 0 && row < 24)
            {
                string id = "j" + junctions.Count;
                junctions.Add(new JunctionDef { id = id, name = "J-" + junctions.Count, incoming = "t" + i, branches = ["t" + (i + 1), "t" + (i + 101)], x = x + 180, z = z });
                tracks[i].b = [new Link { track = "t" + (i + 1), end = 0, junction = id, branch = 0 }, new Link { track = "t" + (i + 101), end = 0, junction = id, branch = 1 }];
            }
        }
        string epoch = "DEVELOPMENT-FIXTURE";
        tracks = tracks.Concat(new[] {
            new TrackDef {id="tt-west",name="Table west",length=100,points=[-110,3300,-10,3300]},
            new TrackDef {id="tt-east",name="Table east",length=100,points=[10,3300,110,3300]},
            new TrackDef {id="tt-north",name="Table north",length=100,points=[0,3310,0,3410]},
            new TrackDef {id="tt-south",name="Table south",length=100,points=[0,3190,0,3290]},
            new TrackDef {id="tt-bridge",name="Table bridge",length=20,points=[0,3310,0,3290]}
        }).ToArray();
        var table = new TurntableDef {id="tt",name="Fixture turntable",track="tt-bridge",x=0,z=3300,radius=10,ends=[new TurntableEnd{track="tt-west",end=1,angle=270},new TurntableEnd{track="tt-east",end=0,angle=90},new TurntableEnd{track="tt-north",end=0,angle=0},new TurntableEnd{track="tt-south",end=1,angle=180}]};
        hub.SetTopology(new Topology { epoch = epoch, revision = 1, tracks = tracks, junctions = junctions.ToArray(), turntables=[table], stations = Enumerable.Range(0, 10).Select(i => new StationDef { id="ST"+i, name="Fixture station "+i, x=i*1500, z=i*200, industry=i%2==0, tracks=["t"+i] }).ToArray() });
        var signals = new SignalState[2000]; var blocks = new BlockState[2000];
        for (int i = 0; i < signals.Length; i++)
        {
            var t = tracks[i]; string color = i % 9 == 0 ? "#ff625d" : i % 7 == 0 ? "#f7c65d" : "#74dca0";
            signals[i] = new SignalState { id = "s" + i, name = "S-" + i, aspect = i % 9 == 0 ? "Stop" : "Clear", aspectIndex = 0, aspectCount = 3, classificationKnown = true, shuntingSignal = i % 5 == 0, type = i % 5 == 0 ? "Shunting" : "Mainline", lamps = [color], blinkingLamps = [i % 7 == 0], x = t.points[0] + 10, z = t.points[1] + 12, track = t.id, block = "b" + i, stop = i % 9 == 0, mode = "Automatic", span = 10, direction = 1, revision = 1, sampledAt = Protocol.Now };
            blocks[i] = new BlockState { id = "b" + i, name = "B-" + i, source = "Fixture", tracks = [t.id], directions = [1], signal = "s" + i, length = 180, sampledAt = Protocol.Now };
        }
        hub.Apply(new GameBatch { epoch = epoch, topologyRevision = 1, turntables=[new TurntableState{id="tt",angle=0,target=0,front="tt-north",frontEnd=0,rear="tt-south",rearEnd=1,points=[0,3310,0,3290],available=true,revision=1,sampledAt=Protocol.Now}], signs = Enumerable.Range(0, 600).Select(i=>new SignState {id="sign"+i,track=tracks[i].id,x=tracks[i].points[0]+30,z=tracks[i].points[1],span=30,direction=1,speeds=[i%3==0?40:60],branches=[-1],source="Fixture",types=["SpeedLimit"]}).ToArray(), jobs = Enumerable.Range(0, 1000).Select(i=>new JobState {id="JOB-"+i, type="Transport",state=i<100?"InProgress":i<800?"Available":i<900?"Completed":"Abandoned",active=i<100, origin="ST"+(i%10),destination="ST"+((i+1)%10),cars=["c"+(i%100*30)],cargo=["SteelRolls"],payment=4500,bonus=2250,elapsedSeconds=300,bonusLimitSeconds=1800,sampledGameTime=0,sampledAt=Protocol.Now,tasksDone=1,tasksTotal=2,legs=[new JobLeg{from="A",to="B",fromTrack="t0",toTrack="t5",type="Transport",state="InProgress",cars=["CAR-1"],cargo=["SteelRolls"]}]}).ToArray(), switches = junctions.Select(j => new SwitchState { id = j.id, branch = 0, revision = 1 }).ToArray(), signals = signals, blocks = blocks, occupancy = tracks.Select(t => new OccupancyState { id = t.id, sampledAt = Protocol.Now }).ToArray(), replacePlayers = true, players = [new PlayerState { id = "p1", name = "Dispatcher", x = 320, z = 400, host = true }] });
        var clock = System.Diagnostics.Stopwatch.StartNew();
        hub.Plan("tt-west","tt-east","UI fixture");
        var archived=hub.Plan("t1","t3","UI fixture");if(archived!=null)hub.CancelRoute(archived.id,"UI fixture");
        bool firstFrame = true;
        while (!cancel.IsCancellationRequested)
        {
            double time = clock.Elapsed.TotalSeconds; var cars = new CarState[3000];
            for (int i = 0; i < cars.Length; i++)
            {
                int train = i / 30, row = train % 25, group = train / 25;
                double x = (group * 4100 + time * (5 + train % 8) + (i % 30) * 18) % 17800;
                int col = (int)(x / 180); double z = row * 120 + Math.Sin(x / 180 * 0.15) * 35;
                cars[i] = new CarState { id = "c" + i, name = i % 30 == 0 ? "L-" + train : "CAR-" + i, consist = "train:" + train, order = i % 30, x = x, z = z, yaw = 90, length = 16, speed = train % 2 == 0 ? 36 : -36, direction = train % 2 == 0 ? 1 : -1, span1 = x % 180, span2 = x % 180, job = "JOB-"+train, cargoKnown = true, cargo = i % 30 == 0 ? "None" : "SteelRolls", cargoAmount = i % 30 == 0 ? 0 : 1, locomotive = i % 30 == 0, vehicleCategory = i % 30 == 0 ? "locomotive" : "wagon", nativeTrainset = true, track1 = "t" + (row * 100 + col), track2 = "t" + (row * 100 + col), sampledAt = Protocol.Now };
            }
            hub.Apply(new GameBatch { epoch = epoch, topologyRevision = 1, cars = firstFrame ? cars : null, motions = firstFrame ? null : cars.Select(CarMotion.From).ToList(), capabilities = new CapabilityState { mode = "demo", status = "ready", gameTime = time, clockRate = 1, signsStatus = "indexed", language = "ru", authority = false, signals = true, signalsStatus = "ready", trackCount = tracks.Length, carCount = 3000, signalCount = 2000, sampledAt = Protocol.Now } });
            firstFrame = false;
            await Task.Delay(100, cancel);
        }
    }
}
