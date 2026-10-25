using System.Numerics;
using System.Reflection;
using Brutal.VulkanApi;
using Waylith;
using Waylith.Graphics.Meshes;
using Waylith.Rendering;

// Real indexed host mesh and real registered shaders. Geometry is generated here
// so the invariant does not depend on any Nivarian resource name or art parameter.
internal sealed class RuntimeGeometry : IDisposable
{
    private readonly Mesh mesh;
    private readonly MeshDrawGeometry geometry;
    private readonly MaterialBinding gamma, transparent, visible;
    private readonly byte mapPass;
    private delegate FinalDrawItem RentDraw(Matrix4x4 matrix, byte passId, MeshDrawGeometry mesh, MaterialBinding binding,
        bool hasOverride, in MaterialUniformBytes uniforms, in MaterialTextureResourceSet<Texture> resources,
        int order, int queue, InstancedDrawInfo instancing, int layer, ushort hostOrder, InstanceStorageBinding[] storage);
    private readonly RentDraw rent;
    private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;
    internal static readonly Vector3 VisibleLinear = new(.01f, .04f, .09f);

    internal unsafe RuntimeGeometry(EngineApplication app, Assembly runtime, string scratch)
    {
        mapPass = (byte)AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "RimWorld")
            .GetType("RimWorldWaylith.Rendering.RenderPassIds").GetField("MapMain", Flags).GetValue(null);
        rent = typeof(FinalDrawItem).GetMethod("Rent", Flags).CreateDelegate<RentDraw>();
        var storage = new CpuMeshData();
        storage.SetVertexLayout(VertexLayout.Empty, 3);
        storage.SetIndexBufferParams(3, MeshIndexFormat.UInt16);
        var indices = (ushort*)storage.GetIndexBuffer().Pointer;
        indices[0] = 0; indices[1] = 1; indices[2] = 2;
        storage.SetSubMeshCount(1); storage.SetSubMesh(0, new SubMesh(0, 3));
        mesh = new Mesh(storage) { DebugName = "ColorTest.Fullscreen" };
        mesh.MarkCpuStorageChanged(); app.GraphicsDevice.MeshUploads.Upload(mesh);
        var gpu = mesh.GpuResource;
        geometry = new MeshDrawGeometry(mesh.Id, 0, gpu.VertexStreams.ToArray(), mesh.DebugName, gpu.Layout,
            gpu.IndexBuffer, VkIndexType.UInt16, 3, 0, 0, MeshTopology.Triangles, gpu.Bounds);
        gamma = Binding("GammaZero", "vec4(1,1,1,0)", false);
        transparent = Binding("LinearZero", "vec4(10,0,0,0)", true);
        visible = Binding("LinearVisible", "vec4(.01,.04,.09,.5)", true);

        MaterialBinding Binding(string name, string color, bool linear)
        {
            string vertex = "#version 450\nvoid main(){vec2 p=vec2((gl_VertexIndex<<1)&2,gl_VertexIndex&2);gl_Position=vec4(p*2-1,0,1);}";
            string fragment = "#version 450\nlayout(location=0) out vec4 c;\n"
                + "vec3 RU_Encode(vec3 v){return mix(v*12.92,1.055*pow(max(v,vec3(0)),vec3(1.0/2.4))-.055,step(vec3(.0031308),v));}\n"
                + "void RU_SourceMain(){c=" + color + ";}\nvoid main(){RU_SourceMain();c.rgb=RU_Encode(c.rgb);}\n// @rimunity-linear-program 1\n";
            string vertPath = Path.Combine(scratch, name + ".vert.glsl"), fragPath = Path.Combine(scratch, name + ".frag.glsl");
            File.WriteAllText(vertPath, vertex); File.WriteAllText(fragPath, fragment);
            var pipeline = new PipelineState(BlendState.Alpha, new DepthState(false, false), FaceCull.None);
            var definition = new ShaderProgramDefinition { vertexShaderFileName = vertPath, fragmentShaderFileName = fragPath,
                useTexture = false, transparent = true, depthTest = false, depthWrite = false, cullMode = FaceCull.None };
            var stages = new PrecompiledShaderStages(
                RuntimeShaderCompiler.Compile(vertex, vertPath, "main", RuntimeShaderLanguage.Glsl, RuntimeShaderStage.Vertex),
                RuntimeShaderCompiler.Compile(fragment, fragPath, "main", RuntimeShaderLanguage.Glsl, RuntimeShaderStage.Fragment), false);
            var shader = new Shader("ColorTest." + name, fragPath, definition, materialLayout: MaterialLayout.Empty(),
                pipelineState: pipeline, vertexInputLayout: new ShaderVertexInputLayout(), precompiledStages: stages);
            if (shader.HasErrors) throw new Exception("Synthetic shader invalid: " + name);
            ShaderRegistry.Register(shader);
            if (linear) runtime.GetType("RimUnity.Rendering.LinearShaderVariants", true).GetMethod("Register", Flags)
                .Invoke(null, new object[] { shader, fragment });
            return new MaterialBinding(shader.Id, shader.Id, 1, name, shader, MaterialLayout.Empty(), default, default, 3001,
                new ShaderVariantKey(shader, 0, "", true), pipeline, null, false);
        }
    }

    internal void Add(MeshDrawFrame frame, int mode)
    {
        if (mode == 0) return;
        Add(gamma);
        Add(mode == 2 ? visible : transparent);
        Add(gamma);
        Add(transparent);
        void Add(MaterialBinding material)
        {
            int order = frame.Draws.Count;
            frame.Draws.Add(rent(Matrix4x4.Identity, mapPass, geometry, material, false,
                in material.Uniforms, in material.Resources, order, 3001, null, 0, 0, null));
        }
    }
    public void Dispose() => mesh.Dispose();
}
