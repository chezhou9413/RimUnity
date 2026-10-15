namespace UnityEngine
{
    public enum HideFlags { HideAndDontSave }
    public class Object
    {
        public string name;
        public HideFlags hideFlags;
        public bool Destroyed;
        public static T Instantiate<T>(T source) where T : Object => (T)source.MemberwiseClone();
        public static void DestroyImmediate(Object value) => value.Destroyed = true;
    }
    public sealed class Material : Object { public float SingleChannel = 1; }
    public sealed class Texture : Object { }
}
namespace UnityEditor
{
    public static class TypeCache
    {
        public static Type[] Types = Array.Empty<Type>();
        public static IEnumerable<Type> GetTypesWithAttribute<T>() => Types;
    }
}
namespace RimUnity.Editor
{
    public sealed class ExportOptions
    {
        public bool discoverProcessors = true;
        public IExportProcessor[] processors = Array.Empty<IExportProcessor>();
    }
}
