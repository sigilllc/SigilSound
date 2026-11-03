using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Sigil.Sound.Repl;

/// <summary>One completion suggestion. <see cref="Kind"/> uses Monaco's names (Method, Property, Class, ...).</summary>
public sealed record CompletionItem(string Label, string Kind, string Detail, string? Documentation, string SortText);

/// <summary>One overload in a signature popup; each parameter is a [start, end) range in <see cref="Label"/>.</summary>
public sealed record SignatureInfo(string Label, string? Documentation, IReadOnlyList<ParameterInfo> Parameters);

public sealed record ParameterInfo(int Start, int End, string? Documentation);

public sealed record SignatureHelp(IReadOnlyList<SignatureInfo> Signatures, int ActiveSignature, int ActiveParameter);

/// <summary>Completion, signature help and documentation for snippets, answered from Roslyn's semantic model.</summary>
public static partial class EditorServices
{
    private static readonly SymbolDisplayFormat SignatureFormat = SymbolDisplayFormat.MinimallyQualifiedFormat
        .WithParameterOptions(SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeName |
                              SymbolDisplayParameterOptions.IncludeDefaultValue | SymbolDisplayParameterOptions.IncludeParamsRefOut)
        .WithMemberOptions(SymbolDisplayMemberOptions.IncludeParameters | SymbolDisplayMemberOptions.IncludeType |
                           SymbolDisplayMemberOptions.IncludeContainingType);

    // Hover shows the type once, in the signature's first word, so the containing type is left out.
    private static readonly SymbolDisplayFormat DescribeFormat = SignatureFormat
        .WithMemberOptions(SymbolDisplayMemberOptions.IncludeParameters | SymbolDisplayMemberOptions.IncludeType);

    private static readonly string[] Keywords =
        ["var", "new", "return", "double", "float", "int", "bool", "true", "false", "null", "static", "if", "else", "for", "foreach", "in"];

    /// <summary>Runs each service once, so the first real request while typing isn't the slow one.</summary>
    public static void Warm()
    {
        const string code = "Saw(110).LowPass(";
        _ = Complete(code, 9);
        _ = Complete(code, 2);
        _ = Signatures(code, code.Length);
        _ = Describe(code, 1);
    }

    /// <summary>
    /// Documentation for the symbol under the cursor as Markdown: its signature, summary, parameters,
    /// return value, remarks and examples. Null when the cursor is not on a documented symbol.
    /// </summary>
    public static string? Describe(string code, int offset, SnippetProfile? profile = null)
    {
        var document = SnippetDocument.Create(code, profile);
        int position = document.ToWrapped(SnippetDocument.NormalizeOffset(code, offset));
        var model = SnippetCompiler.CreateCompilation(document).GetSemanticModel(document.Tree);
        var token = document.Tree.GetRoot().FindToken(position);
        if (!token.IsKind(SyntaxKind.IdentifierToken) || token.Parent is null)
            return null;

        var info = model.GetSymbolInfo(token.Parent);
        var symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault() ?? model.GetDeclaredSymbol(token.Parent);
        if (symbol is null)
            return null;

        // Constructors read better under their type's name, and a bare local needs no more than its type.
        string signature = symbol switch
        {
            ILocalSymbol local => $"(local) {local.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {local.Name}",
            IParameterSymbol parameter => $"(parameter) {parameter.ToDisplayString(DescribeFormat)}",
            _ => symbol.ToDisplayString(DescribeFormat),
        };

        var markdown = new StringBuilder();
        markdown.Append("```csharp\n").Append(signature).Append("\n```");

        var docs = Docs(symbol);
        if (docs is null)
            return markdown.ToString();

        if (docs.Summary is { } summary)
            markdown.Append("\n\n").Append(summary);

        // Document every parameter of the method, not only the one under the cursor.
        if (symbol is IMethodSymbol method)
        {
            var parameters = method.Parameters
                .Select(p => (p.Name, Text: docs.Param(p.Name)))
                .Where(p => p.Text is not null)
                .ToList();
            if (parameters.Count > 0)
            {
                markdown.Append("\n");
                foreach (var (name, text) in parameters)
                    markdown.Append("- `").Append(name).Append("`: ").Append(text).Append('\n');
            }
        }

        if (docs.Returns is { } returns)
            markdown.Append("\n**Returns** ").Append(returns);
        if (docs.Remarks is { } remarks)
            markdown.Append("\n\n").Append(remarks);
        if (docs.Example is { } example)
            markdown.Append("\n\n**Example**\n```csharp\n").Append(example).Append("\n```");

        return markdown.ToString().TrimEnd();
    }

    public static IReadOnlyList<CompletionItem> Complete(string code, int offset, SnippetProfile? profile = null)
    {
        var document = SnippetDocument.Create(code, profile);
        int position = document.ToWrapped(SnippetDocument.NormalizeOffset(code, offset));
        var model = SnippetCompiler.CreateCompilation(document).GetSemanticModel(document.Tree);
        var root = document.Tree.GetRoot();

        // The token just left of the word being typed decides the context.
        var token = root.FindToken(Math.Max(0, position - 1));
        if (token.IsKind(SyntaxKind.IdentifierToken) && token.Span.End >= position && token.SpanStart < position)
            token = token.GetPreviousToken();
        else if (token.Span.End > position)
            token = token.GetPreviousToken();

        if (token.IsKind(SyntaxKind.DotToken))
            return MemberCompletions(model, token, position, document.Profile);

        if (InsideStringOrComment(root, position))
            return [];

        var symbols = model.LookupSymbols(position)
            .Where(s => s is not INamespaceSymbol ns || ns.Name is "System" or "Sigil")
            .Where(s => Visible(s, document.Profile));
        var items = Group(symbols, model, position, rank: s => s.ContainingType?.ContainingNamespace?.ToDisplayString().StartsWith("Sigil") == true ? "0" : "1");
        items.AddRange(Keywords.Select(k => new CompletionItem(k, "Keyword", "keyword", null, "2" + k)));
        return items;
    }

    public static SignatureHelp? Signatures(string code, int offset, SnippetProfile? profile = null)
    {
        var document = SnippetDocument.Create(code, profile);
        int position = document.ToWrapped(SnippetDocument.NormalizeOffset(code, offset));
        var model = SnippetCompiler.CreateCompilation(document).GetSemanticModel(document.Tree);
        var token = document.Tree.GetRoot().FindToken(Math.Max(0, position - 1));

        // The innermost argument list the cursor is inside, between its parentheses.
        var list = token.Parent?.AncestorsAndSelf().OfType<ArgumentListSyntax>().FirstOrDefault(l =>
            position > l.OpenParenToken.SpanStart &&
            (l.CloseParenToken.IsMissing || position <= l.CloseParenToken.SpanStart));
        if (list is null)
            return null;

        IEnumerable<IMethodSymbol> candidates = list.Parent switch
        {
            InvocationExpressionSyntax invocation => model.GetMemberGroup(invocation.Expression).OfType<IMethodSymbol>(),
            BaseObjectCreationExpressionSyntax creation =>
                (model.GetTypeInfo(creation).Type as INamedTypeSymbol)?.InstanceConstructors.Where(c => c.DeclaredAccessibility == Accessibility.Public)
                ?? [],
            _ => [],
        };
        var methods = candidates.Where(m => Visible(m, document.Profile)).ToList();
        if (methods.Count == 0)
            return null;

        int activeParameter = list.Arguments.GetSeparators().Count(s => s.SpanStart < position);
        var resolved = list.Parent is ExpressionSyntax call ? model.GetSymbolInfo(call).Symbol as IMethodSymbol : null;
        int active = resolved is not null ? methods.FindIndex(m => SymbolEqualityComparer.Default.Equals(m.OriginalDefinition, resolved.OriginalDefinition)) : -1;
        if (active < 0)
            active = Math.Max(0, methods.FindIndex(m => m.Parameters.Length > activeParameter || m.Parameters.LastOrDefault()?.IsParams == true));

        return new SignatureHelp(methods.Select(DescribeSignature).ToList(), active, activeParameter);
    }

    private static SignatureInfo DescribeSignature(IMethodSymbol method)
    {
        var label = new StringBuilder();
        label.Append(method.MethodKind == MethodKind.Constructor ? method.ContainingType.Name : method.Name).Append('(');
        var parameters = new List<ParameterInfo>();
        var docs = Docs(method);
        foreach (var parameter in method.Parameters)
        {
            if (parameters.Count > 0) label.Append(", ");
            int start = label.Length;
            label.Append(parameter.ToDisplayString(SignatureFormat));
            parameters.Add(new ParameterInfo(start, label.Length, docs?.Param(parameter.Name)));
        }
        label.Append(')');
        if (!method.ReturnsVoid && method.MethodKind != MethodKind.Constructor)
            label.Append(" → ").Append(method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
        return new SignatureInfo(label.ToString(), docs?.Summary, parameters);
    }

    private static List<CompletionItem> MemberCompletions(SemanticModel model, SyntaxToken dot, int position, SnippetProfile profile)
    {
        ExpressionSyntax? left = dot.Parent switch
        {
            MemberAccessExpressionSyntax access => access.Expression,
            QualifiedNameSyntax qualified => qualified.Left,
            _ => null,
        };
        if (left is null)
            return [];

        var leftSymbol = model.GetSymbolInfo(left).Symbol;
        IEnumerable<ISymbol> symbols;
        if (leftSymbol is INamespaceSymbol ns)
        {
            symbols = model.LookupNamespacesAndTypes(position, ns);
        }
        else if (leftSymbol is INamedTypeSymbol type && IsTypeName(left, model))
        {
            symbols = model.LookupStaticMembers(position, type);
        }
        else
        {
            var instanceType = model.GetTypeInfo(left).Type;
            if (instanceType is null || instanceType.TypeKind == TypeKind.Error)
                return [];
            symbols = model.LookupSymbols(position, instanceType, includeReducedExtensionMethods: true)
                .Where(s => !s.IsStatic || s is IMethodSymbol { MethodKind: MethodKind.ReducedExtension });
        }

        return Group(symbols.Where(s => Visible(s, profile)), model, position,
            rank: s => s.ContainingType?.SpecialType is SpecialType.System_Object or SpecialType.System_ValueType ? "1" : "0");
    }

    private static bool IsTypeName(ExpressionSyntax left, SemanticModel model)
    {
        // "Note.X" is ambiguous when a local is also called Note; the semantic model settles it.
        var info = model.GetSymbolInfo(left).Symbol;
        return info is ITypeSymbol && model.GetTypeInfo(left).Type is { } t && SymbolEqualityComparer.Default.Equals(t, info);
    }

    private static List<CompletionItem> Group(IEnumerable<ISymbol> symbols, SemanticModel model, int position, Func<ISymbol, string> rank)
    {
        var items = new List<CompletionItem>();
        foreach (var group in symbols.GroupBy(s => s.Name))
        {
            var first = group.First();
            int overloads = group.Count();
            string detail = first.ToMinimalDisplayString(model, position, SignatureFormat);
            if (overloads > 1)
                detail += $"  (+{overloads - 1} overload{(overloads > 2 ? "s" : "")})";
            items.Add(new CompletionItem(first.Name, KindOf(first), detail, Docs(first)?.Summary, rank(first) + first.Name));
        }
        return items;
    }

    /// <summary>Whether completion and signature help may offer <paramref name="symbol"/>: public, not hidden, and not in a namespace the profile blocks.</summary>
    private static bool Visible(ISymbol symbol, SnippetProfile profile)
    {
        if (!symbol.CanBeReferencedByName ||
            symbol.IsImplicitlyDeclared ||
            symbol.Name.StartsWith("__", StringComparison.Ordinal) ||
            symbol.DeclaredAccessibility is not (Accessibility.Public or Accessibility.NotApplicable) ||
            symbol is IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator or MethodKind.Conversion or MethodKind.PropertyGet or MethodKind.PropertySet } ||
            symbol.GetAttributes().Any(a => a.AttributeClass?.Name is "ObsoleteAttribute" or "EditorBrowsableAttribute"))
        {
            return false;
        }

        var ns = (symbol as INamespaceSymbol) ?? symbol.ContainingNamespace;
        if (ns is not null && !ns.IsGlobalNamespace)
        {
            if (profile.IsBlockedNamespace(ns.ToDisplayString()))
                return false;
        }

        return true;
    }

    private static string KindOf(ISymbol symbol) => symbol switch
    {
        INamespaceSymbol => "Module",
        INamedTypeSymbol { TypeKind: TypeKind.Struct } => "Struct",
        INamedTypeSymbol { TypeKind: TypeKind.Enum } => "Enum",
        INamedTypeSymbol { TypeKind: TypeKind.Interface } => "Interface",
        INamedTypeSymbol => "Class",
        IMethodSymbol => "Method",
        IPropertySymbol => "Property",
        IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum } => "EnumMember",
        IFieldSymbol { IsConst: true } => "Constant",
        IFieldSymbol => "Field",
        IEventSymbol => "Event",
        ILocalSymbol or IRangeVariableSymbol => "Variable",
        IParameterSymbol => "Variable",
        _ => "Text",
    };

    private static bool InsideStringOrComment(SyntaxNode root, int position)
    {
        var trivia = root.FindTrivia(Math.Max(0, position - 1));
        if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
            return true;
        var token = root.FindToken(Math.Max(0, position - 1));
        return token.IsKind(SyntaxKind.StringLiteralToken) && token.Span.Contains(position - 1) && position < token.Span.End;
    }

    private static MemberDocs? Docs(ISymbol symbol)
    {
        var target = symbol is IMethodSymbol { ReducedFrom: { } original } ? original : symbol;
        string? xml = target.OriginalDefinition.GetDocumentationCommentXml();
        if (string.IsNullOrWhiteSpace(xml))
            return null;
        try
        {
            return new MemberDocs(XElement.Parse(xml));
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    private sealed partial class MemberDocs(XElement member)
    {
        public string? Summary => Text(member.Element("summary"));

        public string? Returns => Text(member.Element("returns"));

        public string? Remarks => Text(member.Element("remarks"));

        /// <summary>The first &lt;example&gt;'s code, with the common indentation removed.</summary>
        public string? Example
        {
            get
            {
                var example = member.Element("example");
                var code = example?.Element("code")?.Value ?? example?.Element("c")?.Value;
                if (string.IsNullOrWhiteSpace(code))
                    return null;

                var lines = code.Replace("\r\n", "\n").Split('\n').SkipWhile(string.IsNullOrWhiteSpace).Reverse()
                    .SkipWhile(string.IsNullOrWhiteSpace).Reverse().ToList();
                int indent = lines.Where(l => l.Length > 0).Min(l => l.Length - l.TrimStart().Length);
                return string.Join("\n", lines.Select(l => l.Length >= indent ? l[indent..] : l));
            }
        }

        public string? Param(string name) =>
            Text(member.Elements("param").FirstOrDefault(p => (string?)p.Attribute("name") == name));

        private static string? Text(XElement? element)
        {
            if (element is null)
                return null;
            var text = new StringBuilder();
            foreach (var node in element.Nodes())
            {
                text.Append(node switch
                {
                    XText t => t.Value,
                    XElement { Name.LocalName: "see" or "seealso" } e =>
                        "`" + CrefName((string?)e.Attribute("cref") ?? (string?)e.Attribute("langword") ?? e.Value) + "`",
                    XElement { Name.LocalName: "paramref" or "typeparamref" } e => "`" + (string?)e.Attribute("name") + "`",
                    XElement { Name.LocalName: "c" } e => "`" + e.Value + "`",
                    XElement e => e.Value,
                    _ => "",
                });
            }
            return Whitespace().Replace(text.ToString(), " ").Trim();
        }

        private static string CrefName(string cref)
        {
            int paren = cref.IndexOf('(');
            if (paren >= 0) cref = cref[..paren];
            return cref[(cref.LastIndexOfAny([':', '.']) + 1)..];
        }

        [GeneratedRegex(@"\s+")]
        private static partial Regex Whitespace();
    }
}
