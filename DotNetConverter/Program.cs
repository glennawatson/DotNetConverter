using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using YamlDotNet.RepresentationModel;

namespace DotNetConverter
{
    class Program
    {
        private static Regex matchMetadataSection = new Regex("^---\n.*\n---", RegexOptions.Singleline);
        static async Task Main(string[] args)
        {
            var fileOutputDirectory = new ConcurrentDictionary<(string title, string toc), string>();
            var oldFileNameToNew = new ConcurrentDictionary<string, (string title, string toc)>();
            var directoryInfo = new DirectoryInfo("/Users/abc/code/dotnet-archive/reactive-extensions");

            Parallel.ForEach(directoryInfo.EnumerateFiles("*.md"), file =>
            {
                var contentStreamReader = file.OpenText();
                var fileContents = contentStreamReader.ReadToEnd();

                var metadataContents = matchMetadataSection.Match(fileContents).Value;
                var contents = fileContents.Substring(metadataContents.Length).Trim('\r', '\n');

                // Load the stream
                var yaml = new YamlStream();
                yaml.Load(new StringReader(metadataContents.TrimStart('-').TrimEnd('-').Trim()));

                var yamlRootNode = yaml.Documents[0].RootNode;

                var title = ((string)yamlRootNode["title"]);
                var toc = ((string)yamlRootNode["TOCTitle"]).Replace(" ", "-");

                string newFileName = GetSubString(title, new[] { " " }) + ".md";

                string newDirectory = GetSubString(toc, new[] { " ", "(", "_", "-" });

                fileOutputDirectory.AddOrUpdate((newFileName, newDirectory), contents, (key, oldContents) => oldContents + contents);
                oldFileNameToNew.TryAdd(file.Name, (newFileName, newDirectory));
            });

            var outputPath = new DirectoryInfo("/Users/abc/code/website/input/rx-docs");
            outputPath.Delete(true);

            foreach (var entry in fileOutputDirectory)
            {
                var newDirectory = outputPath.CreateSubdirectory(entry.Key.toc);
                var newFileName = Path.Combine(newDirectory.FullName, entry.Key.title);

                await File.WriteAllTextAsync(newFileName, entry.Value);
            }
        }

        private static string GetSubString(string title, string[] separators)
        {
            string newFileName;
            int indexOf = 0;
            try
            {
                var values = separators.Select(separator => title.IndexOf(separator, StringComparison.Ordinal)).Where(x => x > 0).ToList();
                indexOf = values.Count > 0 ? values.Min() : -1;
            }
            catch
            {
                indexOf = -1;
            }

            if (indexOf > 0)
            {
                newFileName = title.Substring(0, indexOf);
            }
            else
            {
                newFileName = title;
            }

            return newFileName;
        }
    }
}
