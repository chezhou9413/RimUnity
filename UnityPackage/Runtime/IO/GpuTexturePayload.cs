using System;
using System.IO;
using System.IO.Compression;

namespace RimUnity.IO
{
    //Shared by authoring tools and Core. Compression changes storage only, never GPU blocks.
    public static class GpuTexturePayload
    {
        public sealed class Data
        {
            public int width, height, format, mipCount;
            public bool opaque;
            public byte[][] levels;
        }

        public static bool IsFile(string path) => path.EndsWith(".rutex", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".rutex.gz", StringComparison.OrdinalIgnoreCase);

        //At least 5% and 256 bytes saved; tiny/incompressible files stay on the old path.
        public static byte[] CompressIfUseful(byte[] bytes)
        {
            if (bytes.Length < 4096) return null;
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.Optimal, true)) gzip.Write(bytes, 0, bytes.Length);
            return output.Length <= bytes.Length - Math.Max(256, bytes.Length / 20) ? output.ToArray() : null;
        }

        public static Data Read(string path)
        {
            using var input = File.OpenRead(path);
            if (path.EndsWith(".rutex.gz", StringComparison.OrdinalIgnoreCase))
            {
                using var gzip = new GZipStream(input, CompressionMode.Decompress);
                return Read(gzip);
            }
            return Read(input);
        }

        //Read directly into final mip arrays. No whole-file decompressed staging buffer.
        public static Data Read(Stream input)
        {
            using var reader = new BinaryReader(input, System.Text.Encoding.UTF8, true);
            if (reader.ReadUInt32() != 0x58545552 || reader.ReadInt32() != 1)
                throw new InvalidDataException("RimUnity 纹理载荷头无效。");
            var data = new Data { format = reader.ReadInt32(), width = reader.ReadInt32(), height = reader.ReadInt32(),
                mipCount = reader.ReadInt32(), opaque = reader.ReadBoolean() };
            int maxDimension = Math.Max(data.width, data.height), maxMips = 0;
            for (int size = maxDimension; size > 0; size >>= 1) maxMips++;
            if (data.width <= 0 || data.height <= 0 || data.mipCount <= 0 || data.mipCount > maxMips)
                throw new InvalidDataException("RimUnity 纹理尺寸或 mip 数量无效。");
            //TextureFormat and LoadedTextureFormat share the Unity numeric values.
            int block;
            switch (data.format)
            {
                case 10: case 26: block = 8; break; //DXT1, BC4
                case 12: case 27: case 25: block = 16; break; //DXT5, BC5, BC7
                default: throw new NotSupportedException("纹理载荷不支持格式：" + data.format);
            }
            data.levels = new byte[data.mipCount][];
            for (int i = 0; i < data.mipCount; i++)
            {
                int width = Math.Max(1, data.width >> i), height = Math.Max(1, data.height >> i);
                int length = checked((int)(((width + 3L) / 4) * ((height + 3L) / 4) * block));
                if (reader.ReadInt32() != length || (input.CanSeek && input.Length - input.Position < length))
                    throw new InvalidDataException("RimUnity 压缩纹理 mip 长度不匹配。");
                var level = new byte[length];
                int offset = 0;
                while (offset < length)
                {
                    int read = input.Read(level, offset, length - offset);
                    if (read == 0) throw new EndOfStreamException("RimUnity 纹理载荷不完整。");
                    offset += read;
                }
                data.levels[i] = level;
            }
            if (input.ReadByte() != -1) throw new InvalidDataException("纹理载荷存在多余数据。");
            return data;
        }
    }
}
