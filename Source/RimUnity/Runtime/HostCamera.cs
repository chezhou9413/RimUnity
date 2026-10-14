namespace RimUnity;

//游戏仍以 Unity 形状暴露 CameraDriver；只在这个边界读取数据，不借用其对象系统。
internal static class HostCamera
{
    private static Camera snapshot;

    //复制真实地图投影，避免重新计算的相机与宿主画面产生偏差。
    internal static void Synchronize()
    {
        var source = Verse.Current.CameraDriver?.MyCamera;
        if (source == null) { Camera.main = null; return; }
        if (snapshot == null) snapshot = new GameObject("RimUnity 地图相机").AddComponent<Camera>();
        var position = source.transform.position;
        var rotation = source.transform.rotation;
        snapshot.transform.position = new(position.x, position.y, position.z);
        snapshot.transform.rotation = new(rotation.x, rotation.y, rotation.z, rotation.w);
        snapshot.orthographic = source.orthographic;
        snapshot.orthographicSize = source.orthographicSize;
        snapshot.fieldOfView = source.fieldOfView;
        snapshot.nearClipPlane = source.nearClipPlane;
        snapshot.farClipPlane = source.farClipPlane;
        snapshot.aspect = source.aspect;
        snapshot.cullingMask = source.cullingMask;
        var pixel = source.pixelRect;
        snapshot.pixelRect = new(pixel.x, pixel.y, pixel.width, pixel.height);
        snapshot.worldToCameraMatrix = Copy(source.worldToCameraMatrix);
        snapshot.projectionMatrix = Copy(source.projectionMatrix);
        Camera.main = snapshot;
    }

    //逐行列读取而非 reinterpret，宿主矩阵与制作端的物理布局不同。
    private static Matrix4x4 Copy(UnityEngine.Matrix4x4 source)
    {
        Matrix4x4 value = default;
        for (int column = 0; column < 4; column++)
            for (int row = 0; row < 4; row++) value[row, column] = source[row, column];
        return value;
    }
}
