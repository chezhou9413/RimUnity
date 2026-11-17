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
            var modeField = settingsType.GetField("mapHdrMode", members);
            if (modeField == null) { Console.WriteLine("SKIP map HDR mode regression: supplied runtime predates MapHdrMode."); return; }
            var modeType = runtime.GetType("RimUnity.Rendering.MapHdrMode", true);
            object Mode(string name) => Enum.Parse(modeType, name);
            var settings = Activator.CreateInstance(settingsType);
            var settingsProperty = runtime.GetType("RimUnity.RimUnityMod", true).GetProperty("Bloom", members);
            var previousSettings = settingsProperty.GetValue(null);
            var hooks = runtime.GetType("RimUnity.Rendering.HdrMapHooks", true);
            var capture = hooks.GetNestedType("Capture", BindingFlags.NonPublic).GetMethod("Postfix", members);
            var release = hooks.GetNestedType("ReleaseFrame", BindingFlags.NonPublic).GetMethod("Prefix", members);
            var frames = hooks.GetField("Frames", members).GetValue(null);
            var lightFrames = hooks.GetField("LightFrames", members).GetValue(null);
            var lookup = frames.GetType().GetMethod("TryGetValue");
            var lightLookup = lightFrames.GetType().GetMethod("TryGetValue");
            var needed = runtime.GetType("RimUnity.Rendering.HdrDrawFrame", true).GetMethod("Needed", members);
            var lightNeeded = runtime.GetType("RimUnity.Rendering.HdrLightFrame", true).GetMethod("Needed", members);
            try
            {
                settingsProperty.SetValue(null, settings);
                settingsType.GetField("enabled", members).SetValue(settings, true);
                settingsType.GetField("layerMask", members).SetValue(settings, 1 << 29);
                if (!Equals(modeField.GetValue(settings), Mode("Experiment"))) throw new Exception("Map HDR must default to Experiment.");
                foreach (var (modeName, expectLegacy, expectLight) in new[] {
                    ("None", false, false),
                    ("Legacy", true, false),
                    ("Experiment", false, true),
                    ("None", false, false),
                })
                {
                    modeField.SetValue(settings, Mode(modeName));
                    var identity = Matrix4x4.Identity;
                    using var map = new MeshDrawFrame(++frame, new CameraView(identity, identity, identity, -1),
                        MeshDrawFrame.RentList(), MeshDrawFrame.RentList(), Vector3.Zero);
                    fixture.Add(map, 1); // Real registered Linear shaders, not an empty-frame false positive.
                    fixture.AddAdditive(map); // Experiment only captures additive blends.
                    if (!(bool)needed.Invoke(null, new object[] { map })) throw new Exception("Fixture does not require Legacy HDR.");
                    if (!(bool)lightNeeded.Invoke(null, new object[] { map })) throw new Exception("Fixture does not require Experiment HDR.");
                    try
                    {
                        capture.Invoke(null, new object[] { map });
                        object[] legacyArgs = { map, null };
                        object[] lightArgs = { map, null };
                        bool capturedLegacy = (bool)lookup.Invoke(frames, legacyArgs);
                        bool capturedLight = (bool)lightLookup.Invoke(lightFrames, lightArgs);
                        if (capturedLegacy != expectLegacy) throw new Exception($"Legacy capture mismatch for {modeName}.");
                        if (capturedLight != expectLight) throw new Exception($"Experiment capture mismatch for {modeName}.");
                        object bloomFrame = settingsType.GetMethod("Snapshot", members).Invoke(settings, null);
                        using (var dispose = (IDisposable)bloomFrame)
                        {
                            int mask = (int)bloomFrame.GetType().GetProperty("LayerMask", members).GetValue(bloomFrame);
                            if (mask != 1 << 29) throw new Exception("Map HDR mode changed independent Bloom layers.");
                        }
                        checks++; Console.WriteLine($"PASS map HDR mode={modeName}; legacy={capturedLegacy}; experiment={capturedLight}; independent Bloom mask retained");
                    }
                    finally { release.Invoke(null, new object[] { map }); }
                }
                settingsType.GetMethod("ResetDefaults", members).Invoke(settings, null);
                if (!Equals(modeField.GetValue(settings), Mode("Experiment"))) throw new Exception("Reset must set map HDR to Experiment.");

                settingsType.GetMethod("SetLayerEnabled", members).Invoke(settings, new object[] { 29, true });
                settingsType.GetField("enabled", members).SetValue(settings, false);
                object controlled = settingsType.GetMethod("Snapshot", members).Invoke(settings, null);
                using (var dispose = (IDisposable)controlled)
                {
                    int mask = (int)controlled.GetType().GetProperty("LayerMask", members).GetValue(controlled);
                    if (mask != 1 << 29) throw new Exception("Controlled bloom layer must remain when manual switch is off.");
                }
                settingsType.GetMethod("ReleaseLayer", members).Invoke(settings, new object[] { 29 });
                string describe = (string)settingsType.GetMethod("Describe", members).Invoke(settings, null);
                if (describe == null || !describe.Contains("mapHdr=Experiment") || !describe.Contains("effective=0x"))
                    throw new Exception("Bloom Describe must expose mapHdr mode and effective mask.");
                checks++; Console.WriteLine("PASS controlled bloom layer survives manual switch and Describe exposes mapHdr mode");
            }
            finally { settingsProperty.SetValue(null, previousSettings); }
        }
    }
}
