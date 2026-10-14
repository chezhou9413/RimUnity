using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using RimUnity;
using RimUnity.UI;

namespace Spine.Unity;

//逐槽保留动画绘制顺序，蒙皮、附件切换与裁剪均使用 Spine 核心数据。
public sealed partial class SkeletonGraphic
{
    private static readonly int[] Quad = { 0, 1, 2, 2, 3, 0 };
    private readonly SkeletonClipping clipping = new();
    private readonly List<GuiVertex> triangles = new();
    private float[] world = new float[32];

    protected override void PopulateMesh(UiMeshWriter output)
    {
        if (skeleton == null) return;
        float scale = MeshScale;
        triangles.Clear();
        Texture batchTexture = null;
        Material batchMaterial = null;
        try
        {
            foreach (Slot slot in skeleton.DrawOrder)
            {
                if (!slot.Bone.Active) { clipping.ClipEnd(slot); continue; }
                Attachment attachment = slot.Attachment;
                if (attachment is ClippingAttachment clip) { clipping.ClipStart(slot, clip); continue; }
                float[] uv; int[] indices; int count;
                AtlasRegion atlasRegion;
                Color color;
                if (attachment is RegionAttachment region)
                {
                    region.ComputeWorldVertices(slot, world, 0);
                    uv = region.UVs; indices = Quad; count = Quad.Length;
                    atlasRegion = (AtlasRegion)region.Region;
                    color = new Color(region.R, region.G, region.B, region.A);
                }
                else if (attachment is MeshAttachment mesh)
                {
                    if (world.Length < mesh.WorldVerticesLength) Array.Resize(ref world, mesh.WorldVerticesLength);
                    mesh.ComputeWorldVertices(slot, 0, mesh.WorldVerticesLength, world, 0);
                    uv = mesh.UVs; indices = mesh.Triangles; count = indices.Length;
                    atlasRegion = (AtlasRegion)mesh.Region;
                    color = new Color(mesh.R, mesh.G, mesh.B, mesh.A);
                }
                else { clipping.ClipEnd(slot); continue; }
                color *= new Color(skeleton.R * slot.R, skeleton.G * slot.G, skeleton.B * slot.B, skeleton.A * slot.A);
                if (pmaVertexColors) { color.r *= color.a; color.g *= color.a; color.b *= color.a; }
                Material selected = slot.Data.BlendMode switch
                {
                    BlendMode.Normal => material,
                    BlendMode.Additive when pmaVertexColors => material,
                    BlendMode.Additive => additiveMaterial,
                    BlendMode.Multiply => multiplyMaterial,
                    BlendMode.Screen => screenMaterial,
                    _ => throw new NotSupportedException("未知 Spine 混合模式。")
                };
                if (!selected) throw new InvalidOperationException("Spine 槽缺少对应材质：" + slot.Data.Name);
                if (pmaVertexColors && slot.Data.BlendMode == BlendMode.Additive) color.a = 0;
                float[] positions = world;
                if (clipping.IsClipping)
                {
                    clipping.ClipTriangles(world, indices, count, uv);
                    positions = clipping.ClippedVertices.Items;
                    uv = clipping.ClippedUVs.Items;
                    indices = clipping.ClippedTriangles.Items;
                    count = clipping.ClippedTriangles.Count;
                }
                var pageTexture = (Texture)atlasRegion.page.rendererObject;
                if (triangles.Count > 0 && (pageTexture != batchTexture || selected != batchMaterial))
                {
                    output.Submit(CollectionsMarshal.AsSpan(triangles), batchTexture, batchMaterial, pmaVertexColors);
                    triangles.Clear();
                }
                batchTexture = pageTexture; batchMaterial = selected;
                for (int i = 0; i < count; i++)
                {
                    int v = indices[i] * 2;
                    triangles.Add(new GuiVertex { position = new Vector2(positions[v], positions[v + 1]) * scale + pivotOffset,
                        uv = new Vector2(uv[v], uv[v + 1]), color = color });
                }
                clipping.ClipEnd(slot);
            }
            if (triangles.Count > 0) output.Submit(CollectionsMarshal.AsSpan(triangles), batchTexture, batchMaterial, pmaVertexColors);
        }
        finally { clipping.ClipEnd(); }
    }
}
