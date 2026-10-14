using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace RimUnity.Editor
{
    //从图集源条目导出精灵，避免运行时打包旋转改变 UV 与边框。
    internal static class SpriteAtlasWriter
    {
        internal static Sprite[] Read(SpriteAtlas atlas, string path)
        {
            if (atlas.isVariant) throw new NotSupportedException(path + "：变体图集的缩放纹理尚未接通。");
            var sprites = new HashSet<Sprite>();
            foreach (UnityEngine.Object item in atlas.GetPackables())
            {
                if (item is Sprite sprite) { sprites.Add(sprite); continue; }
                string source = AssetDatabase.GetAssetPath(item);
                string[] paths = AssetDatabase.IsValidFolder(source)
                    ? AssetDatabase.FindAssets("t:Texture2D", new[] { source }).Select(AssetDatabase.GUIDToAssetPath).ToArray()
                    : new[] { source };
                foreach (string assetPath in paths)
                    foreach (Sprite entry in AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Sprite>()) sprites.Add(entry);
            }
            if (sprites.Count == 0) throw new InvalidOperationException(path + "：图集没有可导出的源精灵。");
            if (sprites.GroupBy(s => s.name).Any(g => g.Count() > 1)) throw new InvalidOperationException(path + "：图集包含同名精灵，不能建立唯一查找。");
            return sprites.OrderBy(s => s.name, StringComparer.Ordinal).ToArray();
        }
    }
}
