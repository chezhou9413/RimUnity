using RimUnity.Diagnostics;
using RimUnity;
using System.Text.Json;
using System.Threading;

internal static class Program
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    private static void Main()
    {
        var capture = new LabParticleDetail(100, _ => 2); capture.Start();
        for (int f = 0; f < 63; f++)
        {
            capture.BeginFrame();
            using (LabParticleDetail.Measure(ParticleDetailStage.Geometry, 123))
            {
                using (LabParticleDetail.Measure(ParticleDetailStage.VertexWrite))
                { LabParticleDetail.Work(ParticleDetailWork.Vertices, 4); }
            }
            using (LabParticleDetail.Measure(ParticleDetailStage.Simulation))
                using (LabParticleDetail.Measure(ParticleDetailStage.SystemStep, 123))
                    LabParticleDetail.Work(ParticleDetailWork.Integrations, 5);
            capture.CompleteFrame();
        }
        capture.Stop(); var report = capture.Snapshot();
        var geometry = report.Domains[0]; var simulation = report.Domains[1];
        Check(geometry.SelectedFrames == 3 && simulation.SelectedFrames == 2, "per-domain selected frames");
        Check(report.OrdinaryFrames == 58 && report.InvalidFramesOrScopes == 0, "ordinary denominator excludes detailed frames");
        Check(geometry.Stages.Single(s => s.Stage == "VertexWrite").CallsPerSelectedFrame == 1, "stage means use own domain denominator");
        Check(geometry.Work.Single(w => w.Counter == "Vertices").Count == 12, "geometry work is not counted on simulation/ordinary frames");
        Check(simulation.Work.Single(w => w.Counter == "Integrations").Count == 10, "simulation work count");
        Check(geometry.Families.Any(f => f.Family == 2 && f.Stage == "VertexWrite" && f.Calls == 3), "family inherited by child scopes");
        var parent = geometry.Stages.Single(s => s.Stage == "Geometry"); var child = geometry.Stages.Single(s => s.Stage == "VertexWrite");
        Check(parent.InclusiveMs >= parent.SelfMs + child.InclusiveMs - 1e-9, "nested inclusive/self accounting");
        Check(geometry.Stages.Single(s => s.Stage == "Atlas").Calls == 0, "zero calls remain visible");
        Check(JsonSerializer.Serialize(report).Contains("CallsPerSelectedFrame"), "JSON report serializes named measurements");
        var directory = Path.Combine(Path.GetTempPath(), "RimUnity", "ParticleDetail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); capture.WriteFrames(Path.Combine(directory, "frames.csv"));
        Check(File.ReadAllLines(Path.Combine(directory, "frames.csv")).Length == 1 + 3 * (int)ParticleDetailStage.Simulation + 2 * ((int)ParticleDetailStage.Count - (int)ParticleDetailStage.Simulation), "selected-frame CSV only");
        Check(report.EmptyScopeNanoseconds > 0 && new LabParticleDetail(36000).BufferBytes <= 16 * 1024 * 1024, "calibration and memory bound");
        bool rejected = false; try { capture.Start(); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "retired session cannot restart");
        capture = new LabParticleDetail(10); capture.Start(); capture.BeginFrame();
        var thread = new Thread(() => { using var scope = LabParticleDetail.Measure(ParticleDetailStage.Geometry); }); thread.Start(); thread.Join();
        capture.CompleteFrame(); capture.Stop(); Check(capture.Snapshot().CrossThreadCalls == 1, "cross-thread probe is rejected");
        capture = new LabParticleDetail(10); capture.Start(); capture.BeginFrame();
        var token = LabParticleDetail.Measure(ParticleDetailStage.Geometry); token.Dispose(); token.Dispose();
        capture.CompleteFrame(); capture.Stop(); Check(capture.Snapshot().Domains[0].SelectedFrames == 0, "invalid frame cannot appear as zero cost");
        capture = new LabParticleDetail(10); capture.Start(); capture.BeginFrame();
        using (LabParticleDetail.Measure(ParticleDetailStage.Geometry)) { try { using var scope = LabParticleDetail.Measure(ParticleDetailStage.Atlas); throw new Exception(); } catch (Exception) { } }
        capture.CompleteFrame(); capture.Stop(); Check(capture.Snapshot().InvalidFramesOrScopes == 0, "exception unwinding balances scopes");
        capture = new LabParticleDetail(10); capture.Start(); capture.BeginFrame(); capture.BeginFrame(); capture.CompleteFrame(); capture.Stop();
        Check(capture.Snapshot().Domains[0].SelectedFrames == 0, "duplicate pre-poll boundary invalidates frame");
        capture = new LabParticleDetail(10); capture.Start(); capture.CompleteFrame(); capture.Stop();
        Check(capture.Snapshot().Domains.All(d => d.SelectedFrames == 0), "partial first frame excluded");
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) { using var scope = LabParticleDetail.Measure(ParticleDetailStage.Geometry); }
        Check(GC.GetAllocatedBytesForCurrentThread() == bytes, "retired probes allocate nothing");
        capture = new LabParticleDetail(36000); capture.Start();
        // Warm the active path, then prove collection itself stays allocation-free.
        capture.BeginFrame(); using (LabParticleDetail.Measure(ParticleDetailStage.Geometry)) { }
        capture.CompleteFrame();
        bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        { capture.BeginFrame(); using (LabParticleDetail.Measure(ParticleDetailStage.Geometry)) { LabParticleDetail.Work(ParticleDetailWork.Vertices); } capture.CompleteFrame(); }
        Check(GC.GetAllocatedBytesForCurrentThread() == bytes, "active frame collection allocates zero bytes"); capture.Stop();
        capture = new LabParticleDetail(10); capture.Start(); capture.BeginFrame();
        using (LabParticleDetail.Measure(ParticleDetailStage.BatchAppend, 123))
        {
            using (ParticleMeshProbe.Measure(ParticleMeshStage.CopyVertices)) { }
            try { using var core = ParticleMeshProbe.Measure(ParticleMeshStage.CopyIndices); throw new Exception(); } catch (Exception) { }
        }
        capture.CompleteFrame(); capture.Stop();
        var meshRows = capture.Snapshot().Domains[0].Stages;
        Check(meshRows.Single(s => s.Stage == "BatchCopyVertices").Calls == 1
            && meshRows.Single(s => s.Stage == "BatchCopyIndices").Calls == 1, "Core probe routes into detail collector, including exception unwind");
        Check(capture.Snapshot().InvalidFramesOrScopes == 0 && ParticleMeshProbe.Observer == null, "Core observer released after capture");
        capture = new LabParticleDetail(10); capture.Start(); capture.BeginFrame();
        var lateCore = ParticleMeshProbe.Measure(ParticleMeshStage.Bounds);
        capture.Stop(); lateCore.Dispose();
        Check(capture.Snapshot().InvalidFramesOrScopes == 1 && ParticleMeshProbe.Observer == null, "late Core scope cannot write to stopped capture");
        bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) { using var core = ParticleMeshProbe.Measure(ParticleMeshStage.Validate); }
        Check(GC.GetAllocatedBytesForCurrentThread() == bytes, "inactive Core probes allocate nothing");
        capture = new LabParticleDetail(36000); capture.Start();
        for (int f = 0; f < 100; f++)
        { capture.BeginFrame(); using (LabParticleDetail.Measure(ParticleDetailStage.BatchAppend)) { using var core = ParticleMeshProbe.Measure(ParticleMeshStage.Validate); } capture.CompleteFrame(); }
        bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int f = 0; f < 1000; f++)
        { capture.BeginFrame(); using (LabParticleDetail.Measure(ParticleDetailStage.BatchAppend)) { using var core = ParticleMeshProbe.Measure(ParticleMeshStage.Validate); } capture.CompleteFrame(); }
        Check(GC.GetAllocatedBytesForCurrentThread() == bytes, "active Core probes allocate nothing after warmup"); capture.Stop();
        Console.WriteLine($"PASS {checks} particle detail checks: denominators, nesting, families, numeric work, CSV/JSON, memory bound, thread isolation, invalid/partial frames, exceptions and zero allocations.");
    }
}
