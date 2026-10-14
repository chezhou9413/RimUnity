using System;

namespace RimUnity;

//形状在自身空间采样，最后统一应用模块位置、旋转和缩放。
public sealed partial class ParticleSystem
{
    private bool shapeRejected;
    //形状共用圆弧限制，方向随机量独立从整个球面采样。
    private void Shape(float emissionTime, int batchIndex, int batchCount, out Vector3 position, out Vector3 direction, out Color color)
    {
        position = Vector3.zero; direction = Vector3.forward; color = Color.white;
        shapeRejected = false;
        if (!shape.enabled) return;
        float azimuth = ShapePhase(shape.arcMode, shape.arcSpeed, shape.arcSpread, emissionTime, batchIndex, batchCount, shape.arc, 360) * shape.arc * Mathf.Deg2Rad;
        Vector3 radial = new(Mathf.Cos(azimuth), Mathf.Sin(azimuth), 0);
        switch (shape.shapeType)
        {
            case ParticleSystemShapeType.Mesh:
            case ParticleSystemShapeType.Sprite:
            case ParticleSystemShapeType.MeshRenderer:
            case ParticleSystemShapeType.SkinnedMeshRenderer:
            case ParticleSystemShapeType.SpriteRenderer:
                SampleGeometry(emissionTime, batchIndex, batchCount, out position, out direction, out color);
                break;
            case ParticleSystemShapeType.Sphere:
            case ParticleSystemShapeType.SphereShell:
            case ParticleSystemShapeType.Hemisphere:
            case ParticleSystemShapeType.HemisphereShell:
                float z = Random01() * 2 - 1;
                direction = radial * Mathf.Sqrt(1 - z * z) + Vector3.forward * z;
                if (shape.shapeType == ParticleSystemShapeType.Hemisphere || shape.shapeType == ParticleSystemShapeType.HemisphereShell) direction.z = Mathf.Abs(direction.z);
                float volume = shape.shapeType == ParticleSystemShapeType.SphereShell || shape.shapeType == ParticleSystemShapeType.HemisphereShell ? 1 : Mathf.Pow(Mathf.Lerp(Mathf.Pow(1 - shape.radiusThickness, 3), 1, Random01()), 1f / 3);
                position = direction * (shape.radius * volume);
                break;
            case ParticleSystemShapeType.Box:
            case ParticleSystemShapeType.BoxShell:
            case ParticleSystemShapeType.BoxEdge:
                position = BoxPosition();
                break;
            case ParticleSystemShapeType.Rectangle:
                position = new Vector3(Random01() - .5f, Random01() - .5f, 0);
                break;
            case ParticleSystemShapeType.SingleSidedEdge:
                float radiusPhase = ShapePhase(shape.radiusMode, shape.radiusSpeed, shape.radiusSpread, emissionTime, batchIndex, batchCount, 1, 1, false);
                position = new Vector3((radiusPhase * 2 - 1) * shape.radius, 0, 0);
                direction = Vector3.up;
                break;
            case ParticleSystemShapeType.Circle:
            case ParticleSystemShapeType.CircleEdge:
                position = radial * (shape.radius * (shape.shapeType == ParticleSystemShapeType.CircleEdge ? 1 : RadialSample())); direction = radial;
                break;
            case ParticleSystemShapeType.Donut:
                float tubeAngle = Random01() * Mathf.PI * 2;
                float tubeRadius = shape.donutRadius * RadialSample();
                direction = radial * Mathf.Cos(tubeAngle) + Vector3.forward * Mathf.Sin(tubeAngle);
                position = radial * shape.radius + direction * tubeRadius;
                break;
            case ParticleSystemShapeType.Cone:
            case ParticleSystemShapeType.ConeShell:
            case ParticleSystemShapeType.ConeVolume:
            case ParticleSystemShapeType.ConeVolumeShell:
                float radius = shape.shapeType == ParticleSystemShapeType.ConeShell || shape.shapeType == ParticleSystemShapeType.ConeVolumeShell ? 1 : RadialSample(), angle = shape.angle * Mathf.Deg2Rad;
                direction = (radial * (Mathf.Sin(angle) * radius) + Vector3.forward * Mathf.Cos(angle)).normalized;
                position = radial * (radius * shape.radius);
                if (shape.shapeType == ParticleSystemShapeType.ConeVolume || shape.shapeType == ParticleSystemShapeType.ConeVolumeShell)
                    position += direction * (Random01() * shape.length / Math.Max(.001f, direction.z));
                break;
            default: throw new NotSupportedException("粒子形状尚未支持：" + shape.shapeType);
        }
        if (shape.texture != null)
        {
            Vector2 uv = shape.shapeType == ParticleSystemShapeType.Mesh || shape.shapeType == ParticleSystemShapeType.Sprite || shape.shapeType == ParticleSystemShapeType.MeshRenderer || shape.shapeType == ParticleSystemShapeType.SkinnedMeshRenderer || shape.shapeType == ParticleSystemShapeType.SpriteRenderer ? shapeUv
                : shape.shapeType == ParticleSystemShapeType.Box || shape.shapeType == ParticleSystemShapeType.Rectangle
                    ? new Vector2(position.x + .5f, position.y + .5f)
                    : new Vector2(position.x, position.y) / Mathf.Max(.0001f, shape.radius * 2) + new Vector2(.5f, .5f);
            Color pixel = shape.textureBilinearFiltering ? shape.texture.GetPixelBilinear(uv.x, uv.y)
                : shape.texture.GetPixel(Mathf.FloorToInt(uv.x * shape.texture.width), Mathf.FloorToInt(uv.y * shape.texture.height));
            float channel = shape.textureClipChannel switch { 0 => pixel.r, 1 => pixel.g, 2 => pixel.b, 3 => pixel.a, _ => throw new NotSupportedException("未知形状纹理裁切通道。") };
            shapeRejected = channel < shape.textureClipThreshold;
            if (shape.textureColorAffectsParticles) { color.r *= pixel.r; color.g *= pixel.g; color.b *= pixel.b; }
            if (shape.textureAlphaAffectsParticles) color.a *= pixel.a;
        }
        Quaternion rotation = Quaternion.Euler(shape.rotation);
        position = shape.position + rotation * Vector3.Scale(position, shape.scale);
        direction = rotation * direction;
        if (shape.randomDirectionAmount != 0) direction = Vector3.Lerp(direction, RandomSphere(), shape.randomDirectionAmount).normalized;
        Vector3 fromCenter = position - shape.position;
        if (shape.sphericalDirectionAmount != 0 && fromCenter.sqrMagnitude > 0)
            direction = Vector3.Lerp(direction, fromCenter.normalized, shape.sphericalDirectionAmount).normalized;
        if (shape.randomPositionAmount != 0) position += RandomSphere() * (Mathf.Pow(Random01(), 1f / 3) * shape.randomPositionAmount);
    }

    //盒体坐标按面或边收缩到指定厚度；零厚度保留真正的表面与线段。
    private Vector3 BoxPosition()
    {
        Vector3 position = new(Random01() - .5f, Random01() - .5f, Random01() - .5f);
        if (shape.shapeType == ParticleSystemShapeType.Box) return position;
        Vector3 extent = new(Mathf.Abs(shape.scale.x), Mathf.Abs(shape.scale.y), Mathf.Abs(shape.scale.z));
        bool edge = shape.shapeType == ParticleSystemShapeType.BoxEdge;
        Vector3 weights = edge ? extent : new(extent.y * extent.z, extent.x * extent.z, extent.x * extent.y);
        float total = weights.x + weights.y + weights.z;
        float sample = Random01() * total;
        int axis = total == 0 ? 0 : sample < weights.x ? 0 : sample < weights.x + weights.y ? 1 : 2;
        for (int i = 0; i < 3; i++)
            if (edge ? i != axis : i == axis)
            {
                float offset = .5f * (1 - Random01() * Mathf.Clamp01(shape.boxThickness[i]));
                position[i] = Random01() < .5f ? -offset : offset;
            }
        return position;
    }

    //环形截面按面积采样；半径厚度零表示只在边缘发射。
    private float RadialSample() => Mathf.Sqrt(Mathf.Lerp(Mathf.Pow(1 - shape.radiusThickness, 2), 1, Random01()));

    //均匀球面方向不复用形状圆弧或半球限制。
    private Vector3 RandomSphere()
    {
        float z = Random01() * 2 - 1, angle = Random01() * Mathf.PI * 2;
        float radius = Mathf.Sqrt(1 - z * z);
        return new Vector3(radius * Mathf.Cos(angle), radius * Mathf.Sin(angle), z);
    }
}
