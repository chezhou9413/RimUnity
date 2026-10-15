using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace RimUnity.Editor
{
    internal sealed class ManualArticle
    {
        public string title, source, file, category, body;
        public bool Matches(string search) => string.IsNullOrWhiteSpace(search)
            || (title + "\n" + source + "\n" + body).IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
    }

    internal static class ManualLibrary
    {
        internal static List<ManualArticle> Load(string root)
        {
            var articles = new List<ManualArticle>();
            foreach (string file in Directory.GetFiles(Path.Combine(root, "Guide"), "*.md").OrderBy(p => p, StringComparer.Ordinal))
            {
                string text = File.ReadAllText(file);
                var heading = Regex.Match(text, @"(?m)^# (.+)$");
                articles.Add(new ManualArticle { title = heading.Success ? heading.Groups[1].Value.Trim() : Path.GetFileNameWithoutExtension(file),
                    file = file, category = "说明书", source = "Guide/" + Path.GetFileName(file), body = text });
            }
            var sources = JsonConvert.DeserializeObject<List<ManualArticle>>(File.ReadAllText(Path.Combine(root, "sources.json")));
            foreach (var article in sources)
            {
                article.file = Path.GetFullPath(Path.Combine(root, article.file));
                if (!article.file.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("说明书索引包含包外路径。");
                article.body = File.ReadAllText(article.file);
                articles.Add(article);
            }
            return articles;
        }

        internal static ManualArticle ResolveLink(IEnumerable<ManualArticle> articles, ManualArticle current, string link)
        {
            string relative = Uri.UnescapeDataString(link.Split('#')[0]);
            if (string.IsNullOrEmpty(relative)) return current;
            string directory = Path.GetDirectoryName(current.source.Replace('/', Path.DirectorySeparatorChar)) ?? "";
            //Compare virtual repository paths; never open arbitrary local paths from Markdown.
            string target = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "RimUnityManual", directory, relative));
            return articles.FirstOrDefault(a => string.Equals(target,
                Path.GetFullPath(Path.Combine(Path.GetTempPath(), "RimUnityManual", a.source)), StringComparison.OrdinalIgnoreCase));
        }
    }
}
