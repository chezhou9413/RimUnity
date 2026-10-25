// Deterministic CPU fixture; no claim about driver/GPU behavior or shader validation.
namespace RimUnity
{
    internal struct Vector3 { }
    internal struct Vector4 { }
    internal struct Color32 { }
    internal struct Matrix4x4 { internal static Matrix4x4 identity => default; }
    internal enum IndexFormat { UInt32 }
    internal static class Time { internal static int frameCount; }
    internal sealed class GameObject
    {
        internal int layer;
        internal string name = "fixture";
        internal readonly Transform transform;
        internal GameObject() { transform = new Transform { gameObject = this }; }
    }
    internal sealed class Transform
    {
        internal Transform parent;
        internal GameObject gameObject;
        internal string name => gameObject.name;
        internal Transform root => parent == null ? this : parent.root;
    }
    internal sealed class Canvas { }
    internal sealed class Camera
    {
        internal static readonly HashSet<Camera> Active = new();
        internal static Camera main;
        internal bool isActiveAndEnabled = true;
        internal int cullingMask = -1;
        internal object targetTexture;
    }
    internal sealed class ParticleSystemRenderer { internal bool isActiveAndEnabled = true; }
    internal sealed class ParticleSystem
    {
        internal static readonly HashSet<ParticleSystem> Active = new();
        internal readonly GameObject gameObject = new();
        internal Transform transform => gameObject.transform;
        internal string name => gameObject.name;
        internal bool isActiveAndEnabled = true, HasCanvas;
        internal int particleCount;
        internal ParticleSystemRenderer Renderer;
        internal int GetInstanceID() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        internal T GetComponent<T>() => (T)(object)Renderer;
        internal T GetComponentInParent<T>() => HasCanvas ? (T)(object)FixtureCanvas : default;
        private static readonly Canvas FixtureCanvas = new();
    }
    internal class Material
    {
        internal int passCount = 1;
        internal Native engineMaterial = new();
        internal Rendering.ParticleBatchShaders.Eligibility Eligibility;
        internal class Native { internal int Version; }
    }
    internal class MaterialPropertyBlock { internal int PropertyCount; internal bool HasStorageBindings; }
    internal class Mesh
    {
        internal string name;
        internal IndexFormat indexFormat;
        internal int vertexCount;
        internal int VertexCapacity;
        internal bool Destroyed;
        internal readonly List<int> Indices = new();
        internal readonly List<List<int>> Submeshes = new();
        internal static bool FailAppend;
        internal void MarkDynamic() { }
        internal void Clear() { vertexCount = 0; Indices.Clear(); Submeshes.Clear(); }
        internal void AppendParticleGeometry(List<Vector3> vertices, List<Vector3> normals, List<Vector4> tangents,
            List<Color32> colors, List<Vector4> uv0, List<Vector4> uv1, List<Vector4> uv2, List<int> indices, int submesh, List<ParticleVertex> packed = null)
        {
            if (FailAppend) throw new InvalidOperationException("fixture append failure");
            while (Submeshes.Count <= submesh) Submeshes.Add(new());
            foreach (int index in indices) { Indices.Add(index + vertexCount); Submeshes[submesh].Add(index + vertexCount); }
            vertexCount += packed?.Count ?? vertices.Count;
            VertexCapacity = Math.Max(VertexCapacity, vertexCount);
        }
        internal (long Vertices, long Indices, long ManagedBytes, long NativeCpuBytes) ParticleBufferUsage()
        {
            long indices = 0; foreach (var list in Submeshes) indices += list.Capacity;
            return (VertexCapacity, indices, VertexCapacity * 92L + indices * 4, 0);
        }
    }
    internal static class Graphics
    {
        internal static readonly List<(Material Material, int Layer, int[] Indices, Mesh Mesh, int Submesh)> Draws = new();
        internal static void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int layer, int submesh = 0)
            => Draws.Add((material, layer, mesh.Submeshes[submesh].ToArray(), mesh, submesh));
    }
    internal static class Object
    {
        internal static void Destroy(object item) { if (item is Mesh mesh) mesh.Destroyed = true; }
        internal static void DestroyImmediate(object item) => Destroy(item);
    }
}
namespace RimUnity.Rendering
{
    internal static class ParticleBatchShaders
    {
        internal enum Eligibility { Supported, MultiplePasses, UnknownShader, UnsafeShader }
        internal static Eligibility Check(Material material) => material.passCount == 1 ? material.Eligibility : Eligibility.MultiplePasses;
    }
}
namespace RimUnity.Diagnostics
{
    internal static class ParticleDebugWindow { internal static bool Capturing; }
    internal static class ParticleSpikeCapture
    {
        internal static bool LabCaptureActive => false;
        internal static System.IDisposable Measure(string stage) => null;
    }
}

namespace Waylith
{
    internal sealed class EngineApplication
    {
        internal static readonly EngineApplication Instance = new();
        internal event Action<EngineApplication> FrameEnqueued;
        internal event Action<EngineApplication> PrePollEvents;
        internal int Subscribers => FrameEnqueued?.GetInvocationList().Length ?? 0;
        internal int PrePollSubscribers => PrePollEvents?.GetInvocationList().Length ?? 0;
        internal void Poll() => PrePollEvents?.Invoke(this);
        internal void Emit() => FrameEnqueued?.Invoke(this);
    }
}
namespace Verse
{
    internal class Game { }
    internal static class LongEventHandler { internal static bool AnyEventNowOrWaiting; }
    internal static class Log { internal static void Error(string text) => throw new Exception(text); internal static void Message(string text) { } }
    internal static class GenFilePaths { internal static string ConfigFolderPath = Path.Combine(Path.GetTempPath(), "RimUnity", "LabCpuMetricsTests-" + Guid.NewGuid().ToString("N")); }
    internal static class Messages { internal static void Message(string text, object kind, bool historical) { } }
}
namespace RimWorld { internal static class MessageTypeDefOf { internal static readonly object TaskCompletion = new(); } }
namespace UnityEngine { internal static class Screen { internal static int width => 1600; internal static int height => 900; } }
namespace RimUnity
{
    internal static class RuntimeInfo
    {
        internal static string Version => "test";
        internal static string Provider => "fixture";
        internal static bool IsStandalone => false;
        internal static string PayloadHash => "fixture-payload";
    }
    internal static class RimUnityMod
    {
        internal static readonly Settings Bloom = new();
        internal sealed class Settings { internal Settings Snapshot() => this; internal bool Enabled => true; internal int LayerMask => 1; internal float Threshold => .8f; internal float Intensity => .6f; internal float SoftKnee => .5f; internal float Scatter => .65f; }
    }
}
namespace RimUnity.Diagnostics
{
    internal sealed class PerformanceLab
    {
        internal string Scenario = "selected-prefabs";
        internal bool RegisterFixtureFamily;
        internal void RegisterCpuFamilies(LabCpuMetrics capture) { if (RegisterFixtureFamily) capture?.RegisterSystem(123, 0); }
        internal Dictionary<string, int> FixedDistribution => new();
        internal string[] ActiveEffects => Effects.Where(e => e.Selected).Select(e => e.Mod.PackageIdPlayerFacing + "/" + e.Path).ToArray();
        internal bool IsCurrent = true, StableReady = true, FixedInstances = true, Playing = true, MeasureCpuStages = true;
        internal string Status;
        internal int Concurrency = 20, WarmupSeconds = 10, LiveCount = 20, Spawned = 20, ThrottledFrames;
        internal float Rate = 5, Lifetime = 5, Radius = 8, Scale = 1, RotationX;
        internal readonly List<Effect> Effects = new();
        internal readonly HashSet<GameObject> OwnedRoots = new();
        internal void CollectPopulationRoots(HashSet<GameObject> roots) { roots.Clear(); foreach (var root in OwnedRoots) roots.Add(root); }
        internal (int Systems, int Particles) Counts() => LiveCount == 0 ? (0, 0) : (80, 800);
        internal sealed class Effect
        {
            internal bool Selected;
            internal Mod Mod;
            internal string Path, Warnings;
        }
        internal sealed class Mod { internal string PackageIdPlayerFacing; }
    }
}

namespace RimUnity.Diagnostics { internal static class LabStressScenario { internal const string Version = "mixed-particles-v2"; internal const int Copies = 4, ParticleLimit = 64, WorldLayer = 30; internal static readonly string[] Names = { "fixture" }; } }

namespace Verse { internal static class Find { internal static readonly Driver CameraDriver = new(); internal class Driver { internal float RootSize => 24; } } }
