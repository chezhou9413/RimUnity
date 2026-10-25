using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using RimUnity;

internal static class Program
{
    private static int Main(string[] args)
    {
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            foreach (string folder in args)
            {
                string path = Path.Combine(folder, name.Name + ".dll");
                if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            }
            return null;
        };
        try { Run(); AnimatorReuseChecks.Run(); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run()
    {
        RimUnity.Runtime.NativeRuntime.InitializeOnCurrentThread();
        var root = new GameObject("custom channel animation regression");
        try
        {
            var system = root.AddComponent<ParticleSystem>();
            system.customData.enabled = true;
            system.main.startLifetime = 100;
            system.main.startSpeed = 0;
            system.emission.enabled = false;
            system.shape.enabled = false;
            system.Emit(1);
            var bind = typeof(ParticleSystem).Assembly.GetType("RimUnity.AnimatedParticleFloat")!
                .GetMethod("TryBind", BindingFlags.Static | BindingFlags.NonPublic)!;
            for (int channel = 0; channel < 2; channel++)
            {
                var stream = (ParticleSystemCustomData)channel;
                system.customData.SetMode(stream, ParticleSystemCustomDataMode.Vector);
                for (int component = 0; component < 4; component++)
                {
                    object[] values = { system, $"CustomDataModule.vector{channel}_{component}.scalar", null!, null! };
                    Check((bool)bind.Invoke(null, values)!, "Binding rejected a serialized Unity channel");
                    var read = (Func<float>)values[2]; var write = (Action<float>)values[3];
                    foreach (float value in new[] { 5f, -.2f, .05f })
                    {
                        float other = system.customData.GetVector((ParticleSystemCustomData)(1 - channel), component).constantMax;
                        write(value);
                        var data = new List<Vector4>(); system.GetCustomParticleData(data, stream);
                        Check(data.Count == 1 && Math.Abs(data[0][component] - value) < .00001f, "Animated value did not reach particle custom data");
                        Check(read() == value, "Animated scalar readback differs");
                        Check(system.customData.GetVector((ParticleSystemCustomData)(1 - channel), component).constantMax == other, "Animation modified the other custom channel");
                    }
                }
            }
            Console.WriteLine("PASS zero-based Unity animation paths: both custom channels, all components, forming/overshoot/idle values and channel isolation.");
        }
        finally { RimUnity.Object.DestroyImmediate(root); }
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }
}
