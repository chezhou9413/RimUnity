using System;
using System.Text.RegularExpressions;

namespace RimUnity.ShaderTool;

//阴影通道保留原顶点变形与片元裁切，将深度写入可采样的浮点附件。
internal static class ShadowCasterLowering
{
    internal static ShaderLabSource Lower(ShaderLabSource shader)
    {
        var manifest = shader.Manifest;
        string program = shader.Program;
        string entry = manifest.fragmentEntry;
        //显式 ShadowCaster 宏由兼容层写深度，Surface 片元仍先执行原始裁切。
        if (!program.Contains("SHADOW_CASTER_FRAGMENT", StringComparison.Ordinal))
        {
            var signature = Regex.Match(program, @"\b(?:float|half|fixed)4\s+" + Regex.Escape(entry) + @"\s*\(\s*(\w+)\s+(\w+)\s*\)");
            if (!signature.Success) throw new NotSupportedException("自动阴影需要单一结构参数的 Surface 片元入口。");
            string type = signature.Groups[1].Value;
            var structure = Regex.Match(program, @"\bstruct\s+" + Regex.Escape(type) + @"\s*\{([^}]+)\}");
            var position = Regex.Match(structure.Groups[1].Value, @"\b(\w+)\s*:\s*SV_POSITION", RegexOptions.IgnoreCase);
            if (!position.Success) throw new NotSupportedException("阴影片元缺少 SV_POSITION。");
            program += "\nfloat4 RU_ShadowFragment(" + type + " i):SV_Target { " + entry + "(i); return float4(i." + position.Groups[1].Value + ".z,0,0,1); }\n";
            manifest.fragmentEntry = "RU_ShadowFragment";
        }
        manifest.tags["LightMode"] = "ShadowCaster";
        manifest.states["Blend"] = "Off";
        manifest.states["ZWrite"] = "On";
        manifest.states["ZTest"] = "LEqual";
        manifest.states["ColorMask"] = "RGBA";
        return new ShaderLabSource(manifest, program);
    }
}
