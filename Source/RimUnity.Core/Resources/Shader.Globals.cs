using Waylith.Rendering;

namespace RimUnity;

//全局参数直接登记 Waylith，材质绑定阶段读取同一份状态。
public sealed partial class Shader
{
    public static void SetGlobalFloat(string name, float value) => SetGlobalFloat(PropertyToID(name), value);
    public static void SetGlobalFloat(int id, float value) => GlobalShaderState.SetFloat(id, value);
    public static void SetGlobalInt(string name, int value) => SetGlobalFloat(name, value);
    public static void SetGlobalInt(int id, int value) => SetGlobalFloat(id, value);
    public static void SetGlobalVector(string name, Vector4 value) => SetGlobalVector(PropertyToID(name), value);
    public static void SetGlobalVector(int id, Vector4 value)
    {
        //Unity 的纹理变换为缩放在前，Waylith 材质布局使用偏移在前。
        if (ShaderPropertyId.TryGetName(id, out string name) && name.EndsWith("_ST", System.StringComparison.Ordinal)) value = new(value.z, value.w, value.x, value.y);
        GlobalShaderState.SetVector(id, Vector4.ToNumerics(value));
    }
    public static void SetGlobalColor(string name, Color value) => SetGlobalVector(name, value);
    public static void SetGlobalColor(int id, Color value) => SetGlobalVector(id, value);
    public static void SetGlobalMatrix(string name, Matrix4x4 value) => SetGlobalMatrix(PropertyToID(name), value);
    public static void SetGlobalMatrix(int id, Matrix4x4 value) => GlobalShaderState.SetMatrix(id, Matrix4x4.ToNumerics(value));
    public static void SetGlobalTexture(string name, Texture value) => SetGlobalTexture(PropertyToID(name), value);
    public static void SetGlobalTexture(int id, Texture value) => GlobalShaderState.SetTexture(id, value?.NativeTexture);
    public static float GetGlobalFloat(int id) => GlobalShaderState.TryGetFloat(id, out float value) ? value : 0;
    public static float GetGlobalFloat(string name) => GetGlobalFloat(PropertyToID(name));
    public static Vector4 GetGlobalVector(int id)
    {
        if (!GlobalShaderState.TryGetVector(id, out var v)) return Vector4.zero;
        return ShaderPropertyId.TryGetName(id, out string name) && name.EndsWith("_ST", System.StringComparison.Ordinal)
            ? new(v.Z, v.W, v.X, v.Y) : new(v.X, v.Y, v.Z, v.W);
    }
    public static Vector4 GetGlobalVector(string name) => GetGlobalVector(PropertyToID(name));
    public static Matrix4x4 GetGlobalMatrix(int id) => GlobalShaderState.TryGetMatrix(id, out var value) ? Matrix4x4.FromNumerics(value) : Matrix4x4.zero;
    public static Matrix4x4 GetGlobalMatrix(string name) => GetGlobalMatrix(PropertyToID(name));
    public static void EnableKeyword(string keyword) => GlobalShaderState.EnableKeyword(keyword);
    public static void DisableKeyword(string keyword) => GlobalShaderState.DisableKeyword(keyword);
}
