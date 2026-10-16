using System;
using System.Collections.Generic;
using System.Linq;
using RimUnity.Serialization;
using TMPro;
using UnityEngine;

namespace RimUnity.Editor
{
    //导出实际字体资源和 SDF 参数，不依赖运行时安装 TMP DLL。
    internal static class TmpWriter
    {
        internal static void Component(TextMeshProUGUI text, ComponentData data, ValueWriter writer, string path)
        {
            if (text.font == null) throw new InvalidOperationException(path + "：TMP 字体未指定。");
            if ((text.fontStyle & ~(FontStyles.Bold | FontStyles.Italic)) != 0 || text.enableVertexGradient || text.isRightToLeftText) throw new NotSupportedException(path + "：TMP 支持普通、粗体与斜体，其他字体样式／顶点渐变／RTL 尚未接通。");
            if (text.overflowMode != TextOverflowModes.Overflow && text.overflowMode != TextOverflowModes.Masking
                && text.overflowMode != TextOverflowModes.Truncate && text.overflowMode != TextOverflowModes.Ellipsis)
                throw new NotSupportedException(path + "：TMP 分页和链接溢出尚未接通。");
            foreach (string name in new[] { "text", "font", "fontSize", "fontSizeMin", "fontSizeMax", "enableAutoSizing", "enableWordWrapping", "richText", "enableKerning", "overflowMode", "alignment", "characterSpacing", "lineSpacing", "margin", "color", "raycastTarget", "faceColor", "outlineColor", "outlineWidth" }) data.members.Add(writer.Property(text, name, path));
            Material material = text.fontSharedMaterial;
            if (material == null || !material.HasProperty("_GradientScale")) throw new NotSupportedException(path + "：仅支持 TMP SDF 字体材质。");
            foreach (string keyword in new[] { "UNDERLAY_ON", "UNDERLAY_INNER", "GLOW_ON", "BEVEL_ON" }) if (material.IsKeywordEnabled(keyword)) throw new NotSupportedException(path + "：TMP 材质特性尚未接通：" + keyword);
            bool bold = (text.fontStyle & FontStyles.Bold) != 0;
            data.members.Add(new MemberData { name = "italicSlant", property = true, value = writer.Write((text.fontStyle & FontStyles.Italic) != 0 ? text.font.italicStyle * .01f : 0f, path) });
            float weight = material.GetFloat(bold ? "_WeightBold" : "_WeightNormal") / 4f;
            float dilation = (weight + material.GetFloat("_FaceDilate")) * material.GetFloat("_ScaleRatioA");
            data.members.Add(new MemberData { name = "faceDilate", property = true, value = writer.Write(dilation, path) });
            data.members.Add(new MemberData { name = "fontSpacing", property = true, value = writer.Write(text.font.normalSpacingOffset + (bold ? text.font.boldSpacing : 0), path) });
        }
        internal static void Font(TMP_FontAsset font, ResourceData data, ValueWriter writer, string path)
        {
            if (!font.atlasRenderMode.ToString().Contains("SD")) throw new NotSupportedException(path + "：字体图集必须使用 SDF 渲染模式。");
            data.type = "TMP_FontAsset";
            void Put(string name, object value) => data.members.Add(new MemberData { name = name, value = writer.Write(value, path + "." + name) });
            Put("sourceFontFile", font.sourceFontFile); Put("atlasTextures", font.atlasTextures); Put("fallbackFontAssetTable", font.fallbackFontAssetTable.ToArray());
            Put("pointSize", font.faceInfo.pointSize); Put("scale", font.faceInfo.scale); Put("ascentLine", font.faceInfo.ascentLine);
            Put("descentLine", font.faceInfo.descentLine); Put("lineHeight", font.faceInfo.lineHeight); Put("atlasPadding", (float)font.atlasPadding);
            var glyphs = new ValueData { kind = "array", items = new List<ValueData>() };
            foreach (var group in font.characterTable.GroupBy(c => c.unicode))
            {
                var character = group.First();
                if (group.Any(c => c.glyphIndex != character.glyphIndex || c.scale != character.scale))
                    throw new InvalidOperationException(path + "：字体 " + font.name + " 的字符 U+" + character.unicode.ToString("X") + " 存在冲突记录。");
                //旧字体重复字符的缓存可能为空，按序列化字形索引读取实际记录。
                if (!font.glyphLookupTable.TryGetValue(character.glyphIndex, out var glyph))
                    throw new InvalidOperationException(path + "：字体 " + font.name + " 的字符 U+" + character.unicode.ToString("X") + " 缺少字形 " + character.glyphIndex);
                var fields = new List<MemberData>();
                void Field(string name, object value) => fields.Add(new MemberData { name = name, value = writer.Write(value, path + ".glyph." + name) });
                Field("unicode", character.unicode); Field("index", glyph.index); Field("atlasIndex", glyph.atlasIndex); Field("scale", glyph.scale * character.scale);
                Field("width", glyph.metrics.width); Field("height", glyph.metrics.height); Field("bearingX", glyph.metrics.horizontalBearingX); Field("bearingY", glyph.metrics.horizontalBearingY); Field("advance", glyph.metrics.horizontalAdvance);
                Field("rect", new Rect(glyph.glyphRect.x, glyph.glyphRect.y, glyph.glyphRect.width, glyph.glyphRect.height)); glyphs.items.Add(new ValueData { kind = "data", fields = fields });
            }
            data.members.Add(new MemberData { name = "glyphs", value = glyphs });
            var pairs = new ValueData { kind = "array", items = new List<ValueData>() };
            foreach (var pair in font.fontFeatureTable.glyphPairAdjustmentRecords)
            {
                var a = pair.firstAdjustmentRecord; var b = pair.secondAdjustmentRecord;
                var av = a.glyphValueRecord; var bv = b.glyphValueRecord;
                var fields = new List<MemberData>();
                void Field(string name, object value) => fields.Add(new MemberData { name = name, value = writer.Write(value, path + ".glyphPair." + name) });
                Field("first", a.glyphIndex); Field("second", b.glyphIndex);
                Field("firstAdjustment", new Vector4(av.xPlacement, av.yPlacement, av.xAdvance, av.yAdvance));
                Field("secondAdjustment", new Vector4(bv.xPlacement, bv.yPlacement, bv.xAdvance, bv.yAdvance));
                pairs.items.Add(new ValueData { kind = "data", fields = fields });
            }
            data.members.Add(new MemberData { name = "glyphPairs", value = pairs });
        }
    }
}
