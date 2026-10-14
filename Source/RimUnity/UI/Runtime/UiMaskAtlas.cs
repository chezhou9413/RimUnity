using System;
using System.Collections.Generic;

namespace RimUnity.UI.Runtime;

//每个根画布共享遮罩图集，使复杂特效的八层遮罩只占一个贴图绑定。
internal sealed class UiMaskAtlas
{
    private sealed class Region
    {
        internal int x, y, width, height, revision = -1, generation = -1, frame = -1;
        internal int seen;
    }
    private static readonly Dictionary<Canvas, UiMaskAtlas> canvases = new();
    private readonly Dictionary<Texture, Region> regions = new();
    private readonly List<Texture> textures = new();
    private readonly Vector4[] rows = new Vector4[16];
    private readonly ComputeBuffer buffer = new(16, 16);
    private RenderTexture target;
    private ComputeBuffer pixels;
    private bool pixelsDirty = true;
    private int preparedVersion = -1;

    //只在实际绘制带遮罩的自定义材质时分配。
    internal static UiMaskAtlas For(Canvas canvas)
    {
        Canvas root = canvas.rootCanvas;
        if (!canvases.TryGetValue(root, out var atlas)) canvases.Add(root, atlas = new());
        if (atlas.preparedVersion == UguiRuntime.PreparationVersion) return atlas;
        atlas.preparedVersion = UguiRuntime.PreparationVersion;
        bool changed = false;
        foreach (var entry in UguiRuntime.Entries)
        {
            if (entry.Canvas.rootCanvas != root) continue;
            foreach (var mask in entry.Masks)
            {
                Texture texture = mask.graphic switch
                {
                    Image image => (image.overrideSprite ?? image.sprite)?.texture ?? Texture2D.whiteTexture,
                    RawImage raw => raw.texture ?? Texture2D.whiteTexture,
                    _ => throw new NotSupportedException("Mask 需要 Image 或 RawImage。")
                };
                if (!atlas.regions.ContainsKey(texture))
                {
                    atlas.regions.Add(texture, new()); atlas.textures.Add(texture); changed = true;
                }
                Region region = atlas.regions[texture];
                region.seen = atlas.preparedVersion;
                if (region.width != texture.width || region.height != texture.height) changed = true;
            }
        }
        //切换页面后移除不再参与遮罩的图片，避免图集只增不减。
        for (int i = atlas.textures.Count - 1; i >= 0; i--)
        {
            var texture = atlas.textures[i];
            if (atlas.regions[texture].seen == atlas.preparedVersion) continue;
            atlas.regions.Remove(texture); atlas.textures.RemoveAt(i); changed = true;
        }
        if (changed) atlas.Pack();
        return atlas;
    }

    //按行排布原尺寸图片，像素复制不经过 CPU 解码。
    private void Pack()
    {
        textures.RemoveAll(t => t == null);
        textures.Sort((a, b) => b.height.CompareTo(a.height));
        long area = 0; int widest = 1;
        foreach (var texture in textures) { area += (long)texture.width * texture.height; widest = Math.Max(widest, texture.width); }
        int limit = Waylith.Graphics.GpuInfo.MaxTextureSize;
        int width = Math.Min(limit, Math.Max(widest, (int)Math.Ceiling(Math.Sqrt(area))));
        int x = 0, y = 0, rowHeight = 0, usedWidth = 1;
        foreach (var texture in textures)
        {
            if (texture.width > limit || texture.height > limit) throw new NotSupportedException("遮罩贴图超过设备尺寸上限：" + texture.name);
            if (x + texture.width > width) { x = 0; y += rowHeight; rowHeight = 0; }
            var region = regions[texture]; region.x = x; region.y = y;
            region.width = texture.width; region.height = texture.height; region.revision = region.generation = region.frame = -1;
            x += texture.width; rowHeight = Math.Max(rowHeight, texture.height); usedWidth = Math.Max(usedWidth, x);
        }
        int height = Math.Max(1, y + rowHeight);
        if (height > limit) throw new NotSupportedException("画布的遮罩图集超过设备尺寸上限。");
        if (target != null) Object.Destroy(target);
        pixels?.Dispose(); pixels = null; pixelsDirty = true;
        target = new RenderTexture(usedWidth, height, 0, (int)UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm)
        { name = "RimUnity UI Mask Atlas", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        if (!target.Target.Create()) throw new InvalidOperationException("无法创建 UI 遮罩图集。");
    }

    //静态图片按版本刷新，外部贴图和视频附件每帧最多复制一次。
    internal void SetRegion(int index, Texture texture)
    {
        Region region = regions[texture];
        var native = texture.NativeTexture;
        if (!native.EnsureResident()) throw new InvalidOperationException("遮罩贴图无法上传：" + texture.name);
        int revision = texture is Texture2D image && !image.isExternallyOwned ? image.contentRevision : Time.frameCount;
        if (region.revision != revision || region.generation != native.ResidencyGeneration)
        {
            Waylith.Rendering.GpuTextureService.BlitTexture(native.GpuResource, texture.width, texture.height,
                0, 0, 0, texture.width, texture.height, target.Target.GpuTexture, 0, region.x, region.y,
                region.width, region.height, Brutal.VulkanApi.VkFilter.Nearest);
            region.revision = revision; region.generation = native.ResidencyGeneration;
            pixelsDirty = true;
        }
        rows[index * 2] = new(region.x, region.y, region.width, region.height);
        rows[index * 2 + 1] = new((int)texture.filterMode, (int)texture.wrapMode, target.width, 0);
    }

    //材质提交会快照缓冲区，因此同一个图集可以用于不同遮罩组合。
    internal void Apply(Material material)
    {
        buffer.SetData(rows); material.SetBuffer("_UiMaskRegions", buffer);
        if (material.HasProperty("_UiMaskPixels"))
        {
            PreparePixels(); material.SetBuffer("_UiMaskPixels", pixels);
        }
        if (material.HasProperty("_UiMaskAtlas")) material.SetTexture("_UiMaskAtlas", target);
    }

    //只有用满纹理槽的材质需要透明度缓冲；静态图集生成一次，按实际内容变化刷新。
    private void PreparePixels()
    {
        if (!pixelsDirty) return;
        byte[] rgba = Waylith.Rendering.GpuTextureService.ReadbackTexture2D(target.Target.GpuTexture, target.width, target.height, 4)
            ?? throw new InvalidOperationException("无法读取 UI 遮罩透明度。");
        int count = checked(target.width * target.height);
        var packed = new uint[(count + 3) / 4];
        for (int i = 0; i < count; i++) packed[i / 4] |= (uint)rgba[i * 4 + 3] << ((i % 4) * 8);
        pixels ??= new ComputeBuffer(packed.Length, 4);
        pixels.SetData(packed); pixelsDirty = false;
    }

    //随画布释放缓存和 GPU 附件。
    internal static void Release(Canvas canvas)
    {
        if (!canvases.Remove(canvas, out var atlas)) return;
        if (atlas.target != null) Object.Destroy(atlas.target);
        atlas.buffer.Dispose();
        atlas.pixels?.Dispose();
    }
}
