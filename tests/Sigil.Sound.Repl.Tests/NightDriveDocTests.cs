using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Sigil.Sound.Repl.Tests;

/// <summary>The guide "A whole piece" shows the REPL's opening example in full; the two must not drift apart.</summary>
public class NightDriveDocTests
{
    private static string Normalize(string code) => string.Join("\n", code.Replace("\r\n", "\n").Trim().Split('\n').Select(l => l.TrimEnd()));

    [Fact]
    public void The_guide_shows_the_opening_example_as_it_is()
    {
        var example = Examples.All[0];
        Assert.Equal("Night drive", example.Name);

        var guide = File.ReadAllText(GuidePath());
        var blocks = Regex.Matches(guide, "```csharp\r?\n(.*?)```", RegexOptions.Singleline);
        var whole = blocks[^1].Groups[1].Value;

        Assert.Equal(Normalize(example.Code), Normalize(whole));
    }

    private static string GuidePath([CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "docs", "articles", "night-drive.md"));
}
