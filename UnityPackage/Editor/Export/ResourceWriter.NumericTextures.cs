using System;
using System.IO;
using UnityEngine;

namespace RimUnity.Editor
{
    internal sealed partial class ResourceWriter
    {
        private static bool IsFloatTexture(TextureFormat format) => format == TextureFormat.RHalf || format == TextureFormat.RGHalf
            || format == TextureFormat.RGBAHalf || format == TextureFormat.RFloat || format == TextureFormat.RGFloat || format == TextureFormat.RGBAFloat;
        // Preserve signed and HDR numeric payloads. Float32 also losslessly represents half-float values.
        private static byte[] FloatTextureBytes(Texture2D texture)
        {
            if (texture.isReadable) return EncodeFloatPixels(texture.GetPixels(0), texture.width, texture.height);
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                throw new InvalidOperationException("Float texture readback requires a graphics device: " + texture.name);
            var previous = RenderTexture.active; bool previousSrgb = GL.sRGBWrite;
            var temporary = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var readable = new Texture2D(texture.width, texture.height, TextureFormat.RGBAFloat, false, true);
            try
            {
                GL.sRGBWrite = false; Graphics.Blit(texture, temporary); RenderTexture.active = temporary;
                readable.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); readable.Apply(false, false);
                return EncodeFloatPixels(readable.GetPixels(0), texture.width, texture.height);
            }
            finally { RenderTexture.active = previous; GL.sRGBWrite = previousSrgb; RenderTexture.ReleaseTemporary(temporary); UnityEngine.Object.DestroyImmediate(readable); }
        }
        private static byte[] EncodeFloatPixels(Color[] pixels, int width, int height)
        {
            using (var stream = new MemoryStream(checked(pixels.Length * 16)))
            using (var writer = new BinaryWriter(stream))
            {
                // Unity pixel rows begin at the bottom; native image storage begins at the top.
                if (pixels.Length != checked(width * height)) throw new InvalidDataException("Float texture pixel count does not match dimensions.");
                for (int y = height - 1; y >= 0; y--)
                    for (int x = 0; x < width; x++)
                    {
                        Color pixel = pixels[y * width + x];
                        writer.Write(pixel.r); writer.Write(pixel.g); writer.Write(pixel.b); writer.Write(pixel.a);
                    }
                return stream.ToArray();
            }
        }
    }
}
