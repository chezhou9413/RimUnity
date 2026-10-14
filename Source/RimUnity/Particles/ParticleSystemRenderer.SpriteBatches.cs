using System;
using System.Collections.Generic;

namespace RimUnity;

//跨纹理 Sprite 按排序后的连续段拆批，每段独立网格，保持透明绘制顺序。
public sealed partial class ParticleSystemRenderer
{
    private readonly List<Mesh> spriteBatches = new();
    private readonly Dictionary<Texture, Material> spriteMaterials = new();
    private Material spriteSource;
    private readonly HashSet<Texture> preparedSpriteTextures = new();

    private void DrawParticleBatches(ParticleSystem system, Camera camera)
    {
        if (!system.textureSheetAnimation.enabled || system.textureSheetAnimation.mode != ParticleSystemAnimationMode.Sprites)
        {
            foreach (int i in order) { streamParticleIndex = i; AppendParticle(system, system.particles[i], camera); }
            Submit(ref batch, sharedMaterial, camera); return;
        }
        if (spriteSource != sharedMaterial) { ReleaseSpriteBatches(); spriteSource = sharedMaterial; }
        preparedSpriteTextures.Clear();
        Material current = null; int group = 0;
        foreach (int i in order)
        {
            var particle = system.particles[i];
            UV(system, particle);
            Sprite[] frames = system.textureSheetAnimation.sprites;
            Texture texture = frames[Math.Min(frames.Length - 1, Mathf.FloorToInt(streamFrame))].texture;
            Material material = SpriteMaterial(texture);
            if (current != null && material != current) { SubmitSpriteBatch(group++, current, camera); ClearMesh(); }
            current = material;
            streamParticleIndex = i; AppendParticle(system, particle, camera);
        }
        if (current != null) SubmitSpriteBatch(group, current, camera);
    }

    //同帧每张贴图只复制一次动态材质参数，资源依赖依旧共享。
    private Material SpriteMaterial(Texture texture)
    {
        if (!spriteMaterials.TryGetValue(texture, out Material material))
        {
            material = new Material(sharedMaterial);
            spriteMaterials.Add(texture, material);
        }
        if (preparedSpriteTextures.Add(texture)) { material.CopyPropertiesFromMaterial(sharedMaterial); material.mainTexture = texture; }
        return material;
    }
    private void SubmitSpriteBatch(int index, Material material, Camera camera)
    {
        while (spriteBatches.Count <= index) spriteBatches.Add(null);
        Mesh target = spriteBatches[index]; Submit(ref target, material, camera); spriteBatches[index] = target;
    }
    private void ReleaseSpriteBatches()
    {
        foreach (Mesh item in spriteBatches) if (item != null) Destroy(item);
        spriteBatches.Clear();
        foreach (Material item in spriteMaterials.Values) Destroy(item);
        spriteMaterials.Clear();
    }
}
