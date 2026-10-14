using System;
using System.IO;
using System.Reflection;
using Waylith.Rendering;

namespace RimUnity;

//文件纹理保留恢复来源，首次采样才上传 GPU，闲置后交给宿主管理驻留。
public sealed partial class Texture2D
{
    //只加载已存在的资源，不把损坏的文件替换成占位图片。
    public static Texture2D LoadFile(string path, bool linear = false, bool readable = false)
    {
        Runtime.NativeRuntime.AssertThread();
        string fullPath = Path.GetFullPath(path);
        var data = ReadFile(fullPath, out bool cached);
        return new Texture2D(new FileTexture(fullPath, linear, readable, data, cached));
    }

    private delegate bool ReadCached(string path, out LoadedTextureData data);
    private static readonly ReadCached ReadCache = typeof(GpuTextureService)
        .GetMethod("TryLoadPersistedTexture", BindingFlags.Static | BindingFlags.NonPublic)
        .CreateDelegate<ReadCached>();
    //已存在的缓存只绑定来源，不能每次列表重载都再次写入宿主缓存队列。
    private static readonly Action<Waylith.Rendering.Texture2D, string> SetSourcePath = typeof(Waylith.Rendering.Texture2D)
        .GetProperty("SourcePath").GetSetMethod(true).CreateDelegate<Action<Waylith.Rendering.Texture2D, string>>();

    //像素被脚本改写后不能再从原始文件恢复旧内容。
    private void PrepareFileWrite(bool replace = false)
    {
        if (CoreTexture is FileTexture file) file.DetachSource(replace);
    }

    //优先读宿主像素缓存；PNG 路径保留透明 RGB，不能再经过透明像素扩散。
    private static LoadedTextureData ReadFile(string path, out bool cached)
    {
        cached = ReadCache(path, out var data);
        if (cached) return data;
        byte[] bytes = File.ReadAllBytes(path);
        if (Path.GetExtension(path).Equals(".rutex", StringComparison.OrdinalIgnoreCase))
            return ReadGpuPayload(bytes);
        byte[] rgba = PngDecoder.TryDecodeRgba32(bytes, out int width, out int height);
        if (rgba != null)
            return new LoadedTextureData(width, height, 1, LoadedTextureFormat.Rgba32, new[] { rgba }, TexturePixelData.IsOpaqueRgba32(rgba));
        if (GpuTextureService.TryLoadTextureFromMemory(bytes, out var decoded)) return decoded;
        throw new InvalidDataException("无法解码纹理：" + path);
    }

    //标准载荷保存 GPU 格式及完整 mip 链，不在游戏加载阶段重新压缩。
    private static LoadedTextureData ReadGpuPayload(byte[] bytes)
    {
        using var reader = new BinaryReader(new MemoryStream(bytes, false));
        if (reader.ReadUInt32() != 0x58545552 || reader.ReadInt32() != 1)
            throw new InvalidDataException("RimUnity 纹理载荷头无效。");
        var format = (LoadedTextureFormat)reader.ReadInt32();
        int width = reader.ReadInt32(), height = reader.ReadInt32(), count = reader.ReadInt32();
        bool opaque = reader.ReadBoolean();
        if (width <= 0 || height <= 0 || count <= 0 || count > TexturePixelData.CalculateMipCount(width, height))
            throw new InvalidDataException("RimUnity 纹理尺寸或 mip 数量无效。");
        int block = format switch
        {
            LoadedTextureFormat.Dxt1 or LoadedTextureFormat.Bc4 => 8,
            LoadedTextureFormat.Dxt5 or LoadedTextureFormat.Bc5 or LoadedTextureFormat.Bc7 => 16,
            _ => throw new NotSupportedException("纹理载荷不支持格式：" + format)
        };
        var levels = new byte[count][];
        for (int i = 0; i < count; i++)
        {
            int length = checked(((Math.Max(1, width >> i) + 3) / 4) * ((Math.Max(1, height >> i) + 3) / 4) * block);
            if (reader.ReadInt32() != length || reader.BaseStream.Length - reader.BaseStream.Position < length)
                throw new InvalidDataException("RimUnity 压缩纹理 mip 长度不匹配。");
            levels[i] = reader.ReadBytes(length);
        }
        if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("纹理载荷存在多余数据。");
        return new LoadedTextureData(width, height, count, format, levels, opaque);
    }

    //缺失磁盘缓存时从标准源文件精确恢复，避免宿主通用 PNG 路径改变预乘透明度。
    private sealed class FileTexture : Waylith.Rendering.Texture2D
    {
        private readonly string path;
        private readonly bool linear, keepReadable;
        private bool disposed, modified;

        internal FileTexture(string path, bool linear, bool readable, LoadedTextureData data, bool cached)
            : base(data.Width, data.Height, data.Format, linear, data.MipCount, true)
        {
            this.path = path; this.linear = linear; keepReadable = readable;
            Name = path; Opaque = data.Opaque;
            ReplaceCpuData(data.Width, data.Height, data.Format, data.Levels, data.MipCount, linear, false);
            CpuData.Swizzle = data.Swizzle;
            if (cached) { SetSourcePath(this, path); TrackerName = path; }
            else PersistToTextureCache(path);
            DropPixels();
        }

        public override bool CanRehydrate => !disposed && (!modified || CpuData.HasData);

        //改写资源先保存现有像素，再解除磁盘恢复关系，调用 Apply 时由调用者决定是否保留 CPU 副本。
        internal void DetachSource(bool replace)
        {
            if (modified) return;
            if (!replace && !CpuData.HasData)
            {
                var data = ReadFile(path, out _);
                ReplaceCpuData(data.Width, data.Height, data.Format, data.Levels, data.MipCount, linear, false);
                CpuData.Swizzle = data.Swizzle;
            }
            modified = true; SetSourcePath(this, null);
        }

        //宿主完成纹理引用解析后再上传，所有场景和 UI 都使用这一入口。
        public override bool EnsureResident()
        {
            if (disposed) throw new ObjectDisposedException(path);
            if (modified) return base.EnsureResident();
            if (GpuResource != null) return true;
            if (!CpuData.HasData)
            {
                var data = ReadFile(path, out _);
                ReplaceCpuData(data.Width, data.Height, data.Format, data.Levels, data.MipCount, linear, false);
                CpuData.Swizzle = data.Swizzle;
            }
            bool ready = base.EnsureResident();
            if (ready) DropPixels();
            return ready;
        }

        //不可读资源不保留 CPU 像素副本，GPU 回收后可按路径恢复。
        private void DropPixels()
        {
            if (keepReadable || modified) return;
            CpuData.ReleaseTracked(); CpuData.Release();
        }

        //销毁后的资源不能被旧材质引用重新上传。
        public override void Dispose() { disposed = true; base.Dispose(); }
    }
}
