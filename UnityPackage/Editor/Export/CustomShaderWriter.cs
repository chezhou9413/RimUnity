using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace RimUnity.Editor
{
    //调用方将目录内容与其他导出资源一起提交，失败目录不进入成功清单。
    internal sealed class CustomShaderExport
    {
        internal string Id, Directory;
        internal JObject Manifest;
    }

    //只启动离线编译工具，不启动游戏、Unity Player 或场景验证。
    internal static class CustomShaderWriter
    {
        //同一 Shader 在 UI／世界配置或关键字不同的情况下生成独立资源。
        internal static CustomShaderExport Write(Material material, string directory, bool ui, string path, int pass = -1, bool cameraUi = false)
        {
            string tool = EditorPrefs.GetString("RimUnity.ShaderTool", "");
            string gameRoot = EditorPrefs.GetString("RimUnity.GameRoot", "");
            return Write(material, directory, ui, path, tool, gameRoot, pass, cameraUi);
        }

        //显式路径入口便于批量导出；源文件和工具不具备时准确说明缺失项。
        internal static CustomShaderExport Write(Material material, string directory, bool ui, string path, string tool, string gameRoot, int pass = -1, bool cameraUi = false)
        {
            string source = BuiltInSurfaceSource.Path(material);
            bool importedSource = source.EndsWith(".cfxrshader", StringComparison.OrdinalIgnoreCase);
            if ((!source.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) && !importedSource) || !File.Exists(source))
                throw new NotSupportedException(path + "：自定义转换需要可读取的 .shader 源文件：" + source + "（" + material.shader.name + "）");
            if (string.IsNullOrWhiteSpace(tool) || !File.Exists(tool) || !tool.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                throw new FileNotFoundException(path + "：请配置 RimUnity.ShaderTool 为离线工具 exe 路径。", tool);
            if (string.IsNullOrWhiteSpace(gameRoot) || !File.Exists(Path.Combine(gameRoot, "Waylith.dll")))
                throw new DirectoryNotFoundException(path + "：RimUnity.GameRoot 必须指向含 Waylith.dll 的 RimWorld 1.7 目录。");
            string profile = ui ? "ui" : "world";
            //换过 Shader 的材质会保留无效旧关键字，只提交当前程序声明的变体。
            string keywords = string.Join(",", material.shaderKeywords.Where(value => material.shader.keywordSpace.FindKeyword(value).isValid).OrderBy(value => value, StringComparer.Ordinal));
            if (cameraUi) keywords = string.IsNullOrEmpty(keywords) ? "RIMUNITY_UI_MASKS" : keywords + ",RIMUNITY_UI_MASKS";
            string identity = source + "\n" + material.shader.name + "\n" + AssetDatabase.GetAssetDependencyHash(source) + "\n" + profile + "\n" + keywords + "\n" + pass + "\ncolor-v1:" + QualitySettings.activeColorSpace;
            string hash;
            using (SHA256 sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-", "").ToLowerInvariant();
            string id = "RimUnity_Custom_" + hash;
            string output = Path.GetFullPath(Path.Combine(directory, "s_" + CompactHash(hash)));
            string descriptor = Path.Combine(output, "shader.json");
            //只复用当前导出暂存目录的成功记录，跨批次由主导出器管理发布目录。
            if (!File.Exists(descriptor))
            {
                string input = Path.GetFullPath(source);
                if (importedSource)
                {
                    //使用原导入器已选择管线的明文源码，不自行删除其预处理分支。
                    var importer = AssetImporter.GetAtPath(source);
                    string program = importer.GetType().GetField("shaderSourceCode")?.GetValue(importer) as string;
                    if (string.IsNullOrWhiteSpace(program)) throw new InvalidDataException(path + "：CFXR 导入器没有提供生成源码。");
                    program = Regex.Replace(program, "#include\\s+\"([^\"]+)\"", match =>
                    {
                        string include = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(input), match.Groups[1].Value));
                        return File.Exists(include) ? "#include \"" + include.Replace('\\', '/') + "\"" : match.Value;
                    });
                    Directory.CreateDirectory(output);
                    input = Path.Combine(output, "Imported.shader");
                    File.WriteAllText(input, program, new UTF8Encoding(false));
                }
                Run(tool, gameRoot, input, output, profile, keywords, path, pass);
            }
            JObject manifest = JObject.Parse(File.ReadAllText(descriptor, Encoding.UTF8));
            if ((string)manifest["profile"] != profile || !File.Exists(Path.Combine(output, (string)manifest["vertexFile"]))
                || !File.Exists(Path.Combine(output, (string)manifest["fragmentFile"])))
                throw new InvalidDataException(path + "：Shader 转换结果不完整或绘制配置不匹配。");
            return new CustomShaderExport { Id = id, Directory = output, Manifest = manifest };
        }

        internal static string CompactHash(string hash)
        {
            var bytes = new byte[hash.Length / 2];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(hash.Substring(i * 2, 2), 16);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
        //同时排空两个输出管道，编译错误完整反馈给 Unity 导出窗口。
        private static void Run(string tool, string gameRoot, string source, string output, string profile, string keywords, string path, int pass)
        {
            StringBuilder stdout = new StringBuilder(), stderr = new StringBuilder();
            var info = new ProcessStartInfo
            {
                FileName = Path.GetFullPath(tool), UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                Arguments = string.Join(" ", new[] { Path.GetFullPath(gameRoot), "shaderlab", source, output, profile, keywords, pass.ToString(System.Globalization.CultureInfo.InvariantCulture), QualitySettings.activeColorSpace.ToString() }.Select(Quote))
            };
            using (Process process = new Process { StartInfo = info })
            {
                process.OutputDataReceived += (_, args) => { if (args.Data != null) stdout.AppendLine(args.Data); };
                process.ErrorDataReceived += (_, args) => { if (args.Data != null) stderr.AppendLine(args.Data); };
                if (!process.Start()) throw new InvalidOperationException(path + "：离线 Shader 工具未启动。");
                process.BeginOutputReadLine(); process.BeginErrorReadLine();
                if (!process.WaitForExit(120000)) { process.Kill(); throw new TimeoutException(path + "：Shader 转换超过 120 秒。输出目录：" + output); }
                process.WaitForExit();
                if (process.ExitCode != 0) throw new InvalidOperationException(path + "：Shader 转换失败。\n" + stdout + stderr);
            }
        }

        //Windows 参数中的反斜杠在引号前需要加倍，不能直接拼接源文件路径。
        private static string Quote(string value) => "\"" + Regex.Replace(value, @"(\\*)(""|$)", match =>
            match.Groups[1].Value + match.Groups[1].Value + (match.Groups[2].Value == "\"" ? "\\\"" : "")) + "\"";
    }
}


