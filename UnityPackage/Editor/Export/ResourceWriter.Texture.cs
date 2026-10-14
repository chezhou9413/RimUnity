using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RimUnity.Editor
{
    internal sealed partial class ResourceWriter
    {
        //只压缩源资源已经使用块压缩的贴图，数值纹理和未压缩图片保持原有路径。
        private static bool IsGpuCompressed(TextureFormat format) => format == TextureFormat.DXT1
            || format == TextureFormat.DXT5 || format == TextureFormat.BC4
            || format == TextureFormat.BC5 || format == TextureFormat.BC7;

        //在编辑器完成方向转换和压缩，游戏直接上传完整 mip 链。
        private static byte[] GpuTextureBytes(Texture2D source, bool linear)
        {
            var staging = new Texture2D(source.width, source.height, TextureFormat.RGBA32, source.mipmapCount > 1, linear);
            try
            {
                if (!staging.LoadImage(TextureBytes(source, linear), false))
                    throw new InvalidDataException("无法读取导入后的纹理：" + AssetDatabase.GetAssetPath(source));
                Color32[] pixels = staging.GetPixels32();
                bool opaque = true;
                foreach (var pixel in pixels) if (pixel.a != 255) { opaque = false; break; }
                //Unity 像素数组从左下开始，Waylith 设备数据从左上开始。
                for (int y = 0; y < source.height / 2; y++)
                    for (int x = 0; x < source.width; x++)
                    {
                        int a = y * source.width + x, b = (source.height - 1 - y) * source.width + x;
                        Color32 saved = pixels[a]; pixels[a] = pixels[b]; pixels[b] = saved;
                    }
                staging.SetPixels32(pixels);
                staging.Apply(source.mipmapCount > 1, false);
                //方向转换后由 Unity 高质量编码器生成目标块，不在运行时压缩。
                EditorUtility.CompressTexture(staging, source.format, TextureCompressionQuality.Best);
                if (staging.format != source.format || staging.mipmapCount != source.mipmapCount)
                    throw new InvalidDataException("纹理压缩格式或 mip 数量不匹配：" + AssetDatabase.GetAssetPath(source));
                byte[] payload = staging.GetRawTextureData();
                using var output = new MemoryStream();
                using var writer = new BinaryWriter(output);
                writer.Write(0x58545552u); writer.Write(1); writer.Write((int)source.format);
                writer.Write(source.width); writer.Write(source.height); writer.Write(source.mipmapCount); writer.Write(opaque);
                int offset = 0, block = source.format == TextureFormat.DXT1 || source.format == TextureFormat.BC4 ? 8 : 16;
                for (int mip = 0; mip < source.mipmapCount; mip++)
                {
                    int width = Math.Max(1, source.width >> mip), height = Math.Max(1, source.height >> mip);
                    int count = checked(((width + 3) / 4) * ((height + 3) / 4) * block);
                    writer.Write(count); writer.Write(payload, offset, count); offset += count;
                }
                if (offset != payload.Length) throw new InvalidDataException("压缩纹理载荷长度异常：" + source.name);
                return output.ToArray();
            }
            finally { UnityEngine.Object.DestroyImmediate(staging); }
        }
    }
}
