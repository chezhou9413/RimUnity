using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Waylith;
using Waylith.Graphics;
using Waylith.Graphics.Meshes;
using RimUnity.ScreenEfx;

//Exercise the real first-use geometry factory. Only the GPU upload boundary is substituted.
internal static class FullscreenUploadTests
{
    private static Mesh uploaded;
    private static bool ready, fail;
    private static int uploads;
    private static GraphicsDevice graphics;
    private static MeshUploader uploader;

    internal static void Run(Action<bool, string> check)
    {
        RimUnity.Runtime.NativeRuntime.InitializeOnCurrentThread();
        var harmony = new Harmony("RimUnity.Tests.FullscreenUpload");
        var instance = typeof(EngineApplication).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
        var previous = instance.GetValue(null);
        graphics = (GraphicsDevice)RuntimeHelpers.GetUninitializedObject(typeof(GraphicsDevice));
        uploader = (MeshUploader)RuntimeHelpers.GetUninitializedObject(typeof(MeshUploader));
        var app = (EngineApplication)RuntimeHelpers.GetUninitializedObject(typeof(EngineApplication));
        instance.SetValue(null, app);
        try
        {
            Patch(AccessTools.PropertyGetter(typeof(EngineApplication), "GraphicsDevice"), nameof(Graphics));
            Patch(AccessTools.PropertyGetter(typeof(GraphicsDevice), "MeshUploads"), nameof(Uploader));
            Patch(AccessTools.Method(typeof(MeshUploader), "Upload"), nameof(Upload));
            Patch(AccessTools.PropertyGetter(typeof(GpuMeshResource), "HasBuffers"), nameof(Buffers));
            var create = AccessTools.Method(typeof(EfxFrameBuilder), "MakeTriangle");
            var mesh = (RimUnity.Mesh)create.Invoke(null, null);
            try
            {
                check(uploads == 1 && ReferenceEquals(mesh.EngineMesh, uploaded) && uploaded.GpuResource.HasBuffers,
                    "first fullscreen mesh is explicitly uploaded before capture");
                check(uploaded.VertexCount == 3 && uploaded.IndexCount == 3 && uploaded.SubMeshCount == 1,
                    "uploader receives a flushed fullscreen triangle");
                check(mesh.vertices[1].x == 3 && mesh.uv[2].y == -1,
                    "fullscreen triangle uses Unity bottom-origin UVs for exported samplers");
                //Positive Vulkan viewports map clip Y=-1 to the top. The exported sampler
                //then applies 1-V. Test both edges and a two-pass chain for parity mistakes.
                var uv = mesh.uv;
                float SampleV(float nativeY) => 1f - (uv[0].y + (uv[2].y - uv[0].y) * nativeY * .5f);
                check(SampleV(0) == 0 && SampleV(1) == 1 && SampleV(SampleV(.25f)) == .25f,
                    "exported sampling preserves top/bottom and two-pass orientation");
                AccessTools.Field(typeof(EfxFrameBuilder), "triangle").SetValue(null, mesh);
                EfxFrameBuilder.Shutdown(); EfxFrameBuilder.Shutdown();
                check(AccessTools.PropertyGetter(typeof(Mesh), "IsDisposed").Invoke(uploaded, null) is true,
                    "device shutdown releases the shared mesh immediately without another frame");
            }
            finally { RimUnity.Object.Destroy(mesh); }

            fail = true;
            try { create.Invoke(null, null); throw new Exception("upload failure was ignored"); }
            catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { }
            RimUnity.Runtime.NativeRuntime.BeginFrame(0);
            RimUnity.Runtime.NativeRuntime.EndFrame();
            check(AccessTools.PropertyGetter(typeof(Mesh), "IsDisposed").Invoke(uploaded, null) is true,
                "failed first-use upload releases the partially built mesh");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id); instance.SetValue(null, previous);
            graphics = null; uploader = null; uploaded = null; ready = fail = false;
        }
        void Patch(MethodBase target, string prefix) => harmony.Patch(target, prefix: new HarmonyMethod(typeof(FullscreenUploadTests), prefix));
    }

    private static bool Graphics(ref GraphicsDevice __result) { __result = graphics; return false; }
    private static bool Uploader(ref MeshUploader __result) { __result = uploader; return false; }
    private static bool Upload(Mesh mesh)
    {
        uploads++; uploaded = mesh;
        if (fail) throw new InvalidOperationException("test upload failure");
        ready = true; return false;
    }
    private static bool Buffers(GpuMeshResource __instance, ref bool __result)
    {
        if (uploaded == null || !ReferenceEquals(uploaded.GpuResource, __instance)) return true;
        __result = ready; return false;
    }
}
