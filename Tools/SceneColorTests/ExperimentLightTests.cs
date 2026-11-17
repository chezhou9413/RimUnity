using System.Numerics;
using System.Reflection;
using Brutal.VulkanApi;
using Waylith.Rendering;
using Waylith.Rendering.PostFx;

internal static partial class Program
{
    private sealed partial class GpuRunner
    {
        internal void CheckExperimentLightComposite()
        {
            var scene = new Vector3(.25f, .5f, .75f);
            var snapshot = Constant(scene);
            // Additive coverage: alpha 0 keeps the background and only adds linear light.
            var addend = new Vector3(.04f, .02f, .01f);
            var additiveLight = ConstantRgba(new Vector4(addend, 0));
            var additiveExpected = Encode(Decode(scene) + addend);
            Check("experiment additive light composite",
                Read(LightComposite(snapshot, additiveLight)), additiveExpected);

            // Transparent coverage: alpha attenuates the decoded scene before add.
            var covered = new Vector3(.01f, .04f, .09f);
            const float alpha = .5f;
            var coverageLight = ConstantRgba(new Vector4(covered, alpha));
            var coverageExpected = Encode(Decode(scene) * (1f - alpha) + covered);
            Check("experiment coverage light composite",
                Read(LightComposite(snapshot, coverageLight)), coverageExpected);

            // Empty light must discard and leave the loaded destination untouched.
            var destination = Constant(scene, VkFormat.B8G8R8A8UNorm);
            var before = ReadPixels(Transfer(destination, 0));
            var zeroLight = ConstantRgba(Vector4.Zero);
            CompositeInto(destination, Transfer(destination, 0), zeroLight);
            var after = ReadPixels(Transfer(destination, 0));
            if (before.Length != after.Length) throw new Exception("experiment empty-light size mismatch");
            for (int i = 0; i < before.Length; i++)
                if (Vector3.Distance(before[i], after[i]) > .001f)
                    throw new Exception($"experiment empty-light pixel {i} changed: {after[i]} vs {before[i]}");
            checks++; Console.WriteLine("PASS experiment empty-light pixels byte-stable (" + before.Length + " texels)");
        }

        private void CheckExperimentBlendAdapt(Assembly runtime)
        {
            const BindingFlags members = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;
            var blendType = runtime.GetType("RimUnity.Rendering.HdrLightBlend", true);
            var tryAdapt = blendType.GetMethod("TryAdapt", members);
            object[] Args(PipelineState state) => new object[] { state, null };

            var additive = new PipelineState(
                new BlendState(true, BlendFactor.SrcAlpha, BlendFactor.One, BlendOp.Add, BlendFactor.One, BlendFactor.One, BlendOp.Add),
                new DepthState(true, true), FaceCull.None);
            var args = Args(additive);
            if (!(bool)tryAdapt.Invoke(null, args)) throw new Exception("additive blend must adapt");
            var adaptedAdd = (PipelineState)args[1];
            if (adaptedAdd.Blend.SrcAlpha != BlendFactor.Zero || adaptedAdd.Blend.DstAlpha != BlendFactor.One || adaptedAdd.Depth.Write)
                throw new Exception("additive adapt must clear alpha coverage and depth write");

            var coverage = new PipelineState(BlendState.Alpha, new DepthState(true, true), FaceCull.None);
            args = Args(coverage);
            if ((bool)tryAdapt.Invoke(null, args))
                throw new Exception("alpha coverage blend must stay on the host to avoid night washout");

            var multiply = new PipelineState(BlendState.MultiplyDestination, new DepthState(false, false), FaceCull.None);
            args = Args(multiply);
            if ((bool)tryAdapt.Invoke(null, args)) throw new Exception("multiply blend must fall back to native drawing");

            var opaque = new PipelineState(BlendState.Opaque, new DepthState(true, true), FaceCull.None);
            args = Args(opaque);
            if ((bool)tryAdapt.Invoke(null, args)) throw new Exception("opaque blend must fall back to native drawing");

            checks++; Console.WriteLine("PASS experiment HdrLightBlend additive-only classification");
        }

        private RenderTexture ConstantRgba(Vector4 value, VkFormat format = VkFormat.R16G16B16A16SFloat)
        {
            var target = Target(format); var pass = Pass(constantFile, 0);
            pass.SetPushConstants(value); pass.Output = target;
            Submit(ctx => pass.Record(in ctx, null)); return target;
        }

        private RenderTexture LightComposite(RenderTexture snapshot, RenderTexture light)
        {
            var target = Target(VkFormat.R16G16B16A16SFloat);
            var pass = new PostEffectPass("ColorTest.LightComposite." + owned.Count,
                Path.Combine(shaders, "HdrLightComposite.frag.glsl"), new[] { 0, 1 },
                new BlendState(true, BlendFactor.One, BlendFactor.Zero, BlendOp.Add, BlendFactor.Zero, BlendFactor.One, BlendOp.Add));
            owned.Add(pass);
            pass.SetInput(0, snapshot); pass.SetInput(1, light); pass.Output = target;
            Submit(ctx => pass.Record(in ctx, null));
            return target;
        }

        //Load the destination, then inline-composite so discarded pixels keep host values.
        private void CompositeInto(RenderTexture destination, RenderTexture snapshot, RenderTexture light)
        {
            var device = app.GraphicsDevice.Device;
            var pass = PostFxPassHelpers.CreateColorRenderPass(device, destination.ColorFormat, loadOp: VkAttachmentLoadOp.Load);
            var blend = new BlendState(true, BlendFactor.One, BlendFactor.Zero, BlendOp.Add, BlendFactor.Zero, BlendFactor.One, BlendOp.Add);
            var effect = new PostEffectPass("ColorTest.LightCompositeLoad." + owned.Count,
                Path.Combine(shaders, "HdrLightComposite.frag.glsl"), new[] { 0, 1 }, blend);
            owned.Add(effect);
            try
            {
                Submit(ctx => {
                    effect.SetInput(0, snapshot); effect.SetInput(1, light);
                    var begin = new VkRenderPassBeginInfo
                    {
                        RenderPass = pass, Framebuffer = destination.GetFramebuffer(pass),
                        RenderArea = new VkRect2D(destination.Extent)
                    };
                    ctx.CommandBuffer.BeginRenderPass(in begin, VkSubpassContents.Inline);
                    try { effect.RecordInline(in ctx, pass, VkSampleCountFlags._1Bit); }
                    finally { ctx.CommandBuffer.EndRenderPass(); }
                    destination.Color.SetMipLayout(0, VkImageLayout.ShaderReadOnlyOptimal);
                });
            }
            finally { device.DestroyRenderPass(pass, device.VkAllocator); }
        }
    }
}
