using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Sigil.Sound.Repl;

/// <summary>
/// Whether a snippet uses the page's <c>Keys</c>. The page shows the keyboard only then: for any other snippet the keys
/// would do nothing, and a control that does nothing reads as broken.
/// </summary>
/// <remarks>
/// Decided from the text, not from which example is selected, so it holds for edited snippets and shared links too.
/// It looks at tokens only, so a comment or a string that mentions Keys does not count. It does not resolve names, so a
/// local called <c>Keys</c> shows the keyboard needlessly, which is harmless; hiding it for a snippet that does use it would not be.
/// </remarks>
internal static class KeyboardUsage
{
    public static bool Uses(string code)
    {
        SyntaxToken previous = default;
        SyntaxToken beforePrevious = default;
        foreach (var token in SyntaxFactory.ParseTokens(code))
        {
            if (token.IsKind(SyntaxKind.IdentifierToken) && token.ValueText == "Keys" && !IsMemberOfSomethingElse(previous, beforePrevious))
                return true;
            beforePrevious = previous;
            previous = token;
        }

        return false;
    }

    // "map.Keys" is a member of map, not the page's Keys; "ReplApi.Keys" spelled out in full is the page's.
    private static bool IsMemberOfSomethingElse(SyntaxToken previous, SyntaxToken beforePrevious) =>
        previous.IsKind(SyntaxKind.DotToken) && !(beforePrevious.IsKind(SyntaxKind.IdentifierToken) && beforePrevious.ValueText == "ReplApi");
}
