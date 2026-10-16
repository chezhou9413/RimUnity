using System;
using System.Collections.Generic;

namespace RimUnity;

//跨纹理 Sprite 按排序后的连续段拆批，每段独立网格，保持透明绘制顺序。
public sealed partial class ParticleSystemRenderer
{
    private readonly List<(Mesh mesh, int frame)> spriteBatches = new();
    private readonly Dictionary<Texture, SpriteMaterialCache> spriteMaterials = new();
    private readonly List<Texture> expiredSpriteTextures = new();
    private int lastSpriteTrim;
    private Material spriteSource;
    private readonly HashSet<Texture> preparedSpriteTextures = new();

    //短期空闲保留以应对连续爆发，长期不用的批次和材质才回收。
    private sealed class SpriteMaterialCache
    {
        internal Material material;
        internal int frame;
    }

    //连续纹理段独立提交，不重排透明粒子来压低绘制调用。
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
        if (!spriteMaterials.TryGetValue(texture, out var cached))
        {
            cached = new SpriteMaterialCache { material = new Material(sharedMaterial) };
            spriteMaterials.Add(texture, cached);
        }
        cached.frame = Time.frameCount;
        Material material = cached.material;
        if (preparedSpriteTextures.Add(texture)) { material.CopyPropertiesFromMaterial(sharedMaterial); material.mainTexture = texture; }
        return material;
    }
    //网格列表按峰值扩张，时间戳用于回收后续未再使用的尾部。
    private void SubmitSpriteBatch(int index, Material material, Camera camera)
    {
        while (spriteBatches.Count <= index) spriteBatches.Add(default);
        Mesh target = spriteBatches[index].mesh;
        Submit(ref target, material, camera); spriteBatches[index] = (target, Time.frameCount);
    }

    //每隔 120 帧清理一次；同帧其他相机已用过的资源保持有效。
    private void TrimSpriteBatches()
    {
        int frame = Time.frameCount;
        if (unchecked(frame - lastSpriteTrim) < 120) return;
        lastSpriteTrim = frame;
        int retained = spriteBatches.Count;
        while (retained > 0 && unchecked(frame - spriteBatches[retained - 1].frame) >= 120)
        {
            Mesh mesh = spriteBatches[--retained].mesh;
            if (mesh != null) Destroy(mesh);
        }
        if (retained < spriteBatches.Count) spriteBatches.RemoveRange(retained, spriteBatches.Count - retained);
        foreach (var pair in spriteMaterials)
            if (unchecked(frame - pair.Value.frame) >= 120) expiredSpriteTextures.Add(pair.Key);
        foreach (Texture texture in expiredSpriteTextures)
        {
            ReleaseSpriteMaterial(spriteMaterials[texture].material);
            spriteMaterials.Remove(texture);
        }
        expiredSpriteTextures.Clear();
        if (spriteBatches.Count == 0 && spriteMaterials.Count == 0) spriteSource = null;
    }

    //Sprite 材质的 UI 变体也由当前渲染器持有，必须同步释放。
    private void ReleaseSpriteMaterial(Material material)
    {
        if (uiMaterials.Remove(material, out var ui)) Destroy(ui);
        Destroy(material);
    }

    //源材质切换或组件销毁时释放全部派生资源。
    private void ReleaseSpriteBatches()
    {
        foreach (var item in spriteBatches) if (item.mesh != null) Destroy(item.mesh);
        spriteBatches.Clear();
        foreach (var item in spriteMaterials.Values) ReleaseSpriteMaterial(item.material);
        spriteMaterials.Clear(); preparedSpriteTextures.Clear(); expiredSpriteTextures.Clear(); spriteSource = null;
    }
}
