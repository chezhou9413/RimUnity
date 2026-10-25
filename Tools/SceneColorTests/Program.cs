using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Waylith;
using Waylith.Rendering;
using Waylith.Rendering.PostFx;
using RimUnity.Rendering;

namespace RimUnity { internal static class RimUnityMod { internal static string ShaderDirectory; } }

internal static partial class Program
{
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern bool SetDllDirectory(string path);
    private static string root, shaders, scratch, runtimeAssembly, harmonyPath;
    private static int checks;
    private static readonly Vector3 Display = new(.25f, .5f, .75f);
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--audit") return ArtifactAudit.Run(args.Skip(1).ToArray());
        if (args.Length < 3) throw new ArgumentException("Usage: <game root> <shader directory> <scratch directory>");
        root = Path.GetFullPath(args[0]); shaders = Path.GetFullPath(args[1]); scratch = Path.GetFullPath(args[2]);
        runtimeAssembly = args.Length > 3 ? Path.GetFullPath(args[3]) : null;
        harmonyPath = args.Length > 4 ? Path.GetFullPath(args[4]) : null;
        Directory.CreateDirectory(scratch);
        AssemblyLoadContext.Default.Resolving += (_, name) => {
            if (name.Name == "0Harmony" && harmonyPath != null) return AssemblyLoadContext.Default.LoadFromAssemblyPath(harmonyPath);
            if (runtimeAssembly != null)
            {
                string runtimePath = Path.Combine(Path.GetDirectoryName(runtimeAssembly), name.Name + ".dll");
                if (File.Exists(runtimePath)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(runtimePath);
            }
            string path = Path.Combine(root, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        SetDllDirectory(root);
        try { return Run(); } catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { SetDllDirectory(null); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run()
    {
        RimUnity.RimUnityMod.ShaderDirectory = shaders;
        // A hidden graphics window is required: Headless disables Vulkan entirely.
        using var app = new EngineApplication(new EngineApplicationOptions {
            Title = "RimUnity color regression", HideWindow = true, ShowCrashDialog = false,
            InitialWindowSize = new Brutal.Numerics.int2(64, 64), EnableVideoDecode = false
        });
        using var runner = new GpuRunner(app);
        runner.CheckTransferSequences();
        runner.CheckFrameSlots();
        runner.CheckMapColorChains();
        runner.CheckRuntimeTargets();
        runner.CheckFormats();
        Console.WriteLine($"PASS {checks} real Vulkan pixel checks; hidden window, no game/save loaded.");
        return 0;
    }

    private static Vector3 Decode(Vector3 c) => new(D(c.X), D(c.Y), D(c.Z));
    private static Vector3 Encode(Vector3 c) => new(E(c.X), E(c.Y), E(c.Z));
    private static float D(float c) => c <= .04045f ? c / 12.92f : MathF.Pow((c + .055f) / 1.055f, 2.4f);
    private static float E(float c) => c <= .0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1 / 2.4f) - .055f;
    private static Vector3 Grade(Vector3 c, float saturation)
    {
        float luma = Vector3.Dot(c, new(.22f, .707f, .071f));
        return Vector3.Clamp(Vector3.Lerp(new(luma), c, saturation), Vector3.Zero, Vector3.One);
    }
    private static void Check(string name, Vector3 actual, Vector3 expected, float tolerance = .008f)
    {
        if (!float.IsFinite(actual.X + actual.Y + actual.Z) || Vector3.Distance(actual, expected) > tolerance)
            throw new Exception($"{name}: actual={actual}, expected={expected}");
        checks++; Console.WriteLine($"PASS {name}: {actual}");
    }

    private sealed partial class GpuRunner : IDisposable
    {
        private readonly EngineApplication app;
        private readonly VkCommandPool pool;
        private readonly CommandBuffer command;
        private readonly VkFence fence;
        private readonly TestRenderContext context;
        private readonly RenderTexture anchor;
        private readonly VkRenderPass anchorPass;
        private readonly List<IDisposable> owned = new();
        private int frame;
        private readonly string constantFile;
        internal GpuRunner(EngineApplication app)
        {
            this.app = app;
            var g = app.GraphicsDevice; var device = g.Device;
            pool = device.CreateCommandPool(new VkCommandPoolCreateInfo {
                QueueFamilyIndex = g.GraphicsFamilyIndex, Flags = VkCommandPoolCreateFlags.ResetCommandBufferBit
            }, device.VkAllocator);
            command = device.AllocateCommandBuffer(new VkCommandBufferAllocateInfo { CommandPool = pool, Level = VkCommandBufferLevel.Primary });
            fence = device.CreateFence(new VkFenceCreateInfo(), device.VkAllocator);
            context = new TestRenderContext(g, pool);
            anchor = Target(VkFormat.R16G16B16A16SFloat);
            anchorPass = PostFxPassHelpers.CreateColorRenderPass(device, anchor.ColorFormat);
            context.SetTarget(anchor, anchorPass);
            constantFile = Path.Combine(scratch, "constant.frag.glsl");
            File.WriteAllText(constantFile, "#version 450\nlayout(location=0) out vec4 c; layout(push_constant) uniform Settings { vec4 value; } s; void main(){ c=s.value; }");
        }
        private RenderTexture Target(VkFormat format)
        {
            var t = new RenderTexture(8, 8, format, name: "ColorTest." + owned.Count);
            owned.Add(t); return t;
        }
        private PostEffectPass Pass(string file, int inputs)
        {
            var p = new PostEffectPass("ColorTest." + owned.Count, file,
                Enumerable.Range(0, inputs).ToArray(), pushConstantSize: 16);
            owned.Add(p); return p;
        }
        private void Submit(Action<FrameContext> record)
        {
            context.Slot = 0;
            var ctx = new FrameContext(context, context.FrameResources[0], command, anchorPass,
                anchor.GetFramebuffer(anchorPass), anchor.Extent, VkSampleCountFlags._1Bit, anchor.ColorFormat, VkFormat.Undefined);
            command.Begin(VkCommandBufferUsageFlags.OneTimeSubmitBit);
            record(ctx); command.End();
            var device = app.GraphicsDevice.Device;
            device.ResetFences(new ReadOnlySpan<VkFence>(in fence));
            var submit = command;
            app.GraphicsDevice.SubmitGraphics(default, default, new Span<CommandBuffer>(ref submit), default, fence);
            device.WaitForFence(fence, -1);
        }
        private RenderTexture Constant(Vector3 color, VkFormat format = VkFormat.R16G16B16A16SFloat)
        {
            var target = Target(format); var pass = Pass(constantFile, 0);
            pass.SetPushConstants(new Vector4(color, 1)); pass.Output = target;
            Submit(ctx => pass.Record(in ctx, null)); return target;
        }
        private RenderTexture Transfer(RenderTexture source, float conversion, VkFormat format = VkFormat.R16G16B16A16SFloat)
        {
            var target = Target(format); var pass = new HdrTransferPass("ColorTest.Transfer." + owned.Count);
            owned.Add(pass); int number = ++frame;
            Submit(ctx => pass.Copy(in ctx, number, source, target, conversion)); return target;
        }
        private RenderTexture Shader(string name, RenderTexture first, RenderTexture second, Vector4 settings)
        {
            var target = Target(VkFormat.R16G16B16A16SFloat);
            var pass = Pass(Path.Combine(shaders, name + ".frag.glsl"), second == null ? 1 : 2);
            pass.SetInput(0, first); if (second != null) pass.SetInput(1, second);
            pass.SetPushConstants(settings); pass.Output = target;
            Submit(ctx => pass.Record(in ctx, null)); return target;
        }
        private RenderTexture NativeComposite(RenderTexture source)
        {
            var target = Target(VkFormat.R16G16B16A16SFloat);
            var pass = new PostEffectPass("ColorTest.Native." + owned.Count,
                Path.Combine(root, "Rendering/Shaders/PostFX/PostFxComposite.frag.glsl"), new[] { 0 }, uniformBinding: 1);
            owned.Add(pass); pass.SetInput(0, source); pass.SetUniforms(1f); pass.Output = target;
            Submit(ctx => pass.Record(in ctx, null)); return target;
        }
        private static Vector3 Read(RenderTexture texture)
        {
            var bytes = GpuTextureService.ReadbackTexture2D(texture.Color, 8, 8, 8) ?? throw new Exception("GPU readback failed");
            int center = (4 * 8 + 4) * 8;
            float H(int offset) => (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(bytes, center + offset));
            return new(H(0), H(2), H(4));
        }
        internal void CheckFormats()
        {
            var zero = Constant(Vector3.Zero); var bloom = Constant(new(.04f, .02f, .01f));
            foreach (var format in SceneFormats)
            {
                var host = Constant(Display, format);
                var sampled = Transfer(host, 0);
                var reference = Read(sampled);
                var linear = Transfer(host, 1);
                Check(format + " decode", Read(linear), Decode(reference));
                var restored = Transfer(linear, -1, format);
                Check(format + " native roundtrip", Read(Transfer(restored, 0)), reference);
                Check(format + " host composite before/after HDR", Read(NativeComposite(restored)), Read(NativeComposite(host)));
                if (format is not (VkFormat.R8G8B8A8UNorm or VkFormat.B8G8R8A8UNorm))
                {
                    // Negative control: emulate the old float/sRGB/10-bit classification
                    // on an already drawn host background, then run the real HDR shader.
                    var wrong = Read(Shader("HdrComposite", sampled, zero, new(0, 1, 0, 0)));
                    Check(format + " old inference reproduces extra encoding", wrong, Encode(reference));
                    if (Vector3.Distance(wrong, reference) < .1f) throw new Exception("Negative control failed to reproduce brightening");
                }
                foreach (float saturation in new[] { 0f, 1f, 1.4f })
                {
                    Check(format + " HDR zero bloom sat=" + saturation,
                        Read(Shader("HdrComposite", linear, zero, new(0, saturation, 0, 0))), Grade(reference, saturation));
                    var expected = Grade(Encode(Decode(reference) + new Vector3(.04f, .02f, .01f) * .6f), saturation);
                    Check(format + " HDR bloom sat=" + saturation,
                        Read(Shader("HdrComposite", linear, bloom, new(.6f, saturation, 0, 0))), expected);
                    Check(format + " ScreenEfx linear bloom sat=" + saturation,
                        Read(Shader("ScreenEfxScene", linear, bloom, new(.6f, 1, saturation, 1))), expected);
                    var delta = Encode(Decode(reference) + new Vector3(.04f, .02f, .01f) * .6f) - reference;
                    float deltaLuma = Vector3.Dot(delta, new(.2126f, .7152f, .0722f));
                    var nativeExpected = Vector3.Clamp(Grade(reference, saturation)
                        + Vector3.Lerp(new(deltaLuma), delta, saturation), Vector3.Zero, Vector3.One);
                    Check(format + " ScreenEfx native bloom sat=" + saturation,
                        Read(Shader("ScreenEfxScene", host, bloom, new(.6f, 1, saturation, 0))), nativeExpected);
                }
                Check(format + " ScreenEfx native no bloom",
                    Read(Shader("ScreenEfxScene", host, zero, new(0, 1, 1, 0))), reference);
            }
            var bright = Constant(new(2, 1, .5f));
            var pre = Shader("BloomPrefilter", bright, null, new(.8f, .5f, 0, 0));
            Check("Bloom threshold", Read(pre), new(1.2f, .6f, .3f));
            var down = Shader("BloomDown", pre, null, Vector4.Zero);
            Check("Bloom down constant energy", Read(down), Read(pre));
            var up = Shader("BloomUp", pre, down, new(.65f, 0, 0, 0));
            Check("Bloom up constant energy", Read(up), Read(pre));
        }
        public void Dispose()
        {
            // All submissions have completed their dedicated fence before disposal.
            for (int i = owned.Count - 1; i >= 0; i--) owned[i].Dispose();
            var device = app.GraphicsDevice.Device;
            device.DestroyRenderPass(anchorPass, device.VkAllocator);
            device.DestroyFence(fence, device.VkAllocator);
            device.DestroyCommandPool(pool, device.VkAllocator);
        }
    }
}
