using System.Numerics;
using System.Reflection;
using Waylith.Rendering;

internal static partial class Program
{
    private sealed partial class GpuRunner
    {
        private void CheckMapHdrSetting(Assembly runtime, RuntimeGeometry fixture)
        {
            const BindingFlags members = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
            var settingsType = runtime.GetType("RimUnity.Rendering.BloomSettings", true);
            var hdrFlag = settingsType.GetField("mapHdrEnabled", members);
            if (hdrFlag == null) { Console.WriteLine("SKIP map HDR switch regression: supplied runtime predates the switch."); return; }
            var settings = Activator.CreateInstance(settingsType);
            var settingsProperty = runtime.GetType("RimUnity.RimUnityMod", true).GetProperty("Bloom", members);
            var previousSettings = settingsProperty.GetValue(null);
            var hooks = runtime.GetType("RimUnity.Rendering.HdrMapHooks", true);
            var capture = hooks.GetNestedType("Capture", BindingFlags.NonPublic).GetMethod("Postfix", members);
            var release = hooks.GetNestedType("ReleaseFrame", BindingFlags.NonPublic).GetMethod("Prefix", members);
            var frames = hooks.GetField("Frames", members).GetValue(null);
            var lookup = frames.GetType().GetMethod("TryGetValue");
            var needed = runtime.GetType("RimUnity.Rendering.HdrDrawFrame", true).GetMethod("Needed", members);
            try
            {
                settingsProperty.SetValue(null, settings);
                settingsType.GetField("enabled", members).SetValue(settings, true);
                settingsType.GetField("layerMask", members).SetValue(settings, 1 << 29);
                if ((bool)hdrFlag.GetValue(settings)) throw new Exception("Map HDR must default off.");
                foreach (bool enabled in new[] { false, true, false })
                {
                    hdrFlag.SetValue(settings, enabled);
                    var identity = Matrix4x4.Identity;
                    using var map = new MeshDrawFrame(++frame, new CameraView(identity, identity, identity, -1),
                        MeshDrawFrame.RentList(), MeshDrawFrame.RentList(), Vector3.Zero);
                    fixture.Add(map, 1); // Real registered Linear shaders, not an empty-frame false positive.
                    if (!(bool)needed.Invoke(null, new object[] { map })) throw new Exception("Fixture does not require HDR.");
                    try
                    {
                        capture.Invoke(null, new object[] { map });
                        object[] arguments = { map, null };
                        bool captured = (bool)lookup.Invoke(frames, arguments);
                        if (captured != enabled) throw new Exception("Map HDR capture does not follow its setting.");
                        object bloomFrame = settingsType.GetMethod("Snapshot", members).Invoke(settings, null);
                        using (var dispose = (IDisposable)bloomFrame)
                        {
                            int mask = (int)bloomFrame.GetType().GetProperty("LayerMask", members).GetValue(bloomFrame);
                            if (mask != 1 << 29) throw new Exception("Map HDR toggle changed independent Bloom layers.");
                        }
                        checks++; Console.WriteLine($"PASS map HDR switch enabled={enabled}; independent Bloom mask retained");
                    }
                    finally { release.Invoke(null, new object[] { map }); }
                }
                settingsType.GetMethod("ResetDefaults", members).Invoke(settings, null);
                if ((bool)hdrFlag.GetValue(settings)) throw new Exception("Reset must disable map HDR.");
            }
            finally { settingsProperty.SetValue(null, previousSettings); }
        }
    }
}
