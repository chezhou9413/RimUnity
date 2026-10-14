using System;

namespace RimUnity;

//高度场保留归一化采样和孔洞；数组按 z * 分辨率 + x 展开。
public sealed class TerrainData
{
    public int heightmapResolution;
    public Vector3 size;
    public float[] heights = Array.Empty<float>();
    public bool[] holes = Array.Empty<bool>();
    internal int revision;
    public int holesResolution => heightmapResolution - 1;

    //GetHeights 的二维返回值保持 Unity 的 [z,x] 索引方向。
    public float[,] GetHeights(int xBase, int yBase, int width, int height)
    {
        Validate(); CheckRegion(xBase, yBase, width, height, heightmapResolution);
        var result = new float[height, width];
        for (int z = 0; z < height; z++) for (int x = 0; x < width; x++) result[z, x] = heights[(yBase + z) * heightmapResolution + xBase + x];
        return result;
    }
    public bool[,] GetHoles(int xBase, int yBase, int width, int height)
    {
        Validate(); CheckRegion(xBase, yBase, width, height, holesResolution);
        var result = new bool[height, width];
        for (int z = 0; z < height; z++) for (int x = 0; x < width; x++) result[z, x] = Surface(xBase + x, yBase + z);
        return result;
    }
    public float GetHeight(int x, int y)
    {
        Validate(); CheckRegion(x, y, 1, 1, heightmapResolution); return heights[y * heightmapResolution + x] * size.y;
    }

    //通过 API 改写采样会使所有引用它的碰撞器重建高度场索引。
    public void SetHeights(int xBase, int yBase, float[,] values)
    {
        ArgumentNullException.ThrowIfNull(values); Validate();
        int width = values.GetLength(1), height = values.GetLength(0); CheckRegion(xBase, yBase, width, height, heightmapResolution);
        foreach (float value in values) if (!float.IsFinite(value) || value < 0 || value > 1) throw new ArgumentException("高度值必须位于零到一之间。", nameof(values));
        for (int z = 0; z < height; z++) for (int x = 0; x < width; x++) heights[(yBase + z) * heightmapResolution + xBase + x] = values[z, x];
        revision++;
    }
    public void SetHoles(int xBase, int yBase, bool[,] values)
    {
        ArgumentNullException.ThrowIfNull(values); Validate();
        int width = values.GetLength(1), height = values.GetLength(0); CheckRegion(xBase, yBase, width, height, holesResolution);
        if (holes.Length == 0) { holes = new bool[checked(holesResolution * holesResolution)]; Array.Fill(holes, true); }
        for (int z = 0; z < height; z++) for (int x = 0; x < width; x++) holes[(yBase + z) * holesResolution + xBase + x] = values[z, x];
        revision++;
    }

    //缺省 holes 表示没有孔洞，不表示缺失高度数据。
    internal bool Surface(int x, int z) => holes.Length == 0 || holes[z * holesResolution + x];
    internal Vector3 Point(int x, int z) => new(x * size.x / holesResolution, heights[z * heightmapResolution + x] * size.y, z * size.z / holesResolution);
    internal void Validate()
    {
        int n = heightmapResolution;
        if (n < 2 || heights == null || heights.Length != checked(n * n)) throw new InvalidOperationException("TerrainData 高度数组长度必须为 heightmapResolution 的平方。");
        if (holes == null || holes.Length != 0 && holes.Length != checked((n - 1) * (n - 1))) throw new InvalidOperationException("TerrainData 孔洞数组长度必须为 (heightmapResolution-1) 的平方。");
        if (size.x <= 0 || size.y <= 0 || size.z <= 0 || !float.IsFinite(size.x + size.y + size.z)) throw new InvalidOperationException("TerrainData.size 必须为有限正尺寸。");
    }
    private static void CheckRegion(int x, int z, int width, int height, int resolution)
    {
        if (x < 0 || z < 0 || width < 0 || height < 0 || x > resolution - width || z > resolution - height) throw new ArgumentOutOfRangeException(nameof(x), "高度场区域超出分辨率。");
    }
}
