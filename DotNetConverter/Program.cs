using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Parsers;
using Markdig.Syntax;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Markdig.Renderers.Normalize;
using Markdig.Syntax.Inlines;
using ReverseMarkdown;
using YamlDotNet.RepresentationModel;

namespace DotNetConverter
{
    internal class Program
    {
        private static readonly Regex matchMetadataSection = new Regex("^---\n.*\n---", RegexOptions.Singleline);

        private static async Task Main(string[] args)
        {
            var fileOutputDirectories = new ConcurrentDictionary<(string title, string toc), ConcurrentBag<MarkdownDocument>>();
            var oldFileNameToNew = new ConcurrentDictionary<string, (string title, string toc)>();
            var directoryInfo = new DirectoryInfo(@"C:\OpenSource\dotnet-archive");

            var parallelOptions = new ParallelOptions() {MaxDegreeOfParallelism = 1};

            Parallel.ForEach(directoryInfo.EnumerateFiles("*.md", SearchOption.AllDirectories), parallelOptions, file => ReadFileData(file, fileOutputDirectories, oldFileNameToNew));

            Parallel.ForEach(fileOutputDirectories, parallelOptions, kvp => UpdateLinks(kvp.Value, oldFileNameToNew));

            DirectoryInfo outputPath = new DirectoryInfo(@"C:\OpenSource\website/input/reactive-extensions");
            outputPath.Delete(true);

            foreach (var entry in fileOutputDirectories)
            {
                var newDirectory = outputPath.CreateSubdirectory(entry.Key.toc);
                string newFileName = Path.Combine(newDirectory.FullName, entry.Key.title);

                await File.WriteAllTextAsync(newFileName, string.Join("\r\n", entry.Value.Select(x => ToMd(x))));
            }
        }

        private static void UpdateLinks(ConcurrentBag<MarkdownDocument> documents, ConcurrentDictionary<string, (string title, string toc)> oldFileNameToNew)
        {
            foreach (var document in documents)
            {
                var links = document.Descendants().OfType<LinkInline>();

                foreach (var link in links)
                {
                    if (oldFileNameToNew.TryGetValue(link.Url, out var newName))
                    {
                        link.Url = Path.Combine(newName.toc, newName.title);
                    }
                }
            }
        }

        private static void ReadFileData(FileInfo file, ConcurrentDictionary<(string title, string toc), ConcurrentBag<MarkdownDocument>> fileOutputDirectories, ConcurrentDictionary<string, (string title, string toc)> oldFileNameToNew)
        {
            StreamReader contentStreamReader = file.OpenText();
            string fileContents = contentStreamReader.ReadToEnd();

            var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().UseYamlFrontMatter().Build();
            var document = MarkdownParser.Parse(fileContents, pipeline);

            var (title, toc) = GetFileNameAndDirectory(file, document);

            ProcessHtmlBlocks(document, pipeline);

            string newFileName = GetSubString(title, new[] { " " }) + ".md";

            string newDirectory = GetSubString(toc, new[] { " ", "(", "_", "-" });

            var documents = fileOutputDirectories.GetOrAdd((newFileName, newDirectory), _ => new ConcurrentBag<MarkdownDocument>());
            documents.Add(document);
            oldFileNameToNew.TryAdd(file.Name, (newFileName, newDirectory));
        }

        /// <summary>
        /// Gets the file name and the directory by extracting metadata from the MD Document front matter block.
        /// </summary>
        /// <param name="file">The file being read.</param>
        /// <param name="document">The mark down document being processed.</param>
        /// <returns>The file name and directory.</returns>
        private static (string title, string toc) GetFileNameAndDirectory(FileInfo file, MarkdownDocument document)
        {
            var yamlFrontData = document.Descendants<YamlFrontMatterBlock>().FirstOrDefault();

            var yaml = new YamlStream();
            string title = Path.GetFileNameWithoutExtension(file.Name);
            string toc = string.Empty;

            if (yamlFrontData != null)
            {
                yaml.Load(new StringReader(yamlFrontData?.Lines.ToString().TrimStart('-').TrimEnd('-').Trim()));

                YamlNode yamlRootNode = yaml.Documents[0].RootNode;

                title = ((string)yamlRootNode["title"]) ?? Path.GetFileNameWithoutExtension(file.Name);
                toc = ((string)yamlRootNode["TOCTitle"]).Replace(" ", "-") ?? string.Empty;

                document.Remove(yamlFrontData);
            }

            return (title, toc);
        }

        /// <summary>
        /// Convert HTML blocks to MD blocks inside the document.
        /// </summary>
        /// <param name="document">The document to scan for HTML sections.</param>
        /// <param name="pipeline">The MD pipeline.</param>
        private static void ProcessHtmlBlocks(MarkdownDocument document, MarkdownPipeline pipeline)
        {
            var htmlBlocks = document.Descendants<HtmlBlock>().ToList();
            foreach (var htmlBlock in htmlBlocks)
            {
                var converter = new Converter(new Config(Config.UnknownTagsOption.Drop, true, true));
                var convertedMarkDown = converter.Convert(htmlBlock.Lines.ToString());
                var markdownBlock = MarkdownParser.Parse(convertedMarkDown, pipeline);

                var index = document.IndexOf(htmlBlock);
                document.Remove(htmlBlock);

                document.Insert(index, markdownBlock);
            }
        }

        private static string ToMd(MarkdownObject document)
        {
            var writer = new StringWriter();
            var normalizeRenderer = new NormalizeRenderer(writer);
            normalizeRenderer.Render(document);
            writer.Flush();
            return writer.ToString();
        }

        private static string GetSubString(string title, string[] separators)
        {
            string newFileName;
            int indexOf = 0;
            try
            {
                List<int> values = separators.Select(separator => title.IndexOf(separator, StringComparison.Ordinal)).Where(x => x > 0).ToList();
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
