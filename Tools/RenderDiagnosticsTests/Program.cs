using System;

using System.IO;

using System.Reflection;

using System.Runtime.Loader;

using System.Text;

using System.Collections;

using System.Linq;

public class PropertyFrame
{
    public float CameraYaw
    {
        get;
        set;
    }
    public float CameraPitch
    {
        get;
        set;
    }
    public object Resource
    {
        get;
        set;
    }
    public readonly int Readonly = 99;
}

public class FieldFrame
{
    public float CameraYaw;
    public double CameraPitch;
    public int AddedCounter;
    public object Resource;
}

class Probe
{
    public int Good => 17;
    public int Bad => throw new InvalidOperationException("probe getter");
}

class P
{

    static void Main(string[] args)
    {

        if (args.Length != 3) throw new ArgumentException("usage: <RimUnity.dll> <game directory> <0Harmony.dll>");
        string game = Path.GetFullPath(args[1]), repo = Path.GetDirectoryName(Path.GetFullPath(args[0])), harmonyPath = Path.GetFullPath(args[2]);
        AssemblyLoadContext.Default.Resolving += (c, n) =>
        {
            string p = Path.Combine(repo, n.Name + ".dll");
            if (!File.Exists(p)) p = Path.Combine(game, n.Name + ".dll");
            if (!File.Exists(p) && n.Name == "0Harmony") p = harmonyPath;
            return File.Exists(p) ? c.LoadFromAssemblyPath(p) : null;
        }
       ;
        var a = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(repo, "RimUnity.dll"));
        var t = a.GetType("RimUnity.Diagnostics.RenderDebugReport", true);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        object Call(string n, params object[] args) => t.GetMethod(n, flags).Invoke(null, args);
        void Check(bool value, string message)
        {
            if (!value) throw new Exception(message);
            Console.WriteLine("PASS " + message);
        }

        var b = new StringBuilder();
        Call("Dump", b, "probe", new Probe(), 0);
        Check(b.ToString().Contains("Good = 17") && b.ToString().Contains("probe getter"), "throwing property does not abort diagnostic");

        b.Clear();
        Call("Section", b, "failure", new Action<StringBuilder>(x => throw new Exception("failure probe")));
        Check(b.ToString().Contains("SECTION FAILED"), "section failure remains explicit");

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string red = (string)Call("Redact", home + "/test " + home.Replace('\\', '/') + "/test");
        Check(!red.Contains(home) && red.Contains("<USER>"), "profile paths redacted");

        t.GetField("Collecting", flags).SetValue(null, true);
        for (int i = 0; i < 50; i++) Call("Stage", "stage" + i, "test");
        var stages = (IDictionary)t.GetField("Stages", flags).GetValue(null);
        Check(stages.Count == 32, "stage cache bounded at 32");
        Call("ClearStages");
        Check(stages.Count == 0, "stage cache cleared");
        t.GetField("Collecting", flags).SetValue(null, false);
        Call("Stage", "inactive", "test");
        Check(stages.Count == 0, "inactive capture stores nothing");

        AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game, "RimWorld.dll"));
        AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game, "Waylith.dll"));
        var hooks = t.GetNestedType("ObservePasses", BindingFlags.NonPublic);
        var targets = (IEnumerable)hooks.GetMethod("TargetMethods", flags).Invoke(null, null);
        var methods = targets.Cast<MethodBase>().ToArray();
        Console.WriteLine("Resolved=" + methods.Length);
        Check(methods.Length == 1, "safe host capture method resolves");
        foreach (var m in methods) Console.WriteLine("TARGET " + m.DeclaringType.FullName + "." + m.Name);

        var harmonyType = AssemblyLoadContext.Default.LoadFromAssemblyPath(harmonyPath).GetType("HarmonyLib.Harmony", true);

        var harmony = Activator.CreateInstance(harmonyType, new object[] {
 "RimUnity.Tests.RenderReport" }
);


        var processor = harmonyType.GetMethod("CreateClassProcessor").Invoke(harmony, new object[] {
 hooks }
);

        try
        {
            processor.GetType().GetMethod("Patch").Invoke(processor, null);
            Check(true, "render report hooks actually install against current host");
        }

        finally
        {
            foreach (var method in methods)
            {
                var unpatch = harmonyType.GetMethods().First(m => m.Name == "Unpatch" && m.GetParameters().Length == 3);
                var en = Enum.Parse(unpatch.GetParameters()[1].ParameterType, "All");
                unpatch.Invoke(harmony, new object[] {
 method, en, "RimUnity.Tests.RenderReport" }
               );
            }
        }

        var copy = t.GetMethod("Copy", flags);
        Check(copy.GetCustomAttributes().Any(x => x.GetType().Name == "DebugActionAttribute"), "debug action registered");

        var copier = a.GetType("RimUnity.Rendering.FrameMetadataCopier", true).GetMethod("Build", flags);

        foreach (var shape in new[] {
 typeof(PropertyFrame), typeof(FieldFrame) }
)
        {

            object[] output = {
 null }
;
            var copyValues = (Delegate)copier.MakeGenericMethod(shape).Invoke(null, output);
            object source = Activator.CreateInstance(shape), target = Activator.CreateInstance(shape);
            object resource = new object();

            void Set(object x, string name, object value)
            {
                var field = shape.GetField(name);
                if (field != null) field.SetValue(x, value);
                else shape.GetProperty(name).SetValue(x, value);
            }

            object Get(object x, string name) => shape.GetField(name)?.GetValue(x) ?? shape.GetProperty(name)?.GetValue(x);

            Set(source, "CameraYaw", 23.75f);
            Set(source, "CameraPitch", shape == typeof(FieldFrame) ? (object)(-12.125d) : -12.125f);
            Set(target, "Resource", resource);
            copyValues.DynamicInvoke(source, target);

            Check(Convert.ToDouble(Get(target, "CameraYaw")) == 23.75 && Convert.ToDouble(Get(target, "CameraPitch")) == -12.125, "metadata copied for " + shape.Name);

            Check(ReferenceEquals(Get(target, "Resource"), resource), "resource ownership unchanged for " + shape.Name);

            Console.WriteLine("MAPPING " + output[0]);

        }

        using (var pe = new System.Reflection.PortableExecutable.PEReader(File.OpenRead(a.Location)))
        {
            var md = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
            var refs = md.MemberReferences.Select(h => md.GetString(md.GetMemberReference(h).Name)).ToArray();
            Check(!refs.Any(n => new[] {
 "get_CameraYaw", "set_CameraYaw", "get_CameraPitch", "set_CameraPitch", "get_SwayHead", "get_WindSpeed", "get_BillboardPitchDelta" }
           .Contains(n)), "no hard-coded optional MeshDrawFrame getter/setter references in DLL");
        }

        var frameType = AssemblyLoadContext.Default.Assemblies.First(x => x.GetName().Name == "Waylith").GetType("Waylith.Rendering.MeshDrawFrame", true);

        object[] hostOutput = {
 null }
;
        copier.MakeGenericMethod(frameType).Invoke(null, hostOutput);
        Console.WriteLine("ACTUAL HOST MAPPING " + hostOutput[0]);

        foreach (string name in new[] {
 "CameraYaw", "CameraPitch", "SwayHead", "WindSpeed", "BillboardPitchDelta" }
) Console.WriteLine("HOST MEMBER " + name + ": " + string.Join(" | ", frameType.GetMember(name).Select(m => m.MemberType + " " + m)));

        var hdr = a.GetType("RimUnity.Rendering.HdrDrawFrame", true);
        Console.WriteLine("HDR METADATA " + hdr.GetField("MetadataLayout", flags).GetValue(null));

        string full = new string('x', 4 * 1024 * 1024 + 100);
        var clipped = (string)Call("ClipboardText", full, "report.txt");
        Check(clipped.Contains("CLIPBOARD TRUNCATED") && clipped.Contains("report.txt") && full.Length > 4 * 1024 * 1024, "clipboard size limit points to full report file");

        string escaped = System.Text.Json.JsonSerializer.Serialize(home);
        Check(!((string)Call("Redact", escaped)).Contains(home.Replace("\\", "\\\\")), "JSON escaped profile paths redacted");

        Console.WriteLine("All diagnostic and frame-compatibility checks passed against current game.");

    }

}
