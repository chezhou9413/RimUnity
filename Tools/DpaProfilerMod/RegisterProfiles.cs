using System.Collections;
using System.Reflection;
using System.Xml;
using Verse;

namespace RimUnity.DpaProfiler;

// DPA scans Analyzer.xml in its Mod constructor, before embedded RimUnity loads in LoadModXML.
[StaticConstructorOnStartup]
internal static class RegisterProfiles
{
    static RegisterProfiles()
    {
        try
        {
            Register();
        }
        catch (Exception error)
        {
            Log.Warning("[RimUnity DPA Profiles] Could not register Modder profiles. " + error);
        }
    }

    private static void Register()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        var dpa = assemblies.FirstOrDefault(assembly => assembly.GetName().Name == "PerformanceAnalyzer");
        if (dpa == null || !assemblies.Any(assembly => assembly.GetType("RimUnity.HostFrame") != null))
            return;

        var builder = dpa.GetType("Analyzer.Profiling.DynamicTypeBuilder", throwOnError: true)!;
        var groups = (IDictionary)builder.GetField("methods", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        var parser = dpa.GetType("Analyzer.Profiling.XmlParser", throwOnError: true)!;
        var parse = parser.GetMethod("Parse", BindingFlags.NonPublic | BindingFlags.Static, [typeof(XmlDocument)])
            ?? throw new MissingMethodException(parser.FullName, "Parse(XmlDocument)");
        using var config = typeof(RegisterProfiles).Assembly.GetManifestResourceStream("RimUnity.DpaProfiler.Analyzer.xml")
            ?? throw new InvalidOperationException("Missing embedded Analyzer.xml.");
        var document = new XmlDocument();
        document.Load(config);
        var root = document.DocumentElement!;
        foreach (var group in root.ChildNodes.OfType<XmlElement>().ToArray())
            if (groups.Contains(group.Name))
                root.RemoveChild(group);

        int count = root.ChildNodes.Count;
        if (count == 0) return;
        parse.Invoke(null, [document]);
        Log.Message($"[RimUnity DPA Profiles] Registered {count} Modder entries.");
    }
}
