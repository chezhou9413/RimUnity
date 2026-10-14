using System;
using System.Collections.Generic;

namespace RimUnity;

//轨迹记录世界位置和出生时间，预览使用独立的非缩放时钟。
public sealed partial class TrailRenderer : MonoBehaviour
{
    internal static readonly HashSet<TrailRenderer> Active = new();
    public Material[] sharedMaterials = Array.Empty<Material>();
    public float time = 5, minVertexDistance = .1f, widthMultiplier = 1;
    public bool emitting = true, autodestruct, generateLightingData;
    public int numCornerVertices, numCapVertices, sortingOrder;
    public LineAlignment alignment;
    public LineTextureMode textureMode;
    public Vector2 textureScale = Vector2.one;
    public AnimationCurve widthCurve = AnimationCurve.Constant(0, 1, 1);
    public Gradient colorGradient = new();
    public Material sharedMaterial { get => sharedMaterials.Length > 0 ? sharedMaterials[0] : null; set => sharedMaterials = new[] { value }; }
    private readonly List<Material> ownedMaterials = new();
    private readonly List<TrailPoint> points = new();
    private readonly List<TrailPoint> visible = new();
    private readonly BridgeTime clock = new();
    private float elapsed, emittedDistance;
    private bool hadPoints;
    private struct TrailPoint { internal Vector3 position; internal float birth, distance; }
    public int positionCount => points.Count;

    //实例材质保留转换器登记的全部 Shader Pass。
    public Material material
    {
        get
        {
            Material value = sharedMaterial;
            if (value == null || ownedMaterials.Contains(value)) return value;
            Material copy = new(value);
            sharedMaterials = (Material[])sharedMaterials.Clone(); sharedMaterials[0] = copy; ownedMaterials.Add(copy); return copy;
        }
        set => sharedMaterial = value;
    }
    public float startWidth { get => widthMultiplier * widthCurve.Evaluate(0); set => SetWidth(value, false); }
    public float endWidth { get => widthMultiplier * widthCurve.Evaluate(1); set => SetWidth(value, true); }
    public Color startColor { get => colorGradient.Evaluate(0); set => SetColor(value, false); }
    public Color endColor { get => colorGradient.Evaluate(1); set => SetColor(value, true); }

    private void OnEnable() { Active.Add(this); clock.Reset(); }
    private void OnDisable() => Active.Remove(this);
    private void LateUpdate()
    {
        float dt = clock.Step(false);
        if (!float.IsFinite(time) || time < 0 || !float.IsFinite(minVertexDistance) || minVertexDistance < 0)
            throw new InvalidOperationException("TrailRenderer 的 time 和 minVertexDistance 必须为有限非负数。");
        elapsed += dt;
        if (time == 0) { Clear(); return; }
        if (emitting && (points.Count == 0 || Vector3.Distance(points[^1].position, transform.position) >= Math.Max(.00001f, minVertexDistance))) AddPosition(transform.position);
        float cutoff = elapsed - time;
        while (points.Count > 1 && points[1].birth <= cutoff) points.RemoveAt(0);
        if (points.Count == 1 && points[0].birth < cutoff) points.Clear();
        if (autodestruct && hadPoints && points.Count == 0) Destroy(gameObject);
    }

    //保留末端尚未达到采样距离的活动点，避免轨迹头部落后于 Transform。
    private void VisiblePoints()
    {
        visible.Clear();
        if (points.Count == 0) return;
        Vector3 head = transform.position;
        if (emitting && (head - points[^1].position).sqrMagnitude > 1e-12f)
            visible.Add(new TrailPoint { position = head, birth = elapsed, distance = emittedDistance + Vector3.Distance(points[^1].position, head) });
        for (int i = points.Count - 1; i >= 0; i--)
        {
            TrailPoint value = points[i];
            if (i == 0 && points.Count > 1 && value.birth < elapsed - time)
            {
                TrailPoint next = points[1]; float fraction = Mathf.InverseLerp(value.birth, next.birth, elapsed - time);
                value.position = Vector3.Lerp(value.position, next.position, fraction); value.distance = Mathf.Lerp(value.distance, next.distance, fraction);
            }
            if (visible.Count == 0 || (visible[^1].position - value.position).sqrMagnitude > 1e-12f) visible.Add(value);
        }
    }

    public void Clear() { points.Clear(); visible.Clear(); emittedDistance = 0; hadPoints = false; }
    public void AddPosition(Vector3 value)
    {
        if (points.Count > 0) emittedDistance += Vector3.Distance(points[^1].position, value);
        points.Add(new TrailPoint { position = value, birth = elapsed, distance = emittedDistance }); hadPoints = true;
    }
    public void AddPositions(Vector3[] values) { ArgumentNullException.ThrowIfNull(values); foreach (Vector3 value in values) AddPosition(value); }
    public void SetPositions(Vector3[] values) { ArgumentNullException.ThrowIfNull(values); Clear(); AddPositions(values); }
    public void SetPosition(int index, Vector3 value) { TrailPoint point = points[index]; point.position = value; points[index] = point; }
    public Vector3 GetPosition(int index) => points[index].position;
    public int GetPositions(Vector3[] values)
    {
        ArgumentNullException.ThrowIfNull(values); int number = Math.Min(values.Length, points.Count);
        for (int i = 0; i < number; i++) values[i] = points[i].position; return number;
    }

    //宽度端点赋值保留曲线中间关键点，并按当前乘数换算。
    private void SetWidth(float value, bool end)
    {
        if (widthMultiplier == 0) widthMultiplier = 1;
        var keys = new List<Keyframe>(widthCurve.keys); float at = end ? 1 : 0;
        keys.RemoveAll(key => key.time == at); keys.Add(new Keyframe(at, value / widthMultiplier)); widthCurve.keys = keys.ToArray();
    }
    private void SetColor(Color color, bool end)
    {
        float at = end ? 1 : 0;
        var colors = new List<GradientColorKey>(colorGradient.colorKeys); var alphas = new List<GradientAlphaKey>(colorGradient.alphaKeys);
        colors.RemoveAll(key => key.time == at); alphas.RemoveAll(key => key.time == at);
        colors.Add(new GradientColorKey(color, at)); alphas.Add(new GradientAlphaKey(color.a, at));
        colors.Sort((a, b) => a.time.CompareTo(b.time)); alphas.Sort((a, b) => a.time.CompareTo(b.time)); colorGradient.SetKeys(colors.ToArray(), alphas.ToArray());
    }
    private void OnDestroy()
    {
        Active.Remove(this); if (batch != null) Destroy(batch);
        foreach (Material value in ownedMaterials) Destroy(value);
    }
}
