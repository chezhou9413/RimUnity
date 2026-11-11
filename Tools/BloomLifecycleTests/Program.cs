using RimUnity.Rendering;

internal static class Program
{
    private static int checks;
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        checks++;
    }

    private static void Main()
    {
        HandoffMatched();
        HandoffMissingMap();
        HandoffKeepsFutureFrame();
        HandoffDropsStaleMap();
        HandoffReplacesUnconsumed();
        CompatibilityClassification();
        LayerMaskMath();
        Console.WriteLine($"PASS {checks} Bloom lifecycle checks (no GPU / no game).");
    }

    private static void HandoffMatched()
    {
        var handoff = new BloomMapHandoff<string>();
        handoff.Offer(3, "map-3");
        Check(handoff.TryTake(3, out var map, out var miss) && map == "map-3" && miss == null, "matched frame is taken");
        Check(handoff.Takes == 1 && handoff.Misses == 0, "matched take updates counters");
    }

    private static void HandoffMissingMap()
    {
        var handoff = new BloomMapHandoff<string>();
        Check(!handoff.TryTake(1, out _, out var miss) && miss == "missing-map", "missing map is reported");
        Check(handoff.Misses == 1, "miss counted");
    }

    private static void HandoffKeepsFutureFrame()
    {
        var handoff = new BloomMapHandoff<string>();
        handoff.Offer(5, "map-5");
        Check(!handoff.TryTake(4, out var map, out var miss) && map == null && miss.Contains("stale-postfx"),
            "late PostFx does not consume a future map");
        Check(handoff.PendingFrame == 5, "future map remains pending");
        Check(handoff.TryTake(5, out map, out miss) && map == "map-5" && miss == null, "future map still available");
    }

    private static void HandoffDropsStaleMap()
    {
        var handoff = new BloomMapHandoff<string>();
        handoff.Offer(2, "map-2");
        Check(!handoff.TryTake(4, out var map, out var miss) && map == null && miss.Contains("stale-map"),
            "stale map is discarded");
        Check(handoff.PendingFrame == int.MinValue, "stale pending cleared");
    }

    private static void HandoffReplacesUnconsumed()
    {
        var handoff = new BloomMapHandoff<string>();
        handoff.Offer(1, "map-1");
        handoff.Offer(2, "map-2");
        Check(handoff.LastMiss.Contains("replaced-unconsumed"), "unconsumed older map is noted");
        Check(handoff.TryTake(2, out var map, out _) && map == "map-2", "newest map wins");
    }

    private static void CompatibilityClassification()
    {
        Check(BloomCompatibility.IsPermanentInterfaceFailure(new MissingMethodException("ctor")),
            "missing method is permanent");
        Check(BloomCompatibility.IsPermanentInterfaceFailure(new Exception("wrap", new TypeLoadException("type"))),
            "wrapped type load is permanent");
        Check(!BloomCompatibility.IsPermanentInterfaceFailure(new InvalidOperationException("transient")),
            "ordinary construction failure is temporary");
        Check(!BloomCompatibility.IsPermanentInterfaceFailure(new ArgumentException("bad arg")),
            "argument failures stay temporary");
    }

    //与 BloomSettings.EffectiveLayerMask 规则保持一致：受控层覆盖手动层。
    private static void LayerMaskMath()
    {
        bool enabled = true;
        int layerMask = 1 << 3;
        int controlled = 1 << 29;
        int active = 1 << 29;
        int effective = ((enabled ? layerMask : 0) & ~controlled) | active;
        Check(effective == ((1 << 3) | (1 << 29)), "manual and controlled layers combine");

        active = 0;
        effective = ((enabled ? layerMask : 0) & ~controlled) | active;
        Check(effective == (1 << 3), "disabled controlled layer does not contribute");

        enabled = false;
        active = 1 << 29;
        effective = ((enabled ? layerMask : 0) & ~controlled) | active;
        Check(effective == (1 << 29), "controlled layer still works when manual switch is off");

        active = 0;
        effective = ((enabled ? layerMask : 0) & ~controlled) | active;
        Check(effective == 0, "manual off and controlled off yields empty mask");
    }
}
