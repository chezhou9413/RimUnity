using System;
using System.Text;

namespace RimUnity;

//明文和二进制资源共享字节载荷，读取二进制不进行编码往返。
public sealed class TextAsset : Object
{
    private readonly byte[] content;
    public byte[] bytes => (byte[])content.Clone();
    public string text => Encoding.UTF8.GetString(content);
    public TextAsset(string text) => content = Encoding.UTF8.GetBytes(text);
    public TextAsset(byte[] bytes) => content = (byte[])bytes.Clone();
}
