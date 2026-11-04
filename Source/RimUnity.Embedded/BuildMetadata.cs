using System;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace RimUnity.Embedded;

//发布产物的 UTC 时间，不读取文件系统时间；缺少元数据的历史包排在最早。
internal static class BuildMetadata
{
    internal const string RevisionKey = "RimUnityCoordinatorRevision";
    internal const string TimeKey = "RimUnityBuiltAtUtc";
    internal static string Value(Assembly assembly, string key) => assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value;

    internal static bool TryTime(string text, out DateTimeOffset value) => DateTimeOffset.TryParseExact(text,
        new[] { "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'" },
        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value);

    internal static DateTimeOffset Time(Assembly assembly) => TryTime(Value(assembly, TimeKey), out var value)
        ? value : DateTimeOffset.MinValue;
    internal static int Revision(Assembly assembly) => int.TryParse(Value(assembly, RevisionKey),
        NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : 0;
}
