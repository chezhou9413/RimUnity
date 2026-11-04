using RimUnity.Diagnostics;
using System.Text.Json;
using T = RimUnity.Diagnostics.LabTrailCpu;

int checks = 0;
void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
var capture = new T(8); capture.Register(101); capture.Register(102); capture.Start();
try {
    using (T.Measure(T.Stage.Geometry,101,true)) { }
    using (T.Measure(T.Stage.Submit,101)) using (T.Measure(T.Stage.GlobalFlush)) { }
    capture.CompleteFrame(true);
    try { using var scope = T.Measure(T.Stage.Geometry,101); throw new InvalidOperationException("fixture"); }
    catch (InvalidOperationException) { }
    using (T.Measure(T.Stage.Geometry,102)) { }
    capture.CompleteFrame(true);
    var report=capture.Snapshot();
    Check(report.Frames==2 && report.LegacyGeometryCalls==1 && report.CurrentGeometryCalls==2,"actual variant calls");
    Check(report.Stages[0].Calls==3 && report.Stages[1].Calls==1 && report.Stages[2].Calls==1,"geometry/submit/overlapping flush");
    Check(report.Systems[0].Geometry.Calls==2 && report.Systems[1].Geometry.Calls==1,"per-system identity totals");
    Check(report.Systems.All(s=>s.Geometry.P95Ms==null && s.Submit.MaxMs==null),"unavailable system quantiles must be null");
    Check(report.Stages[0].P99Ms>=report.Stages[0].P95Ms && report.InvalidFrames==0,"distribution and exception scope cleanup");
    using (T.Measure(T.Stage.Geometry,999)) { }
    Task.Run(()=>{ using var scope=T.Measure(T.Stage.Geometry,101); }).GetAwaiter().GetResult();
    capture.CompleteFrame(false);
    report=capture.Snapshot();
    Check(report.UnknownSystemScopes==1 && report.WrongThreadScopes==1 && report.Frames==2,"reject unknown and non-owner scopes, omit partial frame");
    var orphan=T.Measure(T.Stage.Geometry,101); capture.CompleteFrame(true); orphan.Dispose();
    using (T.Measure(T.Stage.Geometry,101)) { } capture.CompleteFrame(true);
    Check(capture.Snapshot().InvalidFrames==1 && capture.Snapshot().Stages[0].Calls==4,"drop unclosed frame and stale disposal");
} finally { capture.Stop(); }
var stopped=capture.Snapshot(); using(T.Measure(T.Stage.Geometry,101)) { }
Check(ReferenceEquals(stopped,capture.Snapshot()),"stopped snapshot cached, collection disabled");
var next=new T(10032); next.Register(101); next.Start();
try {
    var warm=System.Diagnostics.Stopwatch.StartNew();
    while(warm.ElapsedMilliseconds<1500) { using(T.Measure(T.Stage.Geometry,101)) { } next.CompleteFrame(false); }
    for(int i=0;i<32;i++) { using(T.Measure(T.Stage.Geometry,101)) { } next.CompleteFrame(true); }
    long bytes=GC.GetAllocatedBytesForCurrentThread();
    for(int i=0;i<10000;i++) { using(T.Measure(T.Stage.Geometry,101)) { } next.CompleteFrame(true); }
    Check(GC.GetAllocatedBytesForCurrentThread()==bytes,"source timing and frame commit allocate after warmup");
    var late=T.Measure(T.Stage.Geometry,101); next.Stop(); late.Dispose();
    Check(next.Snapshot().Frames==10032 && next.Snapshot().InvalidFrames==1,"late return cannot add to retired capture");
} finally { next.Stop(); }
var bounded=new T(1); for(int i=0;i<1024;i++) bounded.Register(i);
bool exceeded=false; try { bounded.Register(1024); } catch(InvalidOperationException) { exceeded=true; }
Check(exceeded,"registration bounded"); bounded.Stop();

var boundaries=new LabCpuMetrics(4,LabCpuMode.BridgeOnly,trailDetail:true);
boundaries.RegisterSystem(123,5); boundaries.Start();
try {
    for(int f=0;f<3;f++) {
        boundaries.BeginMainFrame();
        using(LabCpuMetrics.BridgeWindow()) {
            using(T.Measure(T.Stage.Geometry,123,true)) { }
            using(T.Measure(T.Stage.Submit,123)) { }
        }
        boundaries.CompleteFrame();
    }
} finally { boundaries.Stop(); }
var aligned=boundaries.Trails.Snapshot();
Check(aligned.Frames==2 && aligned.LegacyGeometryCalls==2 && boundaries.FrameCount==3,"coarse rows use exactly the complete Bridge windows");
Check(aligned.InvalidFrames==0 && aligned.UnknownSystemScopes==0,"Bridge-only still registers actual numeric identities");

var jobs=LabMeasurementPlan.TrailJobs();
var detailJobs=LabMeasurementPlan.TrailDetailJobs();
Check(detailJobs.Length==2 && detailJobs[0].Mode==LabCpuMode.BridgeOnly && detailJobs[1].Mode==LabCpuMode.Stages
    && detailJobs.All(j=>j.Roots==4 && j.ParticlesPerRoot==256 && j.Family==5 && !j.LegacyTrails),"detail sequence keeps the same current-path load and orders boundary before detail capture");
Check(jobs.Length==4 && jobs.Select(j=>j.LegacyTrails).SequenceEqual(new[]{true,false,false,true}),"ABBA plan");
Check(jobs.All(j=>j.Mode==LabCpuMode.BridgeOnly && j.Roots==4 && j.ParticlesPerRoot==256 && j.Family==5),"one controlled load");
T.Report Costs(bool legacy) => new(99,0,0,0,legacy?396:0,legacy?0:396,new[]{
    new T.Row("Geometry",396,4,legacy?1:.8,1,1,1,0),
    new T.Row("Submit",396,4,.1,.1,.1,.1,0),
    new T.Row("GlobalFlush",99,1,.2,.2,.2,.2,0)},Array.Empty<T.SystemRow>(),"pure statistics input");
var rows=jobs.Select((j,i)=>new LabMeasurementPlan.Run(j,"fixture",100,99,new[]{2d,1.8,1.82,2.02}[i],.1,2.1,2.2,4,1024,10000,40000,60000,.1,1,0,0,0,Costs(j.LegacyTrails))).ToArray();
var result=LabMeasurementPlan.CompareTrailWrites(rows);
Check(result.Status=="CurrentFasterThanObservedSpread" && Math.Abs(result.BridgeDeltaMs+.2)<1e-10 && Math.Abs(result.GeometryDeltaMs+.2)<1e-10,"correct delta direction and stage summaries");
Check(LabMeasurementPlan.CompareTrailWrites(rows.Take(3).ToArray()).Status=="Incomplete","partial sequence");
var altered=rows.ToArray(); altered[1]=altered[1] with{TrailCpu=Costs(true)};
Check(LabMeasurementPlan.CompareTrailWrites(altered).Status=="WriteVariantMismatch","prove actual executed path");
altered=rows.ToArray(); altered[1]=altered[1] with{Particles=2000};
Check(LabMeasurementPlan.CompareTrailWrites(altered).Status=="WorkloadMismatch","reject workload drift");
altered=rows.ToArray(); altered[1]=altered[1] with{TrailCpu=Costs(false) with{UnknownSystemScopes=1}};
Check(LabMeasurementPlan.CompareTrailWrites(altered).Status=="CoverageInvalid","reject missing coverage");
altered=rows.ToArray(); altered[1]=altered[1] with{TrailPoints=0};
Check(LabMeasurementPlan.CompareTrailWrites(altered).Status=="MissingTrailWork","empty workload is not a speedup");
Check(JsonSerializer.Serialize(stopped).Contains("\"P95Ms\":null"),"unavailable quantiles remain explicit in JSON");
Console.WriteLine($"PASS {checks} actual source collector/statistics checks; zero warm allocation; no engine substitute or mock host. No game/GPU claim.");
