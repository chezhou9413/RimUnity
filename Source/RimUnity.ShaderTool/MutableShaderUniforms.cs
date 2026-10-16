using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace RimUnity.ShaderTool;

//Cg 允许在单次着色调用中改写参数副本，常量缓冲本身仍保持只读。
internal static class MutableShaderUniforms
{
    //把实际发生赋值的参数复制到每次入口调用初始化的私有变量。
    internal static string Adapt(string source, Dictionary<string, string> uniforms, ShaderLabManifest manifest, StringBuilder header)
    {
        var initialize = new StringBuilder();
        foreach (var pair in uniforms)
        {
            string token = @"\b" + Regex.Escape(pair.Key) + @"\b";
            if (!Regex.IsMatch(source, token + @"(?:\.[xyzwrgba]+)?\s*(?:[+*/-]?=(?!=)|\+\+|--)")) continue;
            string local = "RU_Mutable" + pair.Key;
            source = Regex.Replace(source, token, local);
            header.AppendLine("static " + pair.Value + " " + local + ";");
            initialize.Append(local + " = " + pair.Key + ";\n");
        }
        if (initialize.Length == 0) return source;
        foreach (string entry in new[] { manifest.vertexEntry, manifest.fragmentEntry })
            source = Regex.Replace(source, @"(\b" + Regex.Escape(entry) + @"\s*\([^)]*\)\s*(?::\s*\w+)?\s*\{)",
                m => m.Value + "\n" + initialize);
        return source;
    }
}
