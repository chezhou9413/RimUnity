using System;
using System.IO;
using System.Linq;
using RimUnity;

namespace Spine.Unity;

//共享图集数据不参加帧更新，首次创建骨骼时才解析。
public abstract class AtlasAssetBase : ScriptableObject
{
    public abstract Atlas GetAtlas(bool onlyMetaData = false);
}

//保留 Spine 页名与材质对应关系，纹理仍由 RimUnity 资源目录持有。
public sealed class SpineAtlasAsset : AtlasAssetBase
{
    public TextAsset atlasFile;
    public Texture[] textures;
    private Atlas atlas;
    public override Atlas GetAtlas(bool onlyMetaData = false)
    {
        if (atlas != null) return atlas;
        atlas = new Atlas(new StringReader(atlasFile.text), "", new Loader(textures));
        atlas.FlipV();
        return atlas;
    }
    private sealed class Loader : TextureLoader
    {
        private readonly Texture[] textures;
        internal Loader(Texture[] textures) => this.textures = textures;
        public void Load(AtlasPage page, string path)
        {
            string key = Path.GetFileNameWithoutExtension(path);
            Texture texture = textures.SingleOrDefault(t => t.name == key)
                ?? throw new InvalidDataException("Spine 图集页缺少唯一材质：" + path);
            page.rendererObject = texture;
            page.width = texture.width; page.height = texture.height;
        }
        public void Unload(object texture) { }
    }
}
