using System.Collections.Generic;

namespace RimUnity.ShaderTool;

//离线转换结果同时保存程序路径、材质属性和 ShaderLab 绘制状态。
public sealed class ShaderLabManifest
{
    public int version { get; set; } = 1;
    public string shaderName { get; set; }
    public string profile { get; set; }
    public bool uiMasks { get; set; }
    public string colorSpace { get; set; } = "Gamma";
    public string grabTexture { get; set; }
    public string vertexEntry { get; set; }
    public string fragmentEntry { get; set; }
    public string vertexFile { get; set; } = "shader.vert.glsl";
    public string fragmentFile { get; set; } = "shader.frag.glsl";
    public Dictionary<string, string> arrays { get; set; } = new();
    public List<ShaderLabProperty> properties { get; set; } = new();
    public Dictionary<string, string> tags { get; set; } = new();
    public Dictionary<string, string> states { get; set; } = new();
    public List<string> keywords { get; set; } = new();
    public List<string[]> keywordGroups { get; set; } = new();
    public List<string> approximations { get; set; } = new();
}

//数值默认值保持分量顺序，贴图默认值保留 ShaderLab 中的名称。
public sealed class ShaderLabProperty
{
    public string name { get; set; }
    public string displayName { get; set; }
    public string type { get; set; }
    public string[] attributes { get; set; }
    public float[] defaultValue { get; set; }
    public string defaultTexture { get; set; }
    public float[] range { get; set; }
}

//解析阶段保留源程序，后续适配只替换绑定和明确支持的 Unity 语义。
internal sealed record ShaderLabSource(ShaderLabManifest Manifest, string Program);
