using System;
using System.Collections.Generic;

namespace RimUnity.U2D;

//图集按原始精灵名称查找，共享纹理由资源目录统一管理。
public sealed class SpriteAtlas : Object
{
    private readonly Dictionary<string, Sprite> sprites = new(StringComparer.Ordinal);
    internal SpriteAtlas(Sprite[] entries)
    {
        foreach (Sprite sprite in entries) sprites.Add(sprite.name, sprite);
    }
    public int spriteCount => sprites.Count;
    public Sprite GetSprite(string name) => sprites.GetValueOrDefault(name);
    public bool CanBindTo(Sprite sprite) => sprite && sprites.ContainsKey(sprite.name);
    //将精灵写入调用者提供的缓冲区。
    public int GetSprites(Sprite[] results)
    {
        ArgumentNullException.ThrowIfNull(results);
        int index = 0;
        foreach (Sprite sprite in sprites.Values) { if (index == results.Length) break; results[index++] = sprite; }
        return index;
    }
}
