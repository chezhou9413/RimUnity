using System.Numerics;
using System.Reflection;
using Brutal.VulkanApi;
using Waylith.Rendering;

internal static partial class Program
{
    private sealed partial class GpuRunner
    {
        internal void CheckRuntimeTargets()
        {
            if (runtimeAssembly == null) { Console.WriteLine("SKIP actual runtime target test: no runtime assembly supplied."); return; }
            Assembly.LoadFrom(Path.Combine(root, "RimWorld.dll"));
            var runtime = Assembly.LoadFrom(runtimeAssembly);
            ShaderSourceLoader.RegisterShaderSourceDirectory(Path.Combine(root, "Rendering", "Shaders"));
            runtime.GetType("RimUnity.RimUnityMod", true).GetProperty("ShaderDirectory", BindingFlags.Static | BindingFlags.NonPublic)
                .SetValue(null, shaders);
            var targetType = runtime.GetType("RimUnity.Rendering.HdrDrawTarget", true);
            var snapshotType = runtime.GetType("RimUnity.Rendering.HdrDrawFrame", true);
            const BindingFlags instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            using var fixture = new RuntimeGeometry(app, runtime, scratch);
            CheckMapHdrSetting(runtime, fixture);
            foreach (var format in SceneFormats)
            {
                var host = Pattern(format);
                var reference = ReadPixels(Transfer(host, 0));
                var nativeReference = ReadPixels(NativeComposite(host));
                using var target = (IDisposable)Activator.CreateInstance(targetType, instance, null, new object[] { "ColorTest.Runtime." + format }, null);
                // Empty, fully transparent and visibly blended geometry isolate
                // preservation and prove the real mesh pipeline actually executes.
                for (int repeat = 0; repeat < 3; repeat++)
                {
                    // Reset the original background: a visible positive control must
                    // not become the input of a later transparent-only comparison.
                    host = Pattern(format);
                    int number = ++frame;
                    var identity = Matrix4x4.Identity;
                    using var map = new MeshDrawFrame(number, new CameraView(identity, identity, identity, -1),
                        MeshDrawFrame.RentList(), MeshDrawFrame.RentList(), Vector3.Zero);
                    fixture.Add(map, repeat);
                    using var snapshot = (IDisposable)Activator.CreateInstance(snapshotType, instance, null, new object[] { map, -1, false }, null);
                    RenderTexture linearResult = null;
                    Submit(ctx => {
                        Call("Begin", ctx, host, number, false);
                        Call("BindDepth", ctx, host);
                        // Two queue slices exercise repeated WriteScene with one pass.
                        Call("Record", ctx, host, snapshot, int.MinValue, 3000);
                        Call("WriteScene", ctx, host, number);
                        Call("Record", ctx, host, snapshot, 3000, int.MaxValue);
                        Call("WriteScene", ctx, host, number);
                        linearResult = (RenderTexture)targetType.GetProperty("Result", instance).GetValue(target);
                    });
                    var expectedLinear = reference.Select(c => repeat == 2 ? Vector3.Lerp(Decode(c), RuntimeGeometry.VisibleLinear, .5f) : Decode(c)).ToArray();
                    var expectedDisplay = repeat == 2 ? expectedLinear.Select(Encode).ToArray() : reference;
                    CheckImage($"actual HdrDrawTarget {format} mode={repeat} linear", linearResult, expectedLinear);
                    CheckImage($"actual HdrDrawTarget {format} mode={repeat} writeback", Transfer(host, 0), expectedDisplay);
                    CheckImage($"actual HdrDrawTarget {format} mode={repeat} final native", NativeComposite(host), repeat == 2 ? expectedDisplay : nativeReference);

                    void Call(string method, params object[] arguments)
                    {
                        try { targetType.GetMethod(method, instance).Invoke(target, arguments); }
                        catch (TargetInvocationException error) { throw new InvalidOperationException("Runtime HDR " + method + " failed", error.InnerException); }
                    }
                }
            }
            Console.WriteLine("RUNTIME " + runtimeAssembly);
        }

        private RenderTexture Pattern(VkFormat format)
        {
            string file = Path.Combine(scratch, "pattern.frag.glsl");
            if (!File.Exists(file)) File.WriteAllText(file, "#version 450\nlayout(location=0) out vec4 c; void main(){"
                + "vec3 colors[8]=vec3[](vec3(0),vec3(.003),vec3(.03),vec3(.18),vec3(.5),vec3(.9),vec3(.25,.5,.75),vec3(.9,.3,.05));"
                + "c=vec4(colors[(int(gl_FragCoord.x)+int(gl_FragCoord.y))%8],1);}");
            var result = Target(format); var pass = Pass(file, 0); pass.Output = result;
            Submit(ctx => pass.Record(in ctx, null));
            return result;
        }

        private static Vector3[] ReadPixels(RenderTexture texture)
        {
            var bytes = GpuTextureService.ReadbackTexture2D(texture.Color, 8, 8, 8) ?? throw new Exception("GPU readback failed");
            return Enumerable.Range(0, 64).Select(pixel => {
                float H(int offset) => (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(bytes, pixel * 8 + offset));
                return new Vector3(H(0), H(2), H(4));
            }).ToArray();
        }

        private static void CheckImage(string label, RenderTexture texture, Vector3[] expected)
        {
            var actual = ReadPixels(texture);
            for (int i = 0; i < actual.Length; i++)
                if (!float.IsFinite(actual[i].X + actual[i].Y + actual[i].Z) || Vector3.Distance(actual[i], expected[i]) > .008f)
                    throw new Exception($"{label} pixel={i}: actual={actual[i]}, expected={expected[i]}");
            checks++; Console.WriteLine("PASS " + label + ": 64 gray/color pixels");
        }
    }
}
