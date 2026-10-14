
namespace RimUnity;

//绘制精灵三角网格，保留锚点、顶点色和翻转。
public sealed class SpriteRenderer : MonoBehaviour
{
    public Sprite sprite;
    public Color color = Color.white;
    public bool flipX, flipY;
    public Material sharedMaterial;
    public int sortingOrder;
    private Sprite previousSprite;
    private Color previousColor;
    private bool previousFlipX, previousFlipY;
    private Material previousMaterial, materialInstance;
    private Mesh mesh;
    private MeshRenderer renderer;
    private MeshFilter filter;

    //只有实际实例的生命周期才创建渲染对象，资源模板不参与显示。
    private void Awake()
    {
        filter = gameObject.AddComponent<MeshFilter>();
        renderer = gameObject.AddComponent<MeshRenderer>();
        renderer.enabled = false;
    }

    private void OnEnable() => Refresh();
    private void OnDisable() { if (renderer != null) renderer.enabled = false; }
    private void LateUpdate() => Refresh();

    //几何或外观变化时重建，静止的精灵不产生逐帧数组分配。
    private void Refresh()
    {
        if (renderer == null) return;
        renderer.enabled = sprite != null && sharedMaterial != null && isActiveAndEnabled;
        renderer.sortingOrder = sortingOrder;
        if (!renderer.enabled) return;
        bool spriteChanged = previousSprite != sprite;
        if (spriteChanged || previousColor != color || previousFlipX != flipX || previousFlipY != flipY)
        {
            var geometry = UI.SpriteGeometry.Get(sprite);
            var vertices = new Vector3[geometry.Positions.Length];
            var colors = new Color[vertices.Length];
            var normals = new Vector3[vertices.Length];
            //Sprite.pivot 使用与导出数据一致的归一化坐标。
            Vector2 pivot = sprite.pivot;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector2 p = geometry.Positions[i] - pivot;
                vertices[i] = new Vector3(p.x * sprite.rect.width / sprite.pixelsPerUnit * (flipX ? -1 : 1),
                    p.y * sprite.rect.height / sprite.pixelsPerUnit * (flipY ? -1 : 1), 0);
                colors[i] = color;
                normals[i] = Vector3.back;
            }
            if (mesh == null) mesh = new Mesh();
            mesh.Clear(); mesh.vertices = vertices; mesh.uv = geometry.UV; mesh.colors = colors; mesh.normals = normals;
            mesh.triangles = geometry.Triangles; mesh.RecalculateBounds(); filter.sharedMesh = mesh;
            previousSprite = sprite; previousColor = color; previousFlipX = flipX; previousFlipY = flipY;
        }
        if (previousMaterial != sharedMaterial)
        {
            if (materialInstance != null) Destroy(materialInstance);
            materialInstance = new Material(sharedMaterial);
            renderer.sharedMaterial = materialInstance;
            previousMaterial = sharedMaterial;
            spriteChanged = true;
        }
        if (spriteChanged) materialInstance.mainTexture = sprite.texture;
    }

    //共享资源由资源目录持有，这里只释放实例专用网格与材质。
    private void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
        if (materialInstance != null) Destroy(materialInstance);
    }
}
