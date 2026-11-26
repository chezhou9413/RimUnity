using System;
using System.IO;
using System.Reflection;
using Waylith.Assets;
using Waylith.Graphics.Textures;
using Waylith.Rendering;

namespace RimUnity;

//文件纹理保留恢复来源，首次采样才上传 GPU，闲置后交给宿主管理驻留。
public sealed partial class Texture2D
{
    //只加载已存在的资源，不把损坏的文件替换成占位图片；保留三参重载以兼容已编译模组。
    public static Texture2D LoadFile(string path, bool linear = false, bool readable = false)
        => LoadFile(path, linear, readable, false);

    public static Texture2D LoadFile(string path, bool linear, bool readable, bool mipChain)
    {
        Runtime.NativeRuntime.AssertThread();
        string fullPath = Path.GetFullPath(path);
        var data = ReadFile(fullPath, mipChain, out bool cached, out bool useKeyedCache, out string mipCacheKey);
        return new Texture2D(new FileTexture(fullPath, linear, readable, mipChain, data, cached, useKeyedCache, mipCacheKey));
    }

    //支持从 catalog.zip 等非散文件来源按需读取与恢复贴图，复用宿主 keyed 纹理缓存。
    public static Texture2D LoadSource(string sourceName, string entryName, string cacheKey, Func<Stream> openStream, bool linear = false, bool readable = false, bool mipChain = false)
    {
        Runtime.NativeRuntime.AssertThread();
        ArgumentNullException.ThrowIfNull(openStream);
        bool isPayload = IO.GpuTexturePayload.IsFile(entryName);
        string effectiveKey = !string.IsNullOrWhiteSpace(cacheKey) && mipChain ? cacheKey + ":mip" : cacheKey;
        var data = _ReadSource(sourceName, entryName, effectiveKey, openStream, isPayload, mipChain, out bool cached);
        return new Texture2D(new FileTexture(sourceName, entryName, effectiveKey, openStream, isPayload, linear, readable, mipChain, data, cached));
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

    //仅需要生成 Mip 链的普通图片写入独立 keyed 缓存，避免污染旧三参 LoadFile 共用的宿主路径缓存。
    private static string BuildMipCacheKey(string fullPath)
    {
        FileInfo info = new(fullPath);
        if (!info.Exists) return null;
        return $"rimunity:mip:v1:{fullPath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
    }

    //优先读宿主像素缓存或已挂载 AssetPack；PNG 路径保留透明 RGB，不能再经过透明像素扩散。
    private static LoadedTextureData ReadFile(string path, bool mipChain, out bool cached, out bool useKeyedCache, out string mipCacheKey)
    {
        bool isPayload = IO.GpuTexturePayload.IsFile(path);
        //1.7.5014 rev1195 的 ContentPack.TryGetCurrentTextureEntry 区分有效的已挂载 AssetPack 条目与磁盘散文件；包内已烘焙贴图直接走原读包路径，保留其原有 Mip 链。
        bool isPackTexture = !isPayload && ContentPack.TryGetCurrentTextureEntry(path, premultiplied: false, out _, out _);
        useKeyedCache = mipChain && !isPayload && !isPackTexture;
        mipCacheKey = null;
        if (useKeyedCache)
        {
            mipCacheKey = BuildMipCacheKey(path);
            if (!string.IsNullOrEmpty(mipCacheKey) && GpuTextureService.TryLoadKeyedTextureCache(mipCacheKey, out var keyedData)
                && (keyedData.MipCount > 1 || Math.Max(keyedData.Width, keyedData.Height) <= 1))
            {
                cached = true;
                //1.7.5014 rev1195 的 TextureCacheStore.StoreKeyed 将 Meta.Flags 固定写为 0，读取 Rgba32 时补算不透明标记。
                if (keyedData.Format == LoadedTextureFormat.Rgba32 && keyedData.Levels is { Length: > 0 } && !keyedData.Opaque)
                    keyedData = keyedData with { Opaque = TexturePixelData.IsOpaqueRgba32(keyedData.Levels[0]) };
                return keyedData;
            }
            cached = false;
        }
        else
        {
            cached = ReadCache(path, out var data);
            if (cached)
            {
                //仅对散文件的历史路径缓存做单级迁移：旧版本若把散装单图写成了多级 Mip，无 Mip 请求不再复用旧多级缓存，而是重解码并覆写为单级；AssetPack 与 .rutex 保留原有多级。
                if (!isPayload && !isPackTexture && !mipChain && data.MipCount > 1)
                {
                    cached = false;
                }
                else
                {
                    return data;
                }
            }
        }
        if (isPayload)
        {
            var payload = IO.GpuTexturePayload.Read(path);
            return new LoadedTextureData(payload.width, payload.height, payload.mipCount,
                (LoadedTextureFormat)payload.format, payload.levels, payload.opaque);
        }
        return _DecodeImageBytes(File.ReadAllBytes(path), path, mipChain);
    }

    //归档纹理恢复：块压缩载荷直接流式读取保留 opaque，普通图片走宿主 keyed 磁盘缓存。
    private static LoadedTextureData _ReadSource(string sourceName, string entryName, string cacheKey, Func<Stream> openStream, bool isPayload, bool mipChain, out bool cached)
    {
        cached = false;
        if (isPayload)
        {
            using var stream = openStream();
            var payload = IO.GpuTexturePayload.Read(stream, entryName.EndsWith(".rutex.gz", StringComparison.OrdinalIgnoreCase));
            return new LoadedTextureData(payload.width, payload.height, payload.mipCount,
                (LoadedTextureFormat)payload.format, payload.levels, payload.opaque);
        }
        if (!string.IsNullOrWhiteSpace(cacheKey) && GpuTextureService.TryLoadKeyedTextureCache(cacheKey, out var data))
        {
            bool needsMipUpgrade = mipChain && data.Format == LoadedTextureFormat.Rgba32
                && data.MipCount <= 1 && Math.Max(data.Width, data.Height) > 1;
            if (!needsMipUpgrade)
            {
                cached = true;
                //1.7.5014 rev1195 的 TextureCacheStore.StoreKeyed 将 Meta.Flags 固定写为 0，读取 Rgba32 时补算不透明标记。
                if (data.Format == LoadedTextureFormat.Rgba32 && data.Levels is { Length: > 0 } && !data.Opaque)
                    data = data with { Opaque = TexturePixelData.IsOpaqueRgba32(data.Levels[0]) };
                return data;
            }
        }
        using var input = openStream();
        using var buffer = new MemoryStream();
        input.CopyTo(buffer);
        return _DecodeImageBytes(buffer.ToArray(), sourceName, mipChain);
    }

    private static LoadedTextureData _DecodeImageBytes(byte[] bytes, string label, bool mipChain)
    {
        byte[] rgba = PngDecoder.TryDecodeRgba32(bytes, out int width, out int height);
        if (rgba != null)
        {
            int mipCount = mipChain ? TexturePixelData.CalculateMipCount(width, height) : 1;
            byte[][] levels = mipCount > 1
                ? TexturePixelData.GenerateRgba32MipChain(rgba, width, height, mipCount, premultiplied: false)
                : new[] { rgba };
            return new LoadedTextureData(width, height, mipCount, LoadedTextureFormat.Rgba32, levels, TexturePixelData.IsOpaqueRgba32(rgba));
        }
        if (GpuTextureService.TryLoadTextureFromMemory(bytes, out var decoded))
        {
            if (mipChain && decoded.Format == LoadedTextureFormat.Rgba32 && decoded.MipCount <= 1
                && Math.Max(decoded.Width, decoded.Height) > 1 && decoded.Levels is { Length: > 0 })
            {
                int mipCount = TexturePixelData.CalculateMipCount(decoded.Width, decoded.Height);
                byte[][] levels = TexturePixelData.GenerateRgba32MipChain(decoded.Levels[0], decoded.Width, decoded.Height, mipCount, premultiplied: false);
                return new LoadedTextureData(decoded.Width, decoded.Height, mipCount, decoded.Format, levels, decoded.Opaque, decoded.SourceStamp, decoded.Swizzle);
            }
            return decoded;
        }
        throw new InvalidDataException("无法解码纹理：" + label);
    }

    //缺失磁盘缓存时从标准源文件精确恢复，避免宿主通用 PNG 路径改变预乘透明度。
    private sealed class FileTexture : Waylith.Rendering.Texture2D
    {
        private readonly string path;
        private readonly string _entryName;
        private readonly string _cacheKey;
        private readonly Func<Stream> _openStream;
        private readonly bool _isPayload;
        private readonly bool linear, keepReadable;
        private readonly bool _mipChain;
        private bool disposed, modified;

        internal FileTexture(string path, bool linear, bool readable, bool mipChain, LoadedTextureData data, bool cached, bool useKeyedCache, string mipCacheKey)
            : base(data.Width, data.Height, data.Format, linear, data.MipCount, true)
        {
            this.path = path; this.linear = linear; keepReadable = readable; _mipChain = mipChain;
            Name = path; Opaque = data.Opaque;
            ReplaceCpuData(data.Width, data.Height, data.Format, data.Levels, data.MipCount, linear, false);
            CpuData.Swizzle = data.Swizzle;
            if (useKeyedCache)
            {
                SetSourcePath(this, path);
                TrackerName = path;
                //1.7.5014 rev1195 的 TexturePixelData.Release 仅置空 Levels 引用不修改字节数组，异步写 keyed 缓存后可直接释放 CPU 追踪引用；即使无 keyed key 也不回写旧路径缓存。
                if (!cached && !string.IsNullOrEmpty(mipCacheKey)) _ = GpuTextureService.BeginWriteKeyedTextureCache(mipCacheKey, data);
            }
            else if (cached) { SetSourcePath(this, path); TrackerName = path; }
            else PersistToTextureCache(path);
            DropPixels();
        }

        internal FileTexture(string sourceName, string entryName, string cacheKey, Func<Stream> openStream, bool isPayload, bool linear, bool readable, bool mipChain, LoadedTextureData data, bool cached)
            : base(data.Width, data.Height, data.Format, linear, data.MipCount, true)
        {
            path = sourceName;
            _entryName = entryName;
            _cacheKey = cacheKey;
            _openStream = openStream;
            _isPayload = isPayload;
            this.linear = linear; keepReadable = readable; _mipChain = mipChain;
            Name = sourceName; TrackerName = sourceName; Opaque = data.Opaque;
            ReplaceCpuData(data.Width, data.Height, data.Format, data.Levels, data.MipCount, linear, false);
            CpuData.Swizzle = data.Swizzle;
            //1.7.5014 rev1195 的 TexturePixelData.Release 只替换数组引用不覆盖字节内容，异步写缓存后可安全释放 CPU 追踪引用。
            if (!cached && !isPayload && !string.IsNullOrWhiteSpace(cacheKey))
                _ = GpuTextureService.BeginWriteKeyedTextureCache(cacheKey, data);
            DropPixels();
        }

        public override bool CanRehydrate => !disposed && (!modified || CpuData.HasData);

        private LoadedTextureData _ReloadData()
            => _openStream != null
                ? _ReadSource(path, _entryName, _cacheKey, _openStream, _isPayload, _mipChain, out _)
                : ReadFile(path, _mipChain, out _, out _, out _);

        //改写资源先保存现有像素，再解除磁盘恢复关系，调用 Apply 时由调用者决定是否保留 CPU 副本。
        internal void DetachSource(bool replace)
        {
            if (modified) return;
            if (!replace && !CpuData.HasData)
            {
                var data = _ReloadData();
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
                var data = _ReloadData();
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
