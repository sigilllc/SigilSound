using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Sigil.Sound.Repl;

/// <summary>
/// Whether a snippet uses the Sigilgraph device API (<c>Knob</c>, <c>Slider</c>, <c>Asset</c>, <c>Poly</c>, <c>In.</c>, <c>Out.</c>).
/// The page offers Sigilgraph mode when a snippet typed in plain mode does, instead of showing a bare "name does not exist" error.
/// </summary>
/// <remarks>
/// Decided from tokens, like <see cref="KeyboardUsage"/>: a comment or a string that mentions Knob does not count, and a member
/// called <c>x.Knob</c> is not the device's. Names are not resolved, so a local called <c>In</c> triggers the offer needlessly,
/// which is harmless; missing a real use would not be.
/// </remarks>
internal static class SigilgraphUsage
{
    private static readonly HashSet<string> Calls = new(StringComparer.Ordinal) { "Knob", "Slider", "Asset", "Poly" };
    private static readonly HashSet<string> FaceplateCalls = new(StringComparer.Ordinal) { "Knob", "Slider", "Asset" };

    public static bool Uses(string code) => Scan(code, Calls);

    /// <summary>
    /// Whether it declares something for the faceplate strip: a knob, slider or sample slot, or reads or writes an input or output.
    /// <c>Poly</c> alone does not, since its notes come from the keyboard, so a snippet that only plays notes gets no empty strip.
    /// </summary>
    public static bool UsesFaceplate(string code) => Scan(code, FaceplateCalls);

    /// <summary>Whether it plays notes from the keyboard (<c>Poly</c>), so the page shows the keys in Sigilgraph mode too.</summary>
    public static bool UsesPoly(string code)
    {
        var tokens = SyntaxFactory.ParseTokens(code).ToArray();
        for (int i = 0; i < tokens.Length; i++)
        {
            if (tokens[i].IsKind(SyntaxKind.IdentifierToken) && tokens[i].ValueText == "Poly"
                && !(i > 0 && tokens[i - 1].IsKind(SyntaxKind.DotToken))
                && i + 1 < tokens.Length && tokens[i + 1].IsKind(SyntaxKind.OpenParenToken))
                return true;
        }

        return false;
    }

    /// <summary>Whether it reads the notes at the device's Note In (<c>In.Notes</c>), so the page shows the keys too.</summary>
    public static bool UsesNotes(string code)
    {
        var tokens = SyntaxFactory.ParseTokens(code).ToArray();
        for (int i = 0; i + 2 < tokens.Length; i++)
        {
            if (tokens[i].IsKind(SyntaxKind.IdentifierToken) && tokens[i].ValueText == "In"
                && !(i > 0 && tokens[i - 1].IsKind(SyntaxKind.DotToken))
                && tokens[i + 1].IsKind(SyntaxKind.DotToken)
                && tokens[i + 2].IsKind(SyntaxKind.IdentifierToken) && tokens[i + 2].ValueText == "Notes")
                return true;
        }

        return false;
    }

    private static bool Scan(string code, HashSet<string> calls)
    {
        var tokens = SyntaxFactory.ParseTokens(code).Where(t => !t.IsKind(SyntaxKind.EndOfFileToken)).ToArray();
        for (int i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (!token.IsKind(SyntaxKind.IdentifierToken))
                continue;
            bool afterDot = i > 0 && tokens[i - 1].IsKind(SyntaxKind.DotToken);
            if (afterDot)
                continue;
            var next = i + 1 < tokens.Length ? tokens[i + 1].Kind() : SyntaxKind.None;
            if (calls.Contains(token.ValueText) && next == SyntaxKind.OpenParenToken)
                return true;
            if (token.ValueText is "In" or "Out" && next == SyntaxKind.DotToken)
                return true;
        }

        return false;
    }
}
