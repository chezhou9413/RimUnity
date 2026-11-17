using System.Numerics;
using System.Reflection;
using Brutal.VulkanApi;
using Waylith.Rendering;

internal static partial class Program
{
    private sealed partial class GpuRunner
    {
        private void CheckExperimentMapSlices(Assembly runtime, RuntimeGeometry geometry)
        {
            const BindingFlags members = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
            const string patchId = "RimUnity.SceneColorTests.ExperimentMapSlices";
            const VkFormat color = VkFormat.R16G16B16A16SFloat;
            const VkFormat depth = VkFormat.D32SFloatS8UInt;
            var game = Assembly.Load("RimWorld");
            var sceneType = game.GetType("RimWorldWaylith.Rendering.SceneColorTargets", true);
            using var scenes = (IDisposable)Activator.CreateInstance(sceneType, app.GraphicsDevice, color, depth, "ColorTest.MapSlices");
            sceneType.GetMethod("EnsureSized", members).Invoke(scenes, new object[] { context.FramesInFlight, 8, 8 });
            var scene = (RenderTexture)sceneType.GetMethod("Current", members).Invoke(scenes, new object[] { 0 });
            using var grabs = new SceneGrabTargets(color);
            grabs.SetExtent(8, 8);
            var grab = (SceneGrabPoint)Activator.CreateInstance(typeof(SceneGrabPoint), members, null,
                new object[] { "ColorTest", 3000 }, null);
            var getGrabbed = typeof(SceneGrabTargets).GetMethod("GetOrCreateTarget", members);
            var mapType = game.GetType("RimWorldWaylith.Rendering.MapRenderFeature+MapMainPass", true);
            var host = new MeshBatchRenderer(app.GraphicsDevice, context);
            var water = new MeshBatchRenderer(app.GraphicsDevice, context);
            using var mapPass = (IDisposable)Activator.CreateInstance(mapType, host, water, null, scenes, grabs, null);
            var recordScene = mapType.GetMethod("RecordScene", members);

            var hooks = runtime.GetType("RimUnity.Rendering.HdrMapHooks", true);
            MethodInfo Hook(string type, string method) => hooks.GetNestedType(type, members).GetMethod(method, members);
            var settingsType = runtime.GetType("RimUnity.Rendering.BloomSettings", true);
            var settings = Activator.CreateInstance(settingsType);
            var mode = settingsType.GetField("mapHdrMode", members);
            var bloom = runtime.GetType("RimUnity.RimUnityMod", true).GetProperty("Bloom", members);
            var previous = bloom.GetValue(null);
            var harmonyType = Assembly.Load("0Harmony").GetType("HarmonyLib.Harmony", true);
            var harmony = Activator.CreateInstance(harmonyType, patchId);
            var processor = harmonyType.GetMethod("CreateClassProcessor", new[] { typeof(Type) })
                .Invoke(harmony, new object[] { hooks.GetNestedType("Slice", members) });
            try
            {
                processor.GetType().GetMethod("Patch", Type.EmptyTypes).Invoke(processor, null);
                bloom.SetValue(null, settings);
                foreach (var (modeName, lightBeforeGrab) in new[] { ("Experiment", false), ("Experiment", true), ("None", false) })
                {
                    mode.SetValue(settings, Enum.Parse(mode.FieldType, modeName));
                    var identity = Matrix4x4.Identity;
                    using var map = new MeshDrawFrame(++frame, new CameraView(identity, identity, identity, -1),
                        MeshDrawFrame.RentList(), MeshDrawFrame.RentList(), Vector3.UnitY)
                    { BackgroundColor = new Vector4(Display, 1), SceneGrabs = new[] { grab } };
                    geometry.AddShade(map, 1000);
                    int lightQueue = lightBeforeGrab ? 2000 : 4000;
                    geometry.AddAdditive(map, lightQueue);
                    geometry.AddShade(map, lightQueue + 100);
                    geometry.AddAdditive(map, lightQueue + 200);
                    if (lightBeforeGrab) geometry.AddShade(map, 5000);

                    Vector3 AddLight(Vector3 value) => modeName == "Experiment"
                        ? Encode(Decode(value) + RuntimeGeometry.VisibleLinear)
                        : value + Encode(RuntimeGeometry.VisibleLinear);
                    var lit = AddLight(AddLight(Display * .5f) * .5f);
                    Hook("Capture", "Postfix").Invoke(null, new object[] { map });
                    try
                    {
                        // Record the installed game's scene-grab slices and all HDR runs in one command buffer.
                        Submit(ctx => recordScene.Invoke(mapPass, new object[] { ctx, map }));
                        var grabbed = (RenderTexture)getGrabbed.Invoke(grabs, new object[] { grab });
                        // RecordGrab updates CurrentLayout; the host readback uses the per-mip layout.
                        grabbed.Color.SetMipLayout(0, grabbed.Color.CurrentLayout);
                        Check($"actual {modeName} scene grab with lightBeforeGrab={lightBeforeGrab}",
                            Read(grabbed), lightBeforeGrab ? lit : Display * .5f);
                        Check($"actual {modeName} map output with lightBeforeGrab={lightBeforeGrab}",
                            Read(NativeComposite(scene)), lightBeforeGrab ? lit * .5f : lit);
                    }
                    finally { Hook("ReleaseFrame", "Prefix").Invoke(null, new object[] { map }); }
                }
            }
            finally
            {
                Hook("Dispose", "Prefix").Invoke(null, new object[] { mapPass });
                harmonyType.GetMethod("UnpatchAll", new[] { typeof(string) }).Invoke(harmony, new object[] { patchId });
                bloom.SetValue(null, previous);
            }
        }
    }
}
