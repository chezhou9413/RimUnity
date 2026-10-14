using System;
using System.Collections.Generic;

namespace RimUnity.UI;

//保存 TMP 实际 SDF 图集和字形度量，普通 Font 只用于交付源字体。
public sealed class TMP_FontAsset : ScriptableObject
{
    public Font sourceFontFile;
    public Texture2D[] atlasTextures = Array.Empty<Texture2D>();
    public TMP_Glyph[] glyphs = Array.Empty<TMP_Glyph>();
    public TMP_GlyphPair[] glyphPairs = Array.Empty<TMP_GlyphPair>();
    public TMP_FontAsset[] fallbackFontAssetTable = Array.Empty<TMP_FontAsset>();
    public float pointSize, scale = 1, ascentLine, descentLine, lineHeight, atlasPadding;
    private Dictionary<uint, TMP_Glyph> lookup;
    private Dictionary<ulong, TMP_GlyphPair> pairs;
    //配对使用字形索引，多个 Unicode 指向同一字形时仍共享字距信息。
    internal TMP_GlyphPair Pair(uint first, uint second)
    {
        if (pairs == null) { pairs = new(); foreach (var pair in glyphPairs) pairs[((ulong)pair.first << 32) | pair.second] = pair; }
        pairs.TryGetValue(((ulong)first << 32) | second, out var value); return value;
    }
    internal bool TryGlyph(uint code, out TMP_Glyph glyph, out TMP_FontAsset owner)
    {
        if (lookup == null) { lookup = new(); foreach (var g in glyphs) lookup[g.unicode] = g; }
        if (lookup.TryGetValue(code, out glyph)) { owner = this; return true; }
        foreach (var fallback in fallbackFontAssetTable) if (fallback.TryGlyph(code, out glyph, out owner)) return true;
        owner = null; return false;
    }
}

//图集像素区域和水平排版指标保留 TMP 的原始单位。
public sealed class TMP_Glyph
{
    public uint unicode, index;
    public int atlasIndex;
    public float width, height, bearingX, bearingY, advance, scale = 1;
    public Rect rect;
}

//两侧字形各自的 x/y 偏移与 x/y advance 保留字体特性表中的值。
public sealed class TMP_GlyphPair
{
    public uint first, second;
    public Vector4 firstAdjustment, secondAdjustment;
}
