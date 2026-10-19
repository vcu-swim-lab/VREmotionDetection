using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

// ReadmeExamples/*.cs is compiled against the real API (ReadmeExamples.csproj). That only protects the docs if the
// examples ARE the docs, so this fails when a recipe in Documentation~/UsageGuide.md and its example file differ.
public class DocsRecipeSyncTests
{
    private static string HarnessRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "ReadmeExamples")) &&
                File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("DotnetHarness folder not found above " + AppContext.BaseDirectory);
    }

    private static string PackageRoot()
    {
        string fromEnvironment = Environment.GetEnvironmentVariable("PACKAGE_ROOT");
        if (!string.IsNullOrEmpty(fromEnvironment))
        {
            return fromEnvironment;
        }

        for (var dir = new DirectoryInfo(HarnessRoot()); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "package.json")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("package.json not found above the harness");
    }

    private static string Normalize(string code) =>
        string.Join("\n", code.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd())).Trim();

    private static IEnumerable<string> CSharpBlocks(string markdown)
    {
        foreach (Match m in Regex.Matches(markdown, "```csharp\\r?\\n(.*?)```", RegexOptions.Singleline))
        {
            yield return m.Groups[1].Value;
        }
    }

    private static string ClassNameOf(string block)
    {
        var m = Regex.Match(block, @"^public (?:sealed )?class (\w+)", RegexOptions.Multiline);
        return m.Success ? m.Groups[1].Value : null;
    }

    [Test]
    public void Every_whole_class_recipe_in_the_usage_guide_is_an_example_file_and_matches_it()
    {
        string guide = File.ReadAllText(Path.Combine(PackageRoot(), "Documentation~", "UsageGuide.md"));
        string examples = Path.Combine(HarnessRoot(), "ReadmeExamples");
        var recipes = CSharpBlocks(guide).Select(b => (name: ClassNameOf(b), code: b)).Where(r => r.name != null).ToList();

        Assert.GreaterOrEqual(recipes.Count, 8, "the guide should still contain its whole-class recipes");

        foreach (var (name, code) in recipes)
        {
            // The file is found by the class it declares (CustomBackendExample.cs holds AlwaysNeutralBackend).
            var files = Directory.GetFiles(examples, "*.cs")
                .Where(f => Regex.IsMatch(File.ReadAllText(f), @"^public (?:sealed )?class " + name + @"\b", RegexOptions.Multiline))
                .ToList();

            Assert.AreEqual(1, files.Count, $"UsageGuide recipe '{name}' must be in exactly one ReadmeExamples/*.cs file so it is compiled.");
            Assert.AreEqual(Normalize(code), Normalize(File.ReadAllText(files[0])),
                $"ReadmeExamples/{Path.GetFileName(files[0])} differs from the '{name}' recipe in Documentation~/UsageGuide.md - copy the recipe over it.");
        }
    }

    [Test]
    public void The_readme_snippets_are_still_covered_by_the_fragments_file()
    {
        string readme = File.ReadAllText(Path.Combine(PackageRoot(), "README.md"));
        string fragments = File.ReadAllText(Path.Combine(HarnessRoot(), "ReadmeExamples", "ReadmeFragments.cs"));
        var blocks = CSharpBlocks(readme).ToList();

        Assert.GreaterOrEqual(blocks.Count, 1);

        // Every statement of a README snippet must appear (modulo whitespace) in ReadmeFragments.cs.
        string flat = Regex.Replace(fragments, @"\s+", " ");
        foreach (string block in blocks)
        {
            foreach (string line in block.Replace("\r", "").Split('\n').Select(l => l.Trim()))
            {
                if (line.Length == 0 || line.StartsWith("//") || line.StartsWith("using ") || line == "{" || line == "}" ||
                    line.StartsWith("[RequireComponent") || line.StartsWith("public class") || line.StartsWith("async void Start") ||
                    line.StartsWith("var predictor = GetComponent"))
                {
                    continue;
                }

                StringAssert.Contains(Regex.Replace(line, @"\s+", " "), flat,
                    "README snippet line is not in ReadmeExamples/ReadmeFragments.cs - update the fragments file.");
            }
        }
    }
}
