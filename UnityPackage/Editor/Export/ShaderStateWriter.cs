using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using RimUnity.Serialization;
using UnityEngine;

namespace RimUnity.Editor
{
    //ShaderLab 状态独立于 GLSL，按材质中的属性值生成确定的管线描述。
    internal static class ShaderStateWriter
    {
        internal static ShaderData Write(Material material, JObject manifest, bool ui, string path)
        {
            var states = (JObject)manifest["states"];
            string Value(string key, string fallback)
            {
                string value = (string)states[key] ?? fallback;
                return Regex.Replace(value, @"\[\s*(\w+)\s*\]", match =>
                {
                    string property = match.Groups[1].Value;
                    //Unity 在 Overlay 画布中把这个全局状态设为 Always，它不是材质属性。
                    if (property == "unity_GUIZTestMode") return ui ? "8" : "4";
                    if (!material.HasProperty(property)) throw new InvalidOperationException(path + "：渲染状态引用不存在的属性：" + property);
                    return material.GetFloat(property).ToString(CultureInfo.InvariantCulture);
                });
            }
            string EnumValue(string value, Type unityType)
            {
                if (int.TryParse(value, out int numeric)) return Enum.GetName(unityType, numeric) ?? throw new NotSupportedException(path + "：未知渲染状态值 " + value);
                return value;
            }
            bool On(string value) => value.Equals("On", StringComparison.OrdinalIgnoreCase) || value == "1" ? true :
                value.Equals("Off", StringComparison.OrdinalIgnoreCase) || value == "0" ? false : throw new NotSupportedException(path + "：状态必须为 On／Off：" + value);
            string Check(string value, params string[] choices) => choices.FirstOrDefault(c => c.Equals(value, StringComparison.OrdinalIgnoreCase))
                ?? throw new NotSupportedException(path + "：Waylith 当前提交路径不能表达状态：" + value);
            string Factor(string value) => Check(EnumValue(value, typeof(UnityEngine.Rendering.BlendMode)), "Zero", "One", "SrcAlpha", "OneMinusSrcAlpha", "SrcColor", "DstColor", "OneMinusSrcColor", "OneMinusDstColor");
            string Op(string value)
            {
                value = EnumValue(value, typeof(UnityEngine.Rendering.BlendOp));
                if (value.Equals("RevSub", StringComparison.OrdinalIgnoreCase)) value = "ReverseSubtract";
                if (value.Equals("Sub", StringComparison.OrdinalIgnoreCase)) value = "Subtract";
                return Check(value, "Add", "Subtract", "ReverseSubtract", "Min", "Max");
            }
            string Compare(string value)
            {
                value = EnumValue(value, typeof(UnityEngine.Rendering.CompareFunction));
                if (value.Equals("LEqual", StringComparison.OrdinalIgnoreCase)) value = "LessEqual";
                if (value.Equals("GEqual", StringComparison.OrdinalIgnoreCase)) value = "GreaterEqual";
                if (value == "LessEqual") value = "LessOrEqual";
                if (value == "GreaterEqual") value = "GreaterOrEqual";
                return Check(value, "Disabled", "Never", "Less", "Equal", "LessOrEqual", "Greater", "NotEqual", "GreaterOrEqual", "Always");
            }
            string StencilOp(string value)
            {
                value = EnumValue(value, typeof(UnityEngine.Rendering.StencilOp));
                string[] source = { "IncrSat", "DecrSat", "IncrWrap", "DecrWrap" };
                string[] dest = { "IncrementClamp", "DecrementClamp", "IncrementWrap", "DecrementWrap" };
                for (int i = 0; i < source.Length; i++) if (value.Equals(source[i], StringComparison.OrdinalIgnoreCase)) value = dest[i];
                return Check(value, "Keep", "Zero", "Replace", "IncrementClamp", "DecrementClamp", "Invert", "IncrementWrap", "DecrementWrap");
            }
            var data = new ShaderData { sourceName = material.shader.name, profile = ui ? "ui" : "world", keywords = manifest["keywords"].ToObject<string[]>(), renderQueue = material.renderQueue };
            data.approximations = manifest["approximations"]?.ToObject<string[]>() ?? Array.Empty<string>();
            data.grabTexture = (string)manifest["grabTexture"];
            data.colorSpace = (string)manifest["colorSpace"] ?? "Gamma";
            if (!string.IsNullOrEmpty(data.grabTexture)) data.renderQueue++;
            string cull = EnumValue(Value("Cull", "Back"), typeof(UnityEngine.Rendering.CullMode));
            data.cull = cull.Equals("Off", StringComparison.OrdinalIgnoreCase) ? "None" : Check(cull, "Front", "Back");
            string depth = Compare(Value("ZTest", "LEqual"));
            //宿主固定使用包含等深度的比较；透明拖尾保留遮挡，仅等深度边界存在差异。
            if (depth == "Less")
            {
                data.approximations = data.approximations.Concat(new[] { "ZTest Less 使用宿主 LEqual；恰好等深度的片元也会通过。" }).ToArray();
                depth = "LessOrEqual";
            }
            data.depthTest = depth != "Always" && depth != "Disabled";
            if (depth != "Always" && depth != "Disabled" && depth != "LessOrEqual") throw new NotSupportedException(path + "：Waylith 深度状态只能表达 LEqual／Always／Disabled。");
            data.depthWrite = depth != "Disabled" && On(Value("ZWrite", "On"));
            if (ui && data.depthWrite) throw new NotSupportedException(path + "：Overlay 自定义 Shader 必须使用 ZWrite Off。");
            if (ui && data.depthTest)
            {
                //屏幕 UI 没有场景深度附件，按界面提交顺序合成；显式记录与世界配置的差别。
                data.approximations = data.approximations.Concat(new[] { "Overlay UI 不使用场景深度，ZTest LEqual 按 Always 绘制。" }).ToArray();
                data.depthTest = false;
            }
            if (!On(Value("ZClip", "On")) || On(Value("AlphaToMask", "Off"))) throw new NotSupportedException(path + "：ZClip Off／AlphaToMask 尚未接通。");
            string colorMask = Check(Value("ColorMask", "RGBA"), "RGBA", "15", "RGB", "14", "0");
            data.colorWrite = colorMask != "0";
            string blend = Value("Blend", "Off");
            if (!blend.Equals("Off", StringComparison.OrdinalIgnoreCase))
            {
                data.blend = true;
                var pairs = blend.Split(',');
                string[] color = pairs[0].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                string[] alpha = pairs.Length == 2 ? pairs[1].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries) : color;
                if (pairs.Length > 2 || color.Length != 2 || alpha.Length != 2) throw new InvalidOperationException(path + "：Blend 参数数量错误。");
                data.srcColor = Factor(color[0]); data.dstColor = Factor(color[1]); data.srcAlpha = Factor(alpha[0]); data.dstAlpha = Factor(alpha[1]);
            }
            var ops = Value("BlendOp", "Add").Split(',');
            data.colorOp = Op(ops[0].Trim()); data.alphaOp = Op(ops.Length == 2 ? ops[1].Trim() : ops[0].Trim());
            //宿主只有整体颜色写入开关；独立 Alpha 混合严格保留目标 Alpha 来表达 RGB Mask。
            if (colorMask == "RGB" || colorMask == "14")
            {
                if (!data.blend) { data.blend = true; data.srcColor = "One"; data.dstColor = "Zero"; data.colorOp = "Add"; }
                data.srcAlpha = "Zero"; data.dstAlpha = "One"; data.alphaOp = "Add";
            }
            var offset = Value("Offset", "0, 0").Split(',');
            data.depthBiasSlope = float.Parse(offset[0], CultureInfo.InvariantCulture); data.depthBiasConstant = float.Parse(offset[1], CultureInfo.InvariantCulture);
            data.stencil = states.Properties().Any(p => p.Name.StartsWith("Stencil.", StringComparison.OrdinalIgnoreCase));
            if (data.stencil)
            {
                if (states.Properties().Any(p => p.Name.StartsWith("Stencil.") && (p.Name.EndsWith("Front") || p.Name.EndsWith("Back")))) throw new NotSupportedException(path + "：双面 Stencil 尚未接通。");
                int ByteValue(string key, string fallback) { int n = int.Parse(Value(key, fallback), CultureInfo.InvariantCulture); if (n < 0 || n > 255) throw new NotSupportedException(path + "：Stencil 参数超出 0～255。"); return n; }
                data.stencilRef = ByteValue("Stencil.Ref", "0"); data.stencilReadMask = ByteValue("Stencil.ReadMask", "255"); data.stencilWriteMask = ByteValue("Stencil.WriteMask", "255");
                data.stencilCompare = Compare(Value("Stencil.Comp", "Always")); data.stencilPass = StencilOp(Value("Stencil.Pass", "Keep"));
                data.stencilFail = StencilOp(Value("Stencil.Fail", "Keep")); data.stencilZFail = StencilOp(Value("Stencil.ZFail", "Keep"));
                if (data.stencilCompare == "Disabled") { data.stencil = false; data.stencilCompare = "Always"; }
                //Unity UI 的默认 Stencil 块为 Always/Keep，没有遮罩读写，等价于关闭。
                if (data.stencilCompare == "Always" && data.stencilPass == "Keep" && data.stencilFail == "Keep" && data.stencilZFail == "Keep") data.stencil = false;
                if (ui && data.stencil) throw new NotSupportedException(path + "：Overlay 提交没有 Stencil 附件，自定义 Stencil 程序尚未转换成覆盖率遮罩。");
            }
            return data;
        }
    }
}
