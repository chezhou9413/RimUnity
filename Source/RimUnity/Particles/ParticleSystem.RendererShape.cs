using System;

namespace RimUnity;

public sealed partial class ParticleSystem
{
    private Mesh bakedShape;
    private int bakedFrame = -1;
    private Sprite shapeSprite;
    private readonly ShapeModule rendererShape = new();

    //Renderer 形状从真实实例读取；骨骼网格每帧只烘焙一次，不为每个粒子重复蒙皮。
    private ShapeModule ResolveGeometry(out Transform rendererTransform)
    {
        rendererTransform = null;
        if (shape.shapeType == ParticleSystemShapeType.MeshRenderer)
        {
            if (shape.meshRenderer == null) throw new InvalidOperationException("MeshRenderer 发射形状没有目标。");
            rendererTransform = shape.meshRenderer.transform;
            rendererShape.mesh = shape.meshRenderer.GetComponent<MeshFilter>()?.sharedMesh;
        }
        else if (shape.shapeType == ParticleSystemShapeType.SkinnedMeshRenderer)
        {
            if (shape.skinnedMeshRenderer == null) throw new InvalidOperationException("蒙皮发射形状没有目标。");
            rendererTransform = shape.skinnedMeshRenderer.transform;
            if (bakedShape == null) bakedShape = new Mesh();
            if (bakedFrame != Time.frameCount) { shape.skinnedMeshRenderer.BakeMesh(bakedShape); bakedFrame = Time.frameCount; geometry = null; }
            rendererShape.mesh = bakedShape;
        }
        else if (shape.shapeType == ParticleSystemShapeType.SpriteRenderer)
        {
            if (shape.spriteRenderer == null || shape.spriteRenderer.sprite == null) throw new InvalidOperationException("SpriteRenderer 发射形状没有精灵。");
            rendererTransform = shape.spriteRenderer.transform;
            Sprite sprite = shape.spriteRenderer.sprite;
            if (shapeSprite != sprite)
            {
                var source = UI.SpriteGeometry.Get(sprite);
                rendererShape.spriteVertices = new Vector3[source.Positions.Length];
                for (int i = 0; i < source.Positions.Length; i++)
                {
                    Vector2 point = source.Positions[i] - sprite.pivot;
                    rendererShape.spriteVertices[i] = new Vector3(point.x * sprite.rect.width / sprite.pixelsPerUnit, point.y * sprite.rect.height / sprite.pixelsPerUnit, 0);
                }
                rendererShape.spriteTriangles = source.Triangles; shapeSprite = sprite;
            }
            rendererShape.sprite = sprite;
        }
        else return shape;
        rendererShape.shapeType = shape.shapeType == ParticleSystemShapeType.SpriteRenderer ? ParticleSystemShapeType.Sprite : ParticleSystemShapeType.Mesh;
        rendererShape.meshShapeType = shape.meshShapeType; rendererShape.useMeshMaterialIndex = shape.useMeshMaterialIndex;
        rendererShape.meshMaterialIndex = shape.meshMaterialIndex; rendererShape.scale = shape.scale;
        rendererShape.textureUVChannel = shape.textureUVChannel;
        return rendererShape;
    }

    //实例销毁后释放仅为动态形状生成的临时网格。
    private void ReleaseShapeMesh() { if (bakedShape != null) Destroy(bakedShape); bakedShape = null; geometry = null; }
}
