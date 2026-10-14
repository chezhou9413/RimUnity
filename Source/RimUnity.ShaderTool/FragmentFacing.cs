using System.Text.RegularExpressions;

namespace RimUnity.ShaderTool;

//Cg 的 VFACE 是正负浮点值，Vulkan 的正反面输入是布尔系统语义。
internal static class FragmentFacing
{
    internal static string Adapt(string source, string entry)
    {
        return Regex.Replace(source, @"\b((?:float|half|fixed)4\s+" + Regex.Escape(entry)
            + @"\s*\()([^)]*)(\)\s*:\s*SV_Target\d*\s*\{)", method =>
        {
            string local = "";
            string parameters = Regex.Replace(method.Groups[2].Value, @"\b(?:float|half|fixed)\s+(\w+)\s*:\s*VFACE\b", argument =>
            {
                string name = argument.Groups[1].Value;
                local = "\nfloat " + name + " = RU_FrontFace ? 1.0 : -1.0;\n";
                return "bool RU_FrontFace : SV_IsFrontFace";
            });
            return method.Groups[1].Value + parameters + method.Groups[3].Value + local;
        });
    }
}
