using System;

namespace RimUnity;

//精灵来自同一纹理，模块保留 Unity 的增删查询习惯。
public sealed partial class ParticleSystem
{
    public sealed partial class TextureSheetAnimationModule
    {
        public Sprite[] sprites = Array.Empty<Sprite>();
        public int spriteCount => sprites.Length;
        public Sprite GetSprite(int index) => sprites[index];
        public void SetSprite(int index, Sprite sprite) => sprites[index] = sprite;
        public void AddSprite(Sprite sprite) { Array.Resize(ref sprites, sprites.Length + 1); sprites[^1] = sprite; }
        public void RemoveSprite(int index)
        {
            if ((uint)index >= sprites.Length) throw new ArgumentOutOfRangeException(nameof(index));
            Array.Copy(sprites, index + 1, sprites, index, sprites.Length - index - 1);
            Array.Resize(ref sprites, sprites.Length - 1);
        }
    }
}
