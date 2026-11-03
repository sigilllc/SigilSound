using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Sigil.Sound.Repl;

/// <summary>
/// Wraps what the user typed into a compilable file. A snippet is either one expression or
/// statements ending in an expression (no semicolon), and that last expression is the sound to play:
/// <code>
/// var lfo = Sine(0.25).Range(200, 2000);
/// Saw(110).LowPass(lfo)
/// </code>
/// <c>#line</c> directives map compiler errors back to the user's lines and columns, and
/// <see cref="ToWrapped"/> maps editor offsets into the wrapped file for completion.
/// </summary>
internal sealed class SnippetDocument
{
    public const string FileName = "snippet";
    public const string ClassName = "__Snippet";
    public const string MethodName = "__Run";

    private const string Header =
        """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using Sigil.Sound;
        using Sigil.Sound.Notes;
        using static System.Math;
        using static Sigil.Sound.Sounds;

        public static class __Snippet
        {
            public static Sound __Run()
            {

        """;

    // Each piece of the user's text and where it starts in the wrapped file.
    private readonly (int UserStart, int UserEnd, int WrappedStart)[] _pieces;

    private SnippetDocument(string userCode, string wrapped, (int, int, int)[] pieces)
    {
        UserCode = userCode;
        Wrapped = wrapped;
        _pieces = pieces;
        Tree = CSharpSyntaxTree.ParseText(wrapped, ParseOptions);
    }

    public static CSharpParseOptions ParseOptions { get; } = new(LanguageVersion.Latest);

    public string UserCode { get; }

    public string Wrapped { get; }

    public SyntaxTree Tree { get; }

    public static SnippetDocument Create(string userCode)
    {
        userCode = userCode.Replace("\r\n", "\n");
        int? split = FinalExpressionStart(userCode);

        var text = new StringBuilder(Header);
        var pieces = new List<(int, int, int)>();
        text.Append("#line 1 \"").Append(FileName).Append("\"\n");

        if (split is int at)
        {
            pieces.Add((0, at, text.Length));
            text.Append(userCode, 0, at);

            // "return" goes on a hidden line of its own; padding keeps the expression at its own column.
            int line = 1 + userCode.AsSpan(0, at).Count('\n');
            int column = at - (userCode.LastIndexOf('\n', Math.Max(0, at - 1)) + 1);
            if (at == 0) column = 0;
            text.Append("\n#line hidden\nreturn\n#line ").Append(line).Append(" \"").Append(FileName).Append("\"\n");
            text.Append(' ', column);

            pieces.Add((at, userCode.Length, text.Length));
            text.Append(userCode, at, userCode.Length - at);
            text.Append("\n#line hidden\n;");
        }
        else
        {
            pieces.Add((0, userCode.Length, text.Length));
            text.Append(userCode);
        }

        text.Append("\n#line default\n    }\n}\n");
        return new SnippetDocument(userCode, text.ToString(), pieces.ToArray());
    }

    /// <summary>Maps an offset in the user's text to the wrapped file.</summary>
    public int ToWrapped(int userOffset)
    {
        userOffset = Math.Clamp(userOffset, 0, UserCode.Length);
        // Prefer the later piece at a boundary, so the cursor sits right before the final expression.
        for (int i = _pieces.Length - 1; i >= 0; i--)
        {
            var (start, end, wrappedStart) = _pieces[i];
            if (userOffset >= start && userOffset <= end)
                return wrappedStart + userOffset - start;
        }
        return _pieces[^1].WrappedStart;
    }

    /// <summary>Normalizes an editor offset counted in text that may use CRLF line endings.</summary>
    public static int NormalizeOffset(string original, int offset)
    {
        offset = Math.Clamp(offset, 0, original.Length);
        int crs = 0;
        for (int i = 0; i < offset; i++)
            if (original[i] == '\r' && i + 1 < original.Length && original[i + 1] == '\n')
                crs++;
        return offset - crs;
    }

    /// <summary>Where the final expression starts, when the snippet ends in one without a semicolon.</summary>
    private static int? FinalExpressionStart(string userCode)
    {
        if (string.IsNullOrWhiteSpace(userCode))
            return null;

        // Parse inside braces, so "{" shifts every position by one.
        var block = (BlockSyntax)SyntaxFactory.ParseStatement("{" + userCode + "\n}", options: ParseOptions);
        if (block.Statements.LastOrDefault() is ExpressionStatementSyntax { SemicolonToken.IsMissing: true } last)
            return last.SpanStart - 1;

        // An expression that isn't a valid statement on its own, such as "Sine(440) * 0.5",
        // parses as several broken statements; treat the whole snippet as one expression instead.
        var expression = SyntaxFactory.ParseExpression(userCode, options: ParseOptions);
        if (!expression.ContainsDiagnostics && expression.FullSpan.Length == userCode.Length)
            return expression.SpanStart;

        return null;
    }
}
