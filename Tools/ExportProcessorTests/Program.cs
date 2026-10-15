using RimUnity.Editor;
using UnityEngine;

internal static class Program
{
    private static int assertions;
    private static void Check(bool valid, string message) { assertions++; if (!valid) throw new Exception(message); }
    private static ExportContext Context() => new(new Material { name = "Source" }, "Assets/Source.mat", "Effects/Test", "guid_1", ".", false, false);
    private static ExportProcessorPipeline Pipeline(params IExportProcessor[] processors) => new(new ExportOptions { discoverProcessors = false, processors = processors });
    private static AggregateException Fails(Action action)
    {
        try { action(); } catch (AggregateException error) { return error; }
        throw new Exception("Expected resource failure");
    }
    private static void Main(string[] args)
    {
        var sequence = new List<string>();
        DelegateExportProcessor Processor(string id, int order) => new()
        {
            order = order,
            before = c => { sequence.Add("before" + id); c.DeferCleanup(() => sequence.Add("cleanup" + id)); },
            after = c => { Check(c.Succeeded, "Postprocessor observes successful write"); sequence.Add("after" + id); }
        };
        var context = Context();
        int result = Pipeline(Processor("B", 20), Processor("A", 10)).Run(context, c => { sequence.Add("write"); return 42; });
        Check(result == 42 && context.Succeeded, "Result survives processing");
        Check(string.Join(",", sequence) == "beforeA,beforeB,write,afterA,afterB,cleanupB,cleanupA", "Stable order and reverse cleanup");

        context = Context(); Material copy = null;
        Pipeline(new DelegateExportProcessor { before = c => { copy = c.CreateExportCopy<Material>(); copy.SingleChannel = 0; } })
            .Run(context, c => { Check(((Material)c.ExportAsset).SingleChannel == 0, "Writer sees processed copy"); Check(c.ResourceId == "guid_1" && c.SourcePath == "Assets/Source.mat", "Identity stays original"); return 0; });
        Check(((Material)context.Source).SingleChannel == 1 && !context.Source.Destroyed && copy.Destroyed, "Source preserved; copy destroyed");

        foreach (string stage in new[] { "before", "write", "after" })
        {
            context = Context(); bool restored = false, after = false;
            var processor = new DelegateExportProcessor
            {
                before = c => { c.DeferCleanup(() => restored = true); if (stage == "before") throw new InvalidOperationException(stage); },
                after = c => { after = true; if (stage == "after") throw new InvalidOperationException(stage); }
            };
            var failure = Fails(() => Pipeline(processor).Run(context, c => { if (stage == "write") throw new OperationCanceledException(stage); return 0; }));
            Check(restored && !context.Succeeded && context.Error != null, stage + " failure restores state");
            Check(after == (stage == "after"), "Postprocessing skipped on earlier failure");
            Check(failure.InnerExceptions.Count == 1, "Preserve original exception");
        }

        context = Context(); sequence.Clear();
        var aggregate = Fails(() => Pipeline(new DelegateExportProcessor { before = c =>
        {
            c.DeferCleanup(() => sequence.Add("last"));
            c.DeferCleanup(() => throw new InvalidOperationException("cleanup failure"));
            c.DeferCleanup(() => sequence.Add("first"));
        } }).Run<int>(context, c => throw new InvalidOperationException("write failure")));
        Check(sequence.SequenceEqual(new[] { "first", "last" }) && aggregate.InnerExceptions.Count == 2, "Cleanup continues and primary error retained");
        context = Context();
        Fails(() => Pipeline(new DelegateExportProcessor { before = c => c.DeferCleanup(() => throw new Exception("cleanup")) }).Run(context, c => 0));
        Check(!context.Succeeded && context.Error is AggregateException, "Cleanup failure invalidates an otherwise successful export");
        Fails(() => Pipeline(new DelegateExportProcessor { before = c => c.ExportAsset = new Texture() }).Run(Context(), c => 0));
        assertions++;
        bool wrote = false;
        var mismatch = Pipeline(new DelegateExportProcessor { filter = c => false, before = c => throw new Exception("must not run") });
        mismatch.Run(Context(), c => { wrote = true; return 0; });
        Check(wrote, "Unmatched processors do not prevent writing");

        var options = new ExportOptions { discoverProcessors = false, processors = new[] { new DelegateExportProcessor { before = c => sequence.Add("snapshot") } } };
        var snapshot = new ExportProcessorPipeline(options); options.processors[0] = null;
        snapshot.Run(Context(), c => 0); Check(sequence.Last() == "snapshot", "Batch snapshots processor array");
        UnityEditor.TypeCache.Types = new[] { typeof(Discovered) }; Discovered.Count = 0;
        new ExportProcessorPipeline(new ExportOptions()).Run(Context(), c => 0);
        new ExportProcessorPipeline(new ExportOptions()).Run(Context(), c => 0);
        Check(Discovered.Count == 2, "Automatic processors are batch-owned instances");

        string repo = Path.GetFullPath(args[0]);
        var articles = ManualLibrary.Load(Path.Combine(repo, "UnityPackage/Documentation~"));
        Check(articles.Count(a => a.category == "说明书") == 10, "All handbook chapters present");
        Check(articles.Any(a => a.source == "README.md") && articles.Any(a => a.source == "README.en.md"), "Repository README originals included");
        Check(articles.Any(a => a.source == "UnityPackage/README.md") && articles.Any(a => a.source == "Tools/Steam/README.md") && articles.Any(a => a.source == "1.6/README.txt"), "All README locations included");
        var source = articles.Single(a => a.source == "Docs/zh-CN/Workflow.md");
        Check(ManualLibrary.ResolveLink(articles, source, "../en/Workflow.md")?.source == "Docs/en/Workflow.md", "Cross-language relative link");
        Check(ManualLibrary.ResolveLink(articles, source, "ExporterSetup.md#first")?.source == "Docs/zh-CN/ExporterSetup.md", "Anchor link locates document");
        Check(articles.Any(a => a.Matches("_SingleChannel")) && !source.Matches("UNLIKELY_SEARCH_TOKEN_391"), "Search examines full body");
        Console.WriteLine($"PASS: {assertions} assertions; {articles.Count} offline articles.");
    }
    public sealed class Discovered : ExportProcessor { public static int Count; public Discovered() { Count++; } }
}
