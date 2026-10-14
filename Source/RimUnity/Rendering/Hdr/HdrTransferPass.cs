using System;
using System.IO;
using System.Numerics;
using Waylith.Rendering;
using Waylith.Rendering.PostFx;

namespace RimUnity.Rendering;

//同一帧的多次转换各自保留描述符，不能重置仍被前序命令使用的池。
internal sealed class HdrTransferPass : PostEffectPass
{
    private const int Capacity = 4096;
    private int frame = -1, count;
    //一个转换通道重复使用描述符池，按在途帧隔离。
    internal HdrTransferPass(string name) : base(name, Path.Combine(RimUnityMod.ShaderDirectory, "HdrTransfer.frag.glsl"),
        new[] { 0 }, pushConstantSize: 16, maxPassesPerFrame: Capacity) { }

    //编码方向为负，解码为正，零只拷贝；清屏不采样源颜色。
    internal void Copy(in FrameContext ctx, int frameNumber, Waylith.Rendering.RenderTexture source,
        Waylith.Rendering.RenderTexture target, float conversion, bool clear = false)
    {
        EnsureInitialized(in ctx, target.ColorFormat);
        if (frame != frameNumber) { BeginFrame(ctx.SubmissionFrameIndex); frame = frameNumber; count = 0; }
        if (++count > Capacity) throw new InvalidOperationException("RimUnity HDR 单帧颜色空间切换超过 4096 次。");
        SetInput(0, source); SetPushConstants(new Vector4(conversion, clear ? 1 : 0, 0, 0));
        RecordPass(ctx.CommandBuffer, ctx.SubmissionFrameIndex, target);
    }

    //热重载后描述符池已经重建，下一次拷贝必须重新开始计数。
    public override void Invalidate()
    {
        base.Invalidate(); frame = -1; count = 0;
    }
}
