using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Sigil.Sound.Testing;

namespace Sigil.Sound.Repl.Tests;

/// <summary>
/// Every <c>&lt;example&gt;&lt;code&gt;</c> in the library's XML docs is a complete REPL snippet (docs/articles/writing-examples.md).
/// The guides' REPL snippets are checked the same way. This compiles each one under the REPL page's own profile and
/// renders a second, so a doc example that does not sound fails here instead of in a reader's browser.
/// </summary>
/// <remarks>
/// Examples written before the rule are listed in <c>DocExamples.baseline.txt</c> and are expected to fail until fixed.
/// Both directions are checked, so the list can only shrink: a new failing example is an error, and so is a listed one
/// that now passes. Set <c>SIGIL_DOCS_WRITE_BASELINE=1</c> to rewrite the list from the current results.
/// </remarks>
public class DocExampleTests
{
    private const string BaselineFile = "DocExamples.baseline.txt";
    private static readonly SnippetProfile Profile = ReplProfile.Instance;

    /// <summary>An example's stable id: the documented member, then its position among that member's examples.</summary>
    private sealed record DocExample(string Id, string Code);

    private static IEnumerable<DocExample> Collect()
    {
        foreach (var assembly in new[] { typeof(Sound).Assembly, typeof(Notes).Assembly })
        {
            var xml = Path.ChangeExtension(assembly.Location, ".xml");
            Assert.True(File.Exists(xml), $"XML docs not found next to {assembly.GetName().Name}; GenerateDocumentationFile must stay on.");

            foreach (var member in XDocument.Load(xml).Descendants("member"))
            {
                var codes = member.Elements("example").SelectMany(e => e.Elements("code")).ToList();
                for (int i = 0; i < codes.Count; i++)
                    yield return new DocExample($"{member.Attribute("name")!.Value}#{i + 1}", Dedent(codes[i].Value));
            }
        }
    }

    /// <summary>The REPL snippets in the guides: every fenced C# block that is not project code (it does not start with <c>using</c>).</summary>
    private static IEnumerable<DocExample> CollectArticles([CallerFilePath] string source = "")
    {
        var articles = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "docs", "articles"));
        Assert.True(Directory.Exists(articles), $"Guides not found at {articles}.");

        foreach (var file in Directory.GetFiles(articles, "*.md", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            int n = 0;
            foreach (Match block in Regex.Matches(File.ReadAllText(file), "```csharp\r?\n(.*?)```", RegexOptions.Singleline))
            {
                var code = block.Groups[1].Value.TrimEnd();
                if (code.TrimStart().StartsWith("using ", StringComparison.Ordinal))
                    continue;
                yield return new DocExample($"{Path.GetRelativePath(articles, file)}#{++n}", code);
            }
        }
    }

    // XML keeps the comment's indentation; drop the common indent and the blank edges so the snippet reads as typed.
    private static string Dedent(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0])) lines.RemoveAt(0);
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1])) lines.RemoveAt(lines.Count - 1);
        int indent = lines.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
        return string.Join("\n", lines.Select(l => l.Length >= indent ? l[indent..] : l.TrimStart()));
    }

    /// <summary>Why the example fails, or null when it compiles and sounds.</summary>
    private static string? Problem(DocExample example)
    {
        var result = SnippetCompiler.Compile(example.Code, Profile);
        if (!result.Success)
            return "does not compile: " + string.Join("; ", result.Diagnostics.Where(d => d.IsError).Select(d => d.Message));

        try
        {
            var audio = HostRender.Render(result.Run(), 1.0);
            if (SignalAnalysis.HasNonFinite(audio.Left()) || SignalAnalysis.HasNonFinite(audio.Right()))
                return "renders NaN or infinite samples";
            if (SignalAnalysis.Peak(audio.ToMono()) < 1e-3f)
                return "is silent for its first second";
            return null;
        }
        catch (Exception e)
        {
            return "throws: " + e.Message;
        }
    }

    private static string BaselinePath([System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
        Path.Combine(Path.GetDirectoryName(source)!, BaselineFile);

    [Fact]
    public void Doc_examples_sound_and_the_baseline_only_shrinks()
    {
        var examples = Collect().Concat(CollectArticles()).ToList();
        Assert.NotEmpty(examples);

        var problems = examples.Select(e => (e.Id, Problem: Problem(e))).Where(p => p.Problem is not null).ToList();

        if (Environment.GetEnvironmentVariable("SIGIL_DOCS_WRITE_BASELINE") == "1")
        {
            File.WriteAllLines(BaselinePath(), problems.Select(p => p.Id).Order(StringComparer.Ordinal));
            return;
        }

        var baseline = File.Exists(BaselinePath())
            ? File.ReadAllLines(BaselinePath()).Where(l => l.Trim().Length > 0).ToHashSet()
            : [];

        var newlyBroken = problems.Where(p => !baseline.Contains(p.Id)).Select(p => $"{p.Id} {p.Problem}").ToList();
        var nowFixed = baseline.Except(problems.Select(p => p.Id)).Order(StringComparer.Ordinal).ToList();

        Assert.True(newlyBroken.Count == 0, "Doc examples that do not sound:\n" + string.Join("\n", newlyBroken));
        Assert.True(nowFixed.Count == 0,
            $"These examples now pass; remove them from {BaselineFile}:\n" + string.Join("\n", nowFixed));
    }
}
