using Waylith.Rendering;

namespace RimUnity.Rendering;

//加法线性材质进入光缓冲；深度/模板状态沿用原材质。
internal static class HdrLightBlend
{
    internal static bool ShouldCapture(FinalDrawItem draw)
        => IsMap(draw) && LinearShaderVariants.TryGet(draw.Binding.Shader, out _)
            && TryAdapt(draw.Binding.PipelineState, out _);

    internal static bool IsMap(FinalDrawItem draw)
        => draw.PassId == 0 || draw.PassId == HdrDrawFrame.MapPass;

    //只收真正的加法混合。Alpha 覆盖（烟、软光晕、建筑精灵等）必须留在宿主：
    //若先在光缓冲里互相覆盖再一次性写回，重叠区域会把夜晚场景整块抬亮。
    internal static bool TryAdapt(PipelineState state, out PipelineState adapted)
    {
        adapted = state;
        var blend = state.Blend;
        if (!blend.Enabled || blend.ColorOp != BlendOp.Add) return false;
        if (blend.DstColor != BlendFactor.One || blend.SrcColor is not (BlendFactor.One or BlendFactor.SrcAlpha))
            return false;
        //The light pass shares the host depth/stencil attachment and replaces the
        //original draw in order. Preserve its writes instead of disabling capture
        //for ZWrite materials (ShaderLab's default), or dropping those writes.
        var additive = blend with { SrcAlpha = BlendFactor.Zero, DstAlpha = BlendFactor.One, AlphaOp = BlendOp.Add };
        adapted = state with { Blend = additive };
        return true;
    }
}
