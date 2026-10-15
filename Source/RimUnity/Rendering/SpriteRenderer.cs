
namespace RimUnity;

//绘制精灵三角网格，保留锚点、顶点色和翻转。
public sealed class SpriteRenderer : MonoBehaviour
{
    public Sprite sprite;
    public Color color = Color.white;
    public bool flipX, flipY;
    public Material sharedMaterial;
    public int sortingOrder;

    //动画写入 material.* 时使用这份副本，避免改动资源目录里的共享材质。
    public Material material
    {
        get
        {
            EnsureMaterialInstance();
            return materialInstance;
        }
    }
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
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector2 p = UI.SpriteGeometry.LocalPosition(sprite, geometry.Positions[i]);
                vertices[i] = new Vector3(p.x * (flipX ? -1 : 1), p.y * (flipY ? -1 : 1), 0);
                colors[i] = color;
                normals[i] = Vector3.back;
            }
            if (mesh == null) mesh = new Mesh();
            mesh.Clear(); mesh.vertices = vertices; mesh.uv = geometry.UV; mesh.colors = colors; mesh.normals = normals;
            mesh.triangles = geometry.Triangles; mesh.RecalculateBounds(); filter.sharedMesh = mesh;
            previousSprite = sprite; previousColor = color; previousFlipX = flipX; previousFlipY = flipY;
        }
        bool materialChanged = materialInstance == null || previousMaterial != sharedMaterial;
        EnsureMaterialInstance();
        if ((spriteChanged || materialChanged) && materialInstance != null) materialInstance.mainTexture = sprite.texture;
    }

    //父级动画可能在本组件 OnEnable 之前绑定，因此实例材质要能提前创建。
    private void EnsureMaterialInstance()
    {
        if (sharedMaterial == null) return;
        if (materialInstance == null || previousMaterial != sharedMaterial)
        {
            if (materialInstance != null) Destroy(materialInstance);
            materialInstance = new Material(sharedMaterial);
            if (sprite != null) materialInstance.mainTexture = sprite.texture;
            previousMaterial = sharedMaterial;
        }
        if (renderer != null && renderer.sharedMaterial != materialInstance) renderer.sharedMaterial = materialInstance;
    }

    //共享资源由资源目录持有，这里只释放实例专用网格与材质。
    private void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
        if (materialInstance != null) Destroy(materialInstance);
    }
}
