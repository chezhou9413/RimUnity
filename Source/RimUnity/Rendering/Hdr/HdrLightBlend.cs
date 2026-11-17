using Waylith.Rendering;

namespace RimUnity.Rendering;

//只接管无深度/模板写入副作用的加法材质；其余沿用宿主绘制。
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
        //Captured draws replace the host draw, unlike Bloom's extra light pass.
        //Dropping depth/stencil writes would change later draws' visibility.
        var stencil = state.Stencil;
        if (state.Depth.Write || (stencil.WriteMask != 0 &&
            (stencil.PassOp != StencilOp.Keep || stencil.FailOp != StencilOp.Keep || stencil.DepthFailOp != StencilOp.Keep)))
            return false;
        var additive = blend with { SrcAlpha = BlendFactor.Zero, DstAlpha = BlendFactor.One, AlphaOp = BlendOp.Add };
        adapted = state with { Blend = additive };
        return true;
    }
}
