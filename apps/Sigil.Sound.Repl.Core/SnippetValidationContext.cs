using Microsoft.CodeAnalysis;

namespace Sigil.Sound.Repl;

/// <summary>
/// What a host's own rule gets to look at when it checks a snippet (see the <c>extraValidation</c> argument of
/// <see cref="SnippetProfile"/>): the bound tree, and a way to report problems at the user's text.
/// </summary>
/// <remarks>
/// A host rule runs after the built-in checks passed, so it never has to repeat them, and it can only add errors. The
/// tree is the wrapped file the compiler builds, which also holds generated code around what the user typed; use
/// <see cref="IsUserText"/> to skip that.
/// </remarks>
public sealed class SnippetValidationContext
{
    private readonly SnippetDocument _document;
    private readonly List<SnippetDiagnostic> _diagnostics;

    internal SnippetValidationContext(SemanticModel model, SnippetDocument document, List<SnippetDiagnostic> diagnostics)
    {
        Model = model;
        Root = document.Tree.GetRoot();
        _document = document;
        _diagnostics = diagnostics;
    }

    /// <summary>The semantic model of the wrapped snippet.</summary>
    public SemanticModel Model { get; }

    /// <summary>The root of the wrapped snippet's syntax tree.</summary>
    public SyntaxNode Root { get; }

    /// <summary>True when <paramref name="node"/> starts inside text the user typed rather than the generated wrapper.</summary>
    public bool IsUserText(SyntaxNode node) => _document.IsUserText(node.Span);

    /// <summary>Reports an error at <paramref name="node"/>, shown at the matching place in the user's text.</summary>
    /// <param name="id">A short identifier for the rule, such as <c>HOST01</c>.</param>
    /// <param name="message">What is wrong and, where possible, what to do instead.</param>
    /// <param name="node">The offending syntax.</param>
    public void Report(string id, string message, SyntaxNode node) =>
        _diagnostics.Add(SnippetSecurityValidator.CreateDiagnostic(id, message, node.GetLocation()));
}
