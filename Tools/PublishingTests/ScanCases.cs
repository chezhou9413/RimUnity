using System.Collections;
using System.Security.Cryptography;
using RimUnity.Editor;

internal static class ScanCases
{
    internal static void Run()
    {
        void Check(bool valid, string text) { if (!valid) throw new Exception(text); Console.WriteLine("PASS scanning: " + text); }
        int count = 0; bool installed = false, disposed = false;
        IEnumerator Work()
        {
            try
            {
                for (int i = 0; i < 50; i++) { count++; yield return null; }
                installed = true;
            }
            finally { disposed = true; }
        }
        using (var scan = new ScanStepper(Work()))
        {
            Check(!scan.Advance(2, 10000) && count == 2 && !installed, "scan leaves work for later editor updates");
            scan.Advance(24, 0);
            Check(count == 3, "time budget yields after a single indivisible operation");
            scan.Dispose();
            Check(disposed && !installed, "cancellation disposes work and never installs partial results");
            Check(scan.Advance() && count == 3, "cancelled work cannot resume");
        }
        count = 0; disposed = installed = false;
        using (var scan = new ScanStepper(Work()))
            while (!scan.Advance(3, 10000)) { }
        Check(count == 50 && installed && disposed, "completed scan installs exactly once and releases iterator");
        IEnumerable<object> Failing()
        {
            try { yield return null; throw new IOException("simulated scan failure"); }
            finally { disposed = true; }
        }
        disposed = false;
        using (var scan = new ScanStepper(Failing().GetEnumerator()))
        {
            try { scan.Advance(10, 10000); }
            catch (IOException) { }
        }
        Check(disposed, "failed scan releases iterator resources");
        string file = Path.Combine(Path.GetTempPath(), "scan-" + Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllBytes(file, Enumerable.Range(0, 1024 * 1024).Select(i => (byte)i).ToArray());
        string result = null; int chunks = 0;
        foreach (var step in ExportChangeTracker.DigestSteps(file, value => result = value)) chunks++;
        string expected;
        using (var sha = SHA256.Create())
        using (var input = File.OpenRead(file))
            expected = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        Check(chunks >= 8 && result == expected, "chunked hash matches SHA256 without reading whole file per step");
        using (var iterator = ExportChangeTracker.DigestSteps(file, _ => { }).GetEnumerator())
            Check(iterator.MoveNext(), "large-file scan can pause before EOF");
        using (File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        Check(true, "cancellation closes the paused file stream");
        File.Delete(file);
    }
}
