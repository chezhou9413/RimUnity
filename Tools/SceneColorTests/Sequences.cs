using System.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using RimUnity.Rendering;
using Waylith.Rendering;
using Waylith.Rendering.PostFx;

internal static partial class Program
{
    private sealed partial class GpuRunner
    {
        private static readonly VkFormat[] SceneFormats = {
            VkFormat.R8G8B8A8UNorm, VkFormat.B8G8R8A8UNorm,
            VkFormat.R8G8B8A8SRGB, VkFormat.B8G8R8A8SRGB,
            VkFormat.A2B10G10R10UNormPack32, VkFormat.A2R10G10B10UNormPack32,
            VkFormat.R16G16B16A16UNorm, VkFormat.R16G16B16A16SFloat, VkFormat.R32G32B32A32SFloat
        };

        internal void CheckTransferSequences()
        {
            // Each input differs, so resetting/reusing an already-recorded descriptor
            // set cannot accidentally pass because both draws sampled the same image.
            Vector3[] values = { new(.03f, .07f, .11f), new(.18f, .5f, .85f), new(.91f, .63f, .27f), new(2, 1, .5f) };
            var inputs = values.Select(value => Constant(value)).ToArray();
            var decoded = values.Select(_ => Target(VkFormat.R16G16B16A16SFloat)).ToArray();
            var restored = values.Select(_ => Target(VkFormat.R16G16B16A16SFloat)).ToArray();
            var copied = values.Select(_ => Target(VkFormat.R16G16B16A16SFloat)).ToArray();
            var cleared = Target(VkFormat.R16G16B16A16SFloat);
            using var transfer = new HdrTransferPass("ColorTest.ReusedTransfer");
            for (int repeat = 0; repeat < 4; repeat++)
            {
                int number = ++frame;
                Submit(ctx => {
                    for (int i = 0; i < inputs.Length; i++)
                    {
                        transfer.Copy(in ctx, number, inputs[i], decoded[i], 1);
                        transfer.Copy(in ctx, number, decoded[i], restored[i], -1);
                        transfer.Copy(in ctx, number, inputs[(i + repeat) % inputs.Length], copied[i], 0);
                    }
                    transfer.Copy(in ctx, number, inputs[repeat], cleared, 0, clear: true);
                });
                for (int i = 0; i < inputs.Length; i++)
                {
                    Check($"reused pass frame={repeat} draw={i} decode", Read(decoded[i]), Decode(values[i]), .02f);
                    Check($"reused pass frame={repeat} draw={i} roundtrip", Read(restored[i]), values[i]);
                    Check($"reused pass frame={repeat} draw={i} copy", Read(copied[i]), values[(i + repeat) % values.Length]);
                }
                Check("reused pass clear frame=" + repeat, Read(cleared), Vector3.Zero);
            }
        }

        internal void CheckMapColorChains()
        {
            foreach (var format in SceneFormats)
            {
                // Real production transfer shader + native final composite. Multiple
                // display/linear groups and writebacks are recorded BEFORE submitting.
                var host = Constant(Display, format);
                var reference = Read(Transfer(host, 0));
                var linear = Target(VkFormat.R16G16B16A16SFloat);
                var display = Target(VkFormat.R16G16B16A16SFloat);
                var restored = Target(format);
                var restoredRead = Target(VkFormat.R16G16B16A16SFloat);
                var result = Target(VkFormat.R16G16B16A16SFloat);
                using var transfer = new HdrTransferPass("ColorTest.MapTransfer." + format);
                using var writeback = new HdrTransferPass("ColorTest.Writeback." + format);
                using var inspect = new HdrTransferPass("ColorTest.Inspect." + format);
                using var composite = new PostEffectPass("ColorTest.MapNative." + format,
                    Path.Combine(root, "Rendering/Shaders/PostFX/PostFxComposite.frag.glsl"), new[] { 0 }, uniformBinding: 1);
                var nativeReference = Read(NativeComposite(host));
                foreach (int groups in new[] { 1, 2, 8, 32 })
                {
                    int number = ++frame;
                    Submit(ctx => {
                        transfer.Copy(in ctx, number, host, display, 0);
                        for (int i = 0; i < groups; i++)
                        {
                            transfer.Copy(in ctx, number, display, linear, 1);
                            transfer.Copy(in ctx, number, linear, display, -1);
                        }
                        // End of the map slice: retain a linear result and restore
                        // the host attachment for grabs/native final composition.
                        transfer.Copy(in ctx, number, display, linear, 1);
                        writeback.Copy(in ctx, number, linear, restored, -1);
                        inspect.Copy(in ctx, number, restored, restoredRead, 0);
                        composite.SetInput(0, restored); composite.SetUniforms(1f); composite.Output = result;
                        composite.Record(in ctx, null);
                    });
                    // Repeated half-float store/encode cycles accumulate downward
                    // rounding. Bound that separately; extra encoding brightens by
                    // far more and must never be hidden by a growing tolerance.
                    float tolerance = .008f + groups * .0007f;
                    var actual = Read(restoredRead);
                    Check($"map chain {format} groups={groups} scene", actual, reference, tolerance);
                    Check($"map chain {format} groups={groups} native composite", Read(result), nativeReference, tolerance);
                    if (Vector3.Max(actual - reference, Vector3.Zero).Length() > .008f)
                        throw new Exception($"map chain {format} groups={groups} brightened: {actual} vs {reference}");
                }
            }
        }

        internal void CheckFrameSlots()
        {
            Vector3[] colors = { new(.06f, .15f, .3f), new(.27f, .52f, .82f), new(.94f, .7f, .4f) };
            var inputs = colors.Select(c => Constant(c)).ToArray();
            var linear = colors.Select(_ => Target(VkFormat.R16G16B16A16SFloat)).ToArray();
            var output = colors.Select(_ => Target(VkFormat.R16G16B16A16SFloat)).ToArray();
            using var transfer = new HdrTransferPass("ColorTest.ThreeFrameSlots");
            var device = app.GraphicsDevice.Device;
            var commands = Enumerable.Range(0, 3).Select(_ => device.AllocateCommandBuffer(new VkCommandBufferAllocateInfo {
                CommandPool = pool, Level = VkCommandBufferLevel.Primary })).ToArray();
            var fences = Enumerable.Range(0, 3).Select(_ => device.CreateFence(new VkFenceCreateInfo(), device.VkAllocator)).ToArray();
            try
            {
                for (int round = 0; round < 4; round++)
                {
                    for (int slot = 0; slot < 3; slot++)
                    {
                        context.Slot = slot;
                        var cmd = commands[slot];
                        var ctx = new FrameContext(context, context.FrameResources[slot], cmd, anchorPass,
                            anchor.GetFramebuffer(anchorPass), anchor.Extent, VkSampleCountFlags._1Bit, anchor.ColorFormat, VkFormat.Undefined);
                        int number = ++frame;
                        cmd.Begin(VkCommandBufferUsageFlags.OneTimeSubmitBit);
                        transfer.Copy(in ctx, number, inputs[(slot + round) % 3], linear[slot], 1);
                        transfer.Copy(in ctx, number, linear[slot], output[slot], -1);
                        cmd.End();
                        device.ResetFences(new ReadOnlySpan<VkFence>(in fences[slot]));
                        app.GraphicsDevice.SubmitGraphics(default, default, new Span<CommandBuffer>(ref cmd), default, fences[slot]);
                    }
                    foreach (var pending in fences) device.WaitForFence(pending, -1);
                    for (int slot = 0; slot < 3; slot++)
                        Check($"queued frame slots round={round} slot={slot}", Read(output[slot]), colors[(slot + round) % 3]);
                }
            }
            finally
            {
                device.WaitIdle();
                foreach (var pending in fences) device.DestroyFence(pending, device.VkAllocator);
                context.Slot = 0;
            }
        }
    }
}
