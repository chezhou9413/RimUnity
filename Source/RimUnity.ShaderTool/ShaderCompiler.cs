using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using Brutal.SlangApi;
using Brutal.Strings;
using Waylith.Rendering;

namespace RimUnity.ShaderTool;

//Slang 负责语言转换；Unity 管线适配由导出器另行处理。
internal static class ShaderCompiler
{
    //验证游戏真正使用的编译器和布局解析器，不创建图形设备。
    internal static void Verify(string file)
    {
        string source = File.ReadAllText(file), name = Path.GetFileName(file);
        RimUnity.Rendering.ShaderBindingValidation.Read(source, name);
        bool vertex = name.EndsWith(".vert.glsl", StringComparison.Ordinal);
        var layout = RimUnity.Rendering.ShaderStorageLayout.Extract(source, name);
        if (layout.HasErrors) throw new InvalidDataException(name + "：材质布局不合法。");
        if (vertex && ShaderMetadataParser.ExtractVertexInputLayout(source, name).HasErrors) throw new InvalidDataException(name + "：顶点布局不合法。");
        byte[] code = RuntimeShaderCompiler.Compile(source, name, "main", RuntimeShaderLanguage.Glsl, vertex ? RuntimeShaderStage.Vertex : RuntimeShaderStage.Fragment);
        ShaderUniformLayout.Verify(code, layout, name);
        Console.WriteLine($"{name}: {code.Length} bytes SPIR-V，布局检查通过");
    }

    //输入必须已经完成 Unity 语义及绑定布局适配，不能把 ShaderLab 整份当作 HLSL。
    internal static unsafe void Hlsl(string file, string entry, string stageName, string output, ShaderLabManifest manifest = null, bool verify = true)
    {
        Stage stage = Enum.Parse<Stage>(stageName, true);
        GlobalSessionDesc desc = new() { StructureSize = (uint)sizeof(GlobalSessionDesc), ApiVersion = 0, EnableGlsl = true };
        Slang.PInvoke.slang_createGlobalSession2(in desc, out nint pointer).ThrowIfFailure();
        var wrappers = new StrategyBasedComWrappers();
        IGlobalSession session;
        try { session = (IGlobalSession)wrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.None); }
        finally { Marshal.Release(pointer); }
        using ICompileRequest request = session.CreateCompileRequest();
        request.SetCodeGenTarget(CompileTarget.Glsl);
        //Slang 的 GLSL 表达式使用转置后的矩阵类型，宿主矩阵在 Rewrite 中统一接入。
        request.SetMatrixLayoutMode(MatrixLayoutMode.RowMajor);
        fixed (byte* profile = Encoding.UTF8.GetBytes("glsl_450\0")) request.SetTargetProfile(0, session.FindProfile(profile));
        int unit = request.AddTranslationUnit(SourceLanguage.Hlsl, Utf8(Path.GetFileName(file)));
        request.AddTranslationUnitSourceString(unit, Utf8(Path.GetFileName(file)), Utf8(File.ReadAllText(file)));
        request.AddEntryPoint(unit, Utf8(entry), stage);
        if (request.Compile().IsFailure) throw new InvalidDataException(request.GetDiagnosticOutput().ToString());
        void* code = request.GetEntryPointCode(0, out var length);
        if (code == null || (int)length == 0) throw new InvalidDataException("Slang 未生成 GLSL。");
        string glsl = Encoding.UTF8.GetString(new ReadOnlySpan<byte>(code, (int)length)).TrimEnd('\0');
        //本机 Slang 的 GLSL 前端不接受全局矩阵布局声明；column_major 是 GLSL 默认值。
        glsl = Regex.Replace(glsl, @"(?m)^layout\(column_major\)\s+(uniform|buffer);\s*$", "");
        //游戏前端要求同一声明的布局限定符位于一个 layout 中。
        string previous;
        do { previous = glsl; glsl = Regex.Replace(glsl, @"layout\(([^)]*)\)\s*layout\(([^)]*)\)", "layout($1, $2)"); } while (glsl != previous);
        glsl = ShaderLabGlsl.Rewrite(glsl, File.ReadAllText(file));
        glsl = ShaderMathCompat.Rewrite(glsl, manifest);
        //布局注解来自适配阶段显式指定的位置，不按编译器生成的变量名猜测。
        foreach (Match annotation in Regex.Matches(File.ReadAllText(file), @"//\s*@rimunity-vertex\s+(\d+)\s+(\w+)\s+(required|optional)([^\r\n]*)"))
        {
            string location = annotation.Groups[1].Value;
            string metadata = " // @vertex " + annotation.Groups[2].Value + " " + annotation.Groups[3].Value + annotation.Groups[4].Value;
            glsl = Regex.Replace(glsl, @"layout\(location\s*=\s*" + location + @"\)\s*in\s+\w+\s+\w+;", m => m.Value.Replace("\n", " ").Replace("\r", " ") + metadata);
        }
        string suffix = stage == Stage.Vertex ? ".vert.glsl" : ".frag.glsl";
        if (manifest != null) glsl = ShaderLabMetadata.Apply(glsl, manifest);
        string pending = output + ".pending" + suffix;
        File.WriteAllText(pending, glsl, new UTF8Encoding(false));
        if (verify) Verify(pending);
        File.Move(pending, output, true);
    }

    private static RefString8 Utf8(string value) => new(Encoding.UTF8.GetBytes(value + '\0'));
}
