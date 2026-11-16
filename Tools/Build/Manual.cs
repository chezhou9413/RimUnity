using System.Text.Json;

namespace RimUnity.Build;

//按现有离线手册索引同步原文，避免把未登记的本地笔记打进手册。
internal static class Manual
{
    //新增文档需先登记 sources.json；同步已有文档不需要 Git 或 PowerShell。
    internal static void Sync(bool check = false)
    {
        string manual = Path.Combine(Program.Root, "UnityPackage/Documentation~");
        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(manual, "sources.json")));
        int count = 0;
        foreach (var entry in index.RootElement.EnumerateArray())
        {
            string source = Path.Combine(Program.Root, entry.GetProperty("source").GetString()!);
            string destination = Path.Combine(manual, entry.GetProperty("file").GetString()!);
            if (check)
            {
                if (!File.Exists(destination) || Embedded.Hash(source) != Embedded.Hash(destination))
                    throw new InvalidOperationException("Documentation mirror is stale: " + source + ". Run build.cmd docs / sh build.sh docs.");
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, true);
            }
            count++;
        }
        Console.WriteLine($"Documentation {(check ? "checked" : "synchronized")}: {count} files.");
    }
}
