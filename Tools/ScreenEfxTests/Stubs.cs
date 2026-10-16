namespace Verse
{
    //Only session and logging boundaries are faked. Playback/snapshot code is production source.
    public sealed class Game { }
    public static class Current { public static Game Game; }
    public static class Find { public static object CurrentMap; }
    public static class UnityData
    {
        internal static int ThreadId = Environment.CurrentManagedThreadId;
        public static bool IsInMainThread => Environment.CurrentManagedThreadId == ThreadId;
    }
    public static class LongEventHandler { public static bool AnyEventNowOrWaiting; }
    public static class Log { public static readonly List<string> Errors = new(); public static void Error(string message) => Errors.Add(message); }
}
namespace RimUnity.Rendering
{
    internal static class LinearShaderVariants
    {
        internal static bool TryGet(Waylith.Rendering.Shader source, out Waylith.Rendering.Shader result) { result = source; return false; }
    }
}
namespace RimUnity
{
    internal static class RimUnityMod { internal static string ShaderDirectory => "Shaders/RimUnity"; }
}
namespace RimUnity.Diagnostics
{
    internal static class RenderDebugReport
    {
        internal static bool Collecting => false;
        internal static void Stage(string name, string value) { }
    }
}
