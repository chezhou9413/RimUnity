using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimUnity.Assets;
using RimUnity.Serialization;

namespace RimUnity.Diagnostics;

internal static partial class ParticleSpikeCapture
{
    private const string PatchId = "Chezhou.RimUnity.ParticleSpikeCapture";
    private static readonly Harmony ProbeHarmony = new(PatchId);
    private static readonly List<MethodBase> installed = new();
    private static readonly ConcurrentDictionary<MethodBase, Probe> probes = new();
    private sealed record Probe(string Stage, Func<object, object[], string> Detail, Func<object, object[], string> Notes = null);
    private static readonly FieldInfo CatalogsField = AccessTools.Field(typeof(AssetLoader), "Catalogs");
    private static readonly FieldInfo ResourcesField = AccessTools.Field(typeof(AssetCatalog), "resources");
    private static readonly FieldInfo RecordsField = AccessTools.Field(typeof(AssetCatalog), "records");
    // Published immutable method metadata is retained until the next installation, so in-flight finalizers can finish safely.
    private static void InstallProbes()
    {
        RemoveProbes(); probes.Clear();
        Add(typeof(AssetLoader), "GetCatalog", "Catalog", (_, a) => Clip(a[0] as string));
        Add(typeof(AssetCatalog), "Get", "Asset.Get", (o, a) =>
        {
            var c = (AssetCatalog)o;
            string id = (string)a[0];
            return c.Manifest.owner + " | " + (((IDictionary)RecordsField.GetValue(c))[id] is AssetRecord r ? r.type + " | " + r.path : id);
        }, (o, a) => "cache=" + (((IDictionary)ResourcesField.GetValue(o)).Contains(a[0]) ? "hit" : "miss"));
        Add(typeof(PrefabBuilder), "Template", "Prefab.Template", (_, a) => ((AssetCatalog)a[0]).Manifest.owner + " | " + PrefabName((PrefabData)a[1]));
        Add(typeof(PrefabBuilder), "Build", "Prefab.Build", (_, a) => ((AssetCatalog)a[0]).Manifest.owner + " | " + PrefabName((PrefabData)a[1]) + " | " + ((bool)a[2] ? "template" : "instance"),
            (_, a) => "nodes=" + ((PrefabData)a[1]).nodes.Count() + "; components=" + ((PrefabData)a[1]).nodes.Sum(n => n.components.Count()));
        Add(typeof(ResourceLoader), "Load", "Resource.Load", (_, a) => ((AssetCatalog)a[0]).Manifest.owner + " | " + ((AssetRecord)a[1]).type + " | " + ((AssetRecord)a[1]).path);
        Add(typeof(ValueReader), "Apply", "Prefab.BindMembers", (_, a) => a[0]?.GetType().FullName ?? "<null>");
        Add(typeof(Rendering.CustomShaderLoader), "Load", "Shader.Load", (_, a) => ((AssetRecord)a[1]).path);
        Add(typeof(Rendering.StandardParticleShader), "Resolve", "Shader.StandardVariant", (_, a) => ((Waylith.Rendering.Shader)a[0]).Key + " | linear=" + a[1]);
        Add(typeof(Rendering.MappedColorSpaceShader), "Wrap", "Shader.ColorSpace", (_, a) => ((Waylith.Rendering.Shader)a[0]).Key + " | linear=" + a[1]);
        Add(typeof(Rendering.LinearShaderVariants), "Register", "Shader.HdrVariant", (_, a) => ((Waylith.Rendering.Shader)a[0]).Key);
        Add(typeof(Texture2D), "LoadFile", "Texture.Load", (_, a) => Clip(a[0] as string));
        Add(typeof(Texture2D), "ReadFile", "Texture.ReadDecode", (_, a) => Clip(a[0] as string), postfix: nameof(TextureCacheResult));
        Add(typeof(Texture2D).GetNestedType("FileTexture", BindingFlags.NonPublic), "EnsureResident", "Texture.FileResident", TextureDetail, TextureNotes);
        Add(typeof(Runtime.NativeRuntime), "Drain", "Lifecycle.Drain");
        Add(typeof(Runtime.NativeRuntime), "Invoke", "Lifecycle.Callback", (_, a) =>
            a[0]?.GetType().FullName + " | " + (a[1] as Delegate)?.Method.Name);
        Add(typeof(Runtime.NativeRuntime).Assembly.GetType("RimUnity.Runtime.MessageMethods"), "For", "Lifecycle.BindMethods", (_, a) => ((Type)a[0]).FullName);
        Add(typeof(HostFrame), "Update", "Bridge.Frame");
        Add(typeof(Runtime.NativeRuntime), "BeginFrame", "Bridge.BeginFrame");
        Add(typeof(Runtime.NativeRuntime), "Update", "Bridge.Update");
        Add(typeof(Runtime.NativeRuntime), "LateUpdate", "Bridge.LateUpdate");
        Add(typeof(ParticleSystem), "PlaySystems", "Particle.Play", (o, _) => ObjectPath((Component)o));
        Add(typeof(ParticleSystem), "SimulateFrame", "Particle.FrameSimulation");
        Add(typeof(ParticleSystem), "PrewarmSystems", "Particle.Prewarm", notes: (_, a) => SystemNotes(a[0]));
        Add(typeof(ParticleSystem), "AdvanceSystems", "Particle.Advance", notes: (_, a) => SystemNotes(a[0]));
        Add(typeof(ParticleSystem), "ResetPlayback", "Particle.Reset", (o, _) => ObjectPath((Component)o), (o, _) => ParticleNotes((ParticleSystem)o));
        Add(typeof(ParticlePhysicsWorld), "Refresh", "Particle.PhysicsRefresh");
        Add(typeof(ParticleFrame), "Draw", "Bridge.DrawScene");
        Add(typeof(ParticleSystemRenderer), "Draw", "Particle.Draw", (o, _) => ObjectPath((Component)o), (_, a) => ParticleNotes((ParticleSystem)a[0]));
        Add(typeof(ParticleSystemRenderer), "DrawTrails", "Particle.DrawTrails", (o, _) => ObjectPath((Component)o));
        Add(typeof(Graphics), "DrawMesh", "Draw.Submit", (_, a) =>
            a[2] is Material m ? m.name + " | " + m.shader.name : "<null material>", (_, a) => a[0] is Mesh m ? "vertices=" + m.vertexCount + "; submeshes=" + m.subMeshCount : "");

        Native("Waylith.Rendering.RuntimeShaderCompiler", "Compile", "Shader.Compile", (_, a) => Clip(a[1] as string) + " | entry=" + a[2] + " | " + a[3] + "/" + a[4],
            filter: m => m.GetParameters().Length == 6, postfix: nameof(CompilerCacheResult));
        Native("Waylith.Rendering.RuntimeShaderCompiler", "CompileEntryPoints", "Shader.CompileMiss", (_, a) => Clip(a[1] as string));
        Native("Waylith.Rendering.RuntimeShaderCompiler", "TryReadCache", "Shader.ReadDiskCache", (_, a) => Clip(a[0] as string));
        Native("Waylith.Rendering.ShaderModuleCache", "GetOrCreateVertexShader", "Shader.VertexModule", VariantDetail);
        Native("Waylith.Rendering.ShaderModuleCache", "GetOrCreateFragmentShader", "Shader.FragmentModule", VariantDetail);
        Native("Waylith.Rendering.ShaderModuleCache", "CompileShaderModule", "Shader.CompileModule", (_, a) => Clip(a[3] as string));
        Native("Waylith.Rendering.MeshPipelineCache", "GetOrCreatePipeline", "GPU.Pipeline", (_, a) =>
            a[2] is Waylith.Rendering.MaterialBinding m ? m.Shader.Key : "native pipeline", (_, a) => "topology=" + a[4] + "; state=" + a[5] + "; renderPass=" + a[6] + "; samples=" + a[7] + "; pass=" + a[9]);
        Native("Waylith.Rendering.Texture2D", "EnsureResident", "Texture.NativeResident", TextureDetail, TextureNotes);
        Native("Waylith.Rendering.Texture2D", "UploadCpuData", "Texture.Upload", TextureDetail, TextureNotes);
        Native("Waylith.Rendering.GpuTextureService", "AllocateTexture2D", "GPU.AllocateTexture", (_, a) => Clip(a[0] as string), (_, a) => "width=" + a[1] + "; height=" + a[2] + "; mips=" + a[3] + "; format=" + a[4]);
        Native("Waylith.Rendering.GpuTextureService", "SubmitPendingUploads", "GPU.SubmitTextureUploads");
        Native("Waylith.Rendering.GpuTextureService", "FlushPendingTextureCommands", "GPU.FlushTextureCommands");
        Native("Waylith.Rendering.TextureResidency", "MakeRoom", "GPU.ResidencyMakeRoom");
        Native("Waylith.Graphics.Meshes.MeshUploader", "UploadCore", "GPU.MeshUpload");
        Native("Waylith.Graphics.Meshes.MeshUploader", "UploadBatch", "GPU.MeshUploadBatch");
        coverage.Add("EVENT Host.MainFrame: PrePollEvents -> FrameEnqueued; Host.RenderFrame: RenderThreadFrameStarting -> RenderThreadFrameEnded. Detached spans: overlap nested probes, not exclusive times.");
        coverage.Add("NOT PATCHED: FrameRenderer.CaptureFrame/RecordFrame, CompositePass.Record, GpuTextureService.CreateTexture2D: current Harmony cannot preserve required readonly-reference call signatures. Frame events plus texture residency/upload/allocation provide enclosing timings.");
        coverage.Add("DIRECT Asset.OpenJson / Asset.DeserializeJson / Bloom.Prepare / HDR.Record: source scopes, including generic JSON loading without Harmony generic detours.");
    }
    private static string PrefabName(PrefabData data) => data.nodes.FirstOrDefault(n => n.parent == null)?.name ?? "<empty>";
    private static string ObjectPath(Component component)
    {
        if (ReferenceEquals(component, null)) return "<null>";
        var go = component.gameObject;
        return Clip(go.name, 180) + " | object=" + go.GetInstanceID() + " | component=" + component.GetInstanceID();
    }
    private static string ParticleNotes(ParticleSystem system) => "count=" + system.particleCount + "; capacity=" + system.particles.Length
        + "; max=" + system.main.maxParticles + "; prewarm=" + system.main.prewarm + "; loop=" + system.main.loop
        + "; duration=" + system.main.duration + "; speed=" + system.main.simulationSpeed
        + "; collision=" + system.collision.enabled + "; trails=" + system.trails.enabled + "; subEmitters=" + system.subEmitters.enabled;
    private static string SystemNotes(object value)
    {
        var systems = (List<ParticleSystem>)value;
        return "systems=" + systems.Count + "; particles=" + systems.Take(512).Sum(s => s.particleCount)
            + "; pendingPrewarm(<=512)=" + systems.Take(512).Count(s => s.DiagnosticPrewarmPending)
            + "; maxDelta(<=512)=" + (systems.Count == 0 ? 0 : systems.Take(512).Max(s => s.DiagnosticFrameDelta))
            + "; first=" + (systems.Count == 0 ? "" : ObjectPath(systems[0]));
    }
    private static string TextureDetail(object instance, object[] args) => instance is Waylith.Rendering.Texture2D texture ? Clip(texture.Name) : "native texture";
    private static string VariantDetail(object instance, object[] args) => args[0] is Waylith.Rendering.ShaderVariantKey variant
        ? variant.Shader.Key + " | keywords=" + variant.DynamicKeywordKey : "native variant";
    private static string TextureNotes(object instance, object[] args) => instance is Waylith.Rendering.Texture2D texture
        ? "size=" + texture.width + "x" + texture.height + "; GPU_before=" + (texture.GpuResource != null) + "; CPU_before=" + texture.CpuData.HasData : "";
    private static void Native(string type, string method, string stage, Func<object, object[], string> detail = null,
        Func<object, object[], string> notes = null, Func<MethodInfo, bool> filter = null, string postfix = null)
        => Add(AccessTools.TypeByName(type), method, stage, detail, notes, filter, postfix, type);
    private static void Add(Type type, string method, string stage, Func<object, object[], string> detail = null,
        Func<object, object[], string> notes = null, Func<MethodInfo, bool> filter = null, string postfix = null, string expectedType = null)
    {
        if (type == null) { coverage.Add("MISSING TYPE " + expectedType + " :: " + stage); return; }
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.Name == method && !m.ContainsGenericParameters && (filter == null || filter(m))).ToArray();
        if (methods.Length == 0) coverage.Add("MISSING METHOD " + type.FullName + "." + method + " :: " + stage);
        foreach (var target in methods)
        {
            if (probes.ContainsKey(target)) continue;
            string label = target.DeclaringType.FullName + "." + target.Name + "(" + string.Join(",", target.GetParameters().Select(p => p.ParameterType.Name)) + ")";
            try
            {
                probes.TryAdd(target, new Probe(stage, detail, notes));
                bool opaqueArgs = target.GetParameters().Any(p => (p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType).IsByRefLike || p.ParameterType.IsPointer);
                ProbeHarmony.Patch(target, prefix: new HarmonyMethod(typeof(ParticleSpikeCapture), opaqueArgs ? nameof(ProbePrefixLite) : nameof(ProbePrefix)),
                    postfix: postfix == null ? null : new HarmonyMethod(typeof(ParticleSpikeCapture), postfix),
                    finalizer: new HarmonyMethod(typeof(ParticleSpikeCapture), nameof(ProbeFinalizer)));
                installed.Add(target); coverage.Add("OK " + stage + " :: " + label + (opaqueArgs ? " [timing only: opaque arguments]" : ""));
            }
            catch (Exception e)
            {
                try { ProbeHarmony.Unpatch(target, HarmonyPatchType.All, PatchId); } catch { }
                coverage.Add("FAILED " + label + " :: " + Clip(e.GetBaseException().Message));
            }
        }
    }
    private static void RemoveProbes()
    {
        foreach (var method in installed.ToArray())
            try { ProbeHarmony.Unpatch(method, HarmonyPatchType.All, PatchId); installed.Remove(method); }
            catch (Exception e) { coverage.Add("UNPATCH FAILED " + method + " :: " + e.GetType().Name); }
    }
    private static void ProbePrefix(object __instance, object[] __args, MethodBase __originalMethod, out SpikeCaptureData.Token __state)
    {
        __state = null;
        var data = current;
        if (data == null) return;
        try
        {
            if (!probes.TryGetValue(__originalMethod, out var probe)) return;
            string detail = Clip(probe.Detail?.Invoke(__instance, __args) ?? "");
            string notes = Clip(probe.Notes?.Invoke(__instance, __args) ?? "", 1200);
            __state = data.Begin(probe.Stage, detail, observedFrame);
            if (__state != null) __state.Notes = notes;
        }
        catch { data.ProbeError(); }
    }
    private static void ProbePrefixLite(MethodBase __originalMethod, out SpikeCaptureData.Token __state)
    {
        __state = null;
        var data = current;
        if (data == null) return;
        try
        {
            if (probes.TryGetValue(__originalMethod, out var probe)) __state = data.Begin(probe.Stage, "opaque arguments", observedFrame);
        }
        catch { data.ProbeError(); }
    }
    private static void CompilerCacheResult(bool __5, SpikeCaptureData.Token __state)
    {
        if (__state != null) __state.Outcome = "compilerCacheHit=" + __5;
    }
    private static void TextureCacheResult(bool __1, SpikeCaptureData.Token __state)
    {
        if (__state != null) __state.Outcome = "textureCacheHit=" + __1;
    }
    private static Exception ProbeFinalizer(Exception __exception, SpikeCaptureData.Token __state)
    {
        if (__state == null) return __exception;
        try { __state.Owner.End(__state, __exception == null ? __state.Outcome : "exception:" + Clip(__exception.GetType().FullName + ": " + __exception.Message, 1500)); }
        catch { __state.Owner.ProbeError(); }
        // Preserve original control flow, including exceptions and other mods' finalizers.
        return __exception;
    }
    // Source instrumentation for bridge methods whose IL includes host readonly-reference calls.
    // Inactive path is a volatile null check, with no allocation or metadata formatting.
    internal static MeasureScope Measure(string stage, string detail = "")
    {
        var data = current;
        return data == null ? default : new MeasureScope(data.Begin(stage, detail, observedFrame));
    }
    internal readonly struct MeasureScope : IDisposable
    {
        private readonly SpikeCaptureData.Token token;
        internal MeasureScope(SpikeCaptureData.Token token) => this.token = token;
        public void Dispose() { if (token != null) token.Owner.End(token); }
    }
}
