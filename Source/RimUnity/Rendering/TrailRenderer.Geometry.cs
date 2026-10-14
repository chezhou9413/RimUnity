using System;
using System.Collections.Generic;

namespace RimUnity;

//条带共享内侧接缝，圆角和端帽使用额外三角形，避免透明接头重复叠画。
public sealed partial class TrailRenderer
{
    private Mesh batch;
    private readonly List<Vector3> vertices = new(), normals = new();
    private readonly List<Vector4> tangents = new();
    private readonly List<Vector2> uv = new();
    private readonly List<Color> colors = new();
    private readonly List<int> indices = new();
    private struct Section { internal Vector3 center, side, direction, facing; internal float halfWidth, u; internal Color color; internal int inMinus, inPlus, outMinus, outPlus; }
    private readonly List<Section> sections = new();

    internal void Draw(Camera camera)
    {
        if (!isActiveAndEnabled || sharedMaterials.Length == 0) return;
        VisiblePoints(); if (visible.Count < 2) return;
        Build(camera);
        if (indices.Count == 0) return;
        batch ??= new Mesh { name = "RimUnity TrailRenderer", indexFormat = IndexFormat.UInt32 };
        FillMesh(batch);
        foreach (Material material in sharedMaterials)
        {
            if (material == null) continue;
            if (Diagnostics.ParticleDebugWindow.Capturing)
            {
                Diagnostics.RendererDebugCapture.Capture(this, batch, material, "TrailRenderer", widthMultiplier, colors);
            }
            RimUnity.Graphics.DrawMesh(batch, Matrix4x4.identity, material, gameObject.layer);
        }
    }

    private void Build(Camera camera)
    {
        vertices.Clear(); normals.Clear(); tangents.Clear(); uv.Clear(); colors.Clear(); indices.Clear(); sections.Clear();
        if (numCornerVertices < 0 || numCapVertices < 0) throw new InvalidOperationException("轨迹圆角与端帽顶点数不能为负。");
        float total = 0, distance = 0;
        for (int i = 1; i < visible.Count; i++) total += Vector3.Distance(visible[i - 1].position, visible[i].position);
        for (int i = 0; i < visible.Count; i++)
        {
            Vector3 center = visible[i].position;
            Vector3 before = i > 0 ? (center - visible[i - 1].position).normalized : (visible[1].position - center).normalized;
            Vector3 after = i + 1 < visible.Count ? (visible[i + 1].position - center).normalized : before;
            if (i > 0) distance += Vector3.Distance(visible[i - 1].position, center);
            float t = total > 0 ? distance / total : 0;
            Vector3 facing = alignment == LineAlignment.TransformZ ? transform.forward
                : camera == null ? Vector3.forward : camera.orthographic ? camera.transform.forward : (center - camera.transform.position).normalized;
            Vector3 sideBefore = Vector3.Cross(before, facing).normalized, sideAfter = Vector3.Cross(after, facing).normalized;
            if (sideBefore.sqrMagnitude < 1e-10f) sideBefore = camera != null ? camera.transform.right : transform.right;
            if (sideAfter.sqrMagnitude < 1e-10f) sideAfter = sideBefore;
            Vector3 miter = (sideBefore + sideAfter).normalized;
            if (miter.sqrMagnitude < 1e-10f) miter = sideAfter;
            float half = Mathf.Max(0, widthCurve.Evaluate(t) * widthMultiplier) * .5f;
            float u = textureMode switch
            {
                LineTextureMode.Stretch => t,
                LineTextureMode.Tile => distance,
                LineTextureMode.DistributePerSegment => i / (float)(visible.Count - 1),
                LineTextureMode.RepeatPerSegment => i,
                LineTextureMode.Static => visible[i].distance,
                _ => throw new NotSupportedException("不支持的轨迹贴图模式：" + textureMode)
            };
            var section = new Section { center = center, facing = facing, side = sideAfter, direction = after, halfWidth = half, u = u, color = colorGradient.Evaluate(t) };
            float miterLength = half / Mathf.Max(.25f, Mathf.Abs(Vector3.Dot(miter, sideAfter)));
            float turn = Vector3.Dot(Vector3.Cross(before, after), facing);
            if (i > 0 && i + 1 < visible.Count && numCornerVertices > 0 && Mathf.Abs(turn) > .0001f)
                RoundCorner(ref section, sideBefore, sideAfter, miter * miterLength, turn);
            else
            {
                section.inMinus = section.outMinus = Vertex(section, center - miter * miterLength, 0);
                section.inPlus = section.outPlus = Vertex(section, center + miter * miterLength, 1);
            }
            sections.Add(section);
        }
        for (int i = 1; i < sections.Count; i++)
        {
            Section a = sections[i - 1], b = sections[i];
            Triangle(a.outMinus, a.outPlus, b.inPlus); Triangle(a.outMinus, b.inPlus, b.inMinus);
        }
        if (numCapVertices > 0) { Cap(sections[0], true); Cap(sections[^1], false); }
    }

    //外侧圆弧使用 numCornerVertices 个中间顶点，内侧接缝保持单一顶点。
    private void RoundCorner(ref Section section, Vector3 before, Vector3 after, Vector3 miter, float turn)
    {
        float sign = turn > 0 ? 1 : -1;
        int inner = Vertex(section, section.center - miter * sign, sign > 0 ? 0 : 1);
        Vector3 start = before * sign;
        float angle = Mathf.Atan2(Vector3.Dot(Vector3.Cross(before, after), section.facing), Vector3.Dot(before, after)) * Mathf.Rad2Deg;
        int first = -1, previous = -1;
        for (int i = 0; i <= numCornerVertices + 1; i++)
        {
            Vector3 side = Quaternion.AngleAxis(angle * i / (numCornerVertices + 1), section.facing) * start;
            int current = Vertex(section, section.center + side * section.halfWidth, sign > 0 ? 1 : 0);
            if (i == 0) first = current;
            else if (sign > 0) Triangle(inner, previous, current); else Triangle(inner, current, previous);
            previous = current;
        }
        if (sign > 0) { section.inMinus = section.outMinus = inner; section.inPlus = first; section.outPlus = previous; }
        else { section.inPlus = section.outPlus = inner; section.inMinus = first; section.outMinus = previous; }
    }

    //半圆端帽沿轨迹外侧展开，不在条带覆盖区域叠加半个圆盘。
    private void Cap(Section section, bool head)
    {
        Vector3 outward = (head ? -1 : 1) * section.direction;
        int center = Vertex(section, section.center, .5f), previous = -1;
        for (int i = 0; i <= numCapVertices + 1; i++)
        {
            float angle = Mathf.PI * i / (numCapVertices + 1);
            Vector3 offset = (-section.side * Mathf.Cos(angle) + outward * Mathf.Sin(angle)) * section.halfWidth;
            int current = Vertex(section, section.center + offset, .5f - .5f * Mathf.Cos(angle));
            if (i > 0) { if (head) Triangle(center, previous, current); else Triangle(center, current, previous); }
            previous = current;
        }
    }
    private int Vertex(Section section, Vector3 point, float across)
    {
        int index = vertices.Count; vertices.Add(point); colors.Add(section.color);
        uv.Add(new Vector2(section.u * textureScale.x, across * textureScale.y)); normals.Add(-section.facing);
        tangents.Add(new Vector4(section.direction.x, section.direction.y, section.direction.z, 1)); return index;
    }
    private void Triangle(int a, int b, int c) { indices.Add(a); indices.Add(b); indices.Add(c); }
    private void FillMesh(Mesh mesh)
    {
        mesh.Clear(); mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetUVs(0, uv); mesh.SetTriangles(indices, 0);
        if (generateLightingData) { mesh.SetNormals(normals); mesh.SetTangents(tangents); }
        mesh.RecalculateBounds();
    }
}
