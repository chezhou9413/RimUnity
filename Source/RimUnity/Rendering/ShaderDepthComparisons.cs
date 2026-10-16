using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Brutal.VulkanApi;
using HarmonyLib;
using Waylith.Rendering;

namespace RimUnity.Rendering;

//仅为 RimUnity 注册的程序补齐深度比较，其余游戏管线保持宿主默认行为。
internal static class ShaderDepthComparisons
{
    private sealed record Comparison(VkCompareOp Value);
    private static readonly ConditionalWeakTable<ShaderProgramDefinition, Comparison> Comparisons = new();

    //Unity 比较名按反向 Z 翻转；共享 Program 的 HDR 变体自动沿用同一规则。
    internal static void Register(ShaderProgramDefinition program, string name)
    {
        VkCompareOp comparison = name switch
        {
            "Less" => VkCompareOp.Greater, "LessOrEqual" => VkCompareOp.GreaterOrEqual,
            "Greater" => VkCompareOp.Less, "GreaterOrEqual" => VkCompareOp.LessOrEqual,
            "Equal" => VkCompareOp.Equal, "NotEqual" => VkCompareOp.NotEqual,
            "Never" => VkCompareOp.Never, "Always" or "Disabled" => VkCompareOp.Always,
            _ => throw new NotSupportedException("未知深度比较：" + name)
        };
        Comparisons.Remove(program); Comparisons.Add(program, new Comparison(comparison));
    }

    //不访问设备，仅检查当前宿主 IL 是否仍有唯一的深度比较写入点。
    internal static void ValidateHost()
        => Rewrite(PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(MeshPipelineCache), "GetOrCreatePipeline"))).ToArray();

    //管线缓存按 ShaderVariantId 区分，不会把不同程序的比较状态混在一起。
    private static VkCompareOp Resolve(VkCompareOp original, MaterialBinding material)
        => Comparisons.TryGetValue(material.Shader.Program, out Comparison value) ? value.Value : original;

    //在原有比较值和写入操作之间插入选择器，不替换深度附件或原生渲染流程。
    private static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> source)
    {
        var instructions = source.ToList();
        int count = 0;
        foreach (var instruction in instructions)
        {
            bool write = instruction.opcode == OpCodes.Stfld && instruction.operand is FieldInfo field
                && field.DeclaringType == typeof(VkPipelineDepthStencilStateCreateInfo) && field.Name == "DepthCompareOp";
            write |= instruction.operand is MethodInfo method && method.DeclaringType == typeof(VkPipelineDepthStencilStateCreateInfo) && method.Name == "set_DepthCompareOp";
            if (write)
            {
                count++;
                var material = new CodeInstruction(OpCodes.Ldarg_3);
                material.labels.AddRange(instruction.labels); instruction.labels.Clear();
                yield return material;
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ShaderDepthComparisons), nameof(Resolve)));
            }
            yield return instruction;
        }
        if (count != 1) throw new InvalidOperationException("Waylith 深度比较写入点数量变化：" + count + "，需要更新适配。");
    }

    //框架初始化时对实际宿主管线入口打补丁。
    [HarmonyPatch(typeof(MeshPipelineCache), "GetOrCreatePipeline")]
    private static class PipelinePatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => Rewrite(instructions);
    }
}
