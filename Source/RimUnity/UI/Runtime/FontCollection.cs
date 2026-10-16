using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace RimUnity.UI.Runtime;

//将 TTC 的首个字型整理成独立字体，供宿主的 SFNT 度量读取器使用。
internal static class FontCollection
{
    private const uint CollectionTag = 0x74746366, HeadTag = 0x68656164, SignatureTag = 0x44534947;

    //首个字型与宿主 ImGui 默认的 FontNo=0 保持一致，不改变字形内容。
    internal static byte[] FirstFace(byte[] source)
    {
        if (source.Length < 4 || Read32(source, 0) != CollectionTag) return source;
        RequireRange(source, 0, 16);
        uint version = Read32(source, 4);
        if (version != 0x00010000 && version != 0x00020000)
            throw new InvalidDataException($"不支持的 TTC 版本：{version:X8}");
        uint faces = Read32(source, 8);
        if (faces == 0 || faces > (source.Length - 12) / 4)
            throw new InvalidDataException("TTC 字型目录不完整。");
        int face = checked((int)Read32(source, 12));
        RequireRange(source, face, 12);
        uint format = Read32(source, face);
        if (format != 0x00010000 && format != 0x4F54544F && format != 0x74727565)
            throw new InvalidDataException($"TTC 字型格式无效：{format:X8}");
        int count = BinaryPrimitives.ReadUInt16BigEndian(source.AsSpan(face + 4, 2));
        if (count == 0 || count > 4095) throw new InvalidDataException("TTC 字体表数量无效。");
        RequireRange(source, face + 12, count * 16);
        var tables = new List<(uint Tag, int Offset, int Length)>();
        for (int i = 0; i < count; i++)
        {
            int record = face + 12 + i * 16;
            uint tag = Read32(source, record);
            int offset = checked((int)Read32(source, record + 8));
            int length = checked((int)Read32(source, record + 12));
            RequireRange(source, offset, length);
            //抽取后原数字签名失效，不能将集合签名当作独立字体签名。
            if (tag != SignatureTag) tables.Add((tag, offset, length));
        }
        count = tables.Count;
        int total = 12 + count * 16;
        foreach (var table in tables) total = checked(total + Align4(table.Length));
        byte[] result = new byte[total];
        Write32(result, 0, format);
        Write16(result, 4, count);
        int power = 1, selector = 0;
        while (power * 2 <= count) { power *= 2; selector++; }
        Write16(result, 6, power * 16);
        Write16(result, 8, selector);
        Write16(result, 10, count * 16 - power * 16);
        int cursor = 12 + count * 16, head = -1;
        for (int i = 0; i < count; i++)
        {
            var table = tables[i];
            source.AsSpan(table.Offset, table.Length).CopyTo(result.AsSpan(cursor));
            if (table.Tag == HeadTag)
            {
                if (table.Length < 12) throw new InvalidDataException("TTC 的 head 表不完整。");
                head = cursor;
                Write32(result, head + 8, 0);
            }
            int record = 12 + i * 16;
            Write32(result, record, table.Tag);
            Write32(result, record + 4, Checksum(result.AsSpan(cursor, Align4(table.Length))));
            Write32(result, record + 8, (uint)cursor);
            Write32(result, record + 12, (uint)table.Length);
            cursor += Align4(table.Length);
        }
        if (head < 0) throw new InvalidDataException("TTC 字型缺少 head 表。");
        Write32(result, head + 8, unchecked(0xB1B0AFBAu - Checksum(result)));
        return result;
    }

    //字体表使用相对整个文件的偏移，读取前核对外部数据边界。
    private static void RequireRange(byte[] data, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
            throw new InvalidDataException($"TTC 字体表越界：offset={offset}, length={length}");
    }

    //SFNT 表及校验和按四字节对齐。
    private static int Align4(int length) => checked(length + 3) & ~3;

    //所有 SFNT 整数以大端保存。
    private static uint Read32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
    private static void Write32(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset, 4), value);
    private static void Write16(byte[] data, int offset, int value) => BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(offset, 2), checked((ushort)value));

    //head 的表校验和按零调整值计算，文件校验和再写入实际调整值。
    private static uint Checksum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        for (int i = 0; i < data.Length; i += 4) sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(data.Slice(i, 4)));
        return sum;
    }
}
