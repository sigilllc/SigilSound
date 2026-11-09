using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Sigil.Sound.Repl;

/// <summary>
/// Enforces semantic and syntactic sandboxing on user snippets compiled in the REPL.
/// All restrictions are strictly confined to the REPL layer.
/// </summary>
public static class SnippetSecurityValidator
{
    /// <summary>
    /// Validates the document's syntax tree and semantic model against its profile's policy.
    /// Returns any security violations as <see cref="SnippetDiagnostic"/> items.
    /// </summary>
    internal static IReadOnlyList<SnippetDiagnostic> Validate(CSharpCompilation compilation, SnippetDocument document)
    {
        var diagnostics = new List<SnippetDiagnostic>();
        var root = document.Tree.GetRoot();
        var model = compilation.GetSemanticModel(document.Tree);

        // 1. Directives first: a user-written #line renames the file the diagnostics and checks below are keyed on.
        ValidateNoDirectives(root, document, diagnostics);
        if (diagnostics.Count > 0)
            return diagnostics;

        // 2. Structural check: ensure no code escaped the snippet wrapper
        ValidateStructure(root, diagnostics);
        if (diagnostics.Count > 0)
            return diagnostics;

        // 3. Syntax and semantic check
        var walker = new SecurityWalker(model, document, diagnostics);
        walker.Visit(root);

        // 4. The host's own rule, only on a snippet the built-in checks accepted.
        if (diagnostics.Count == 0 && document.Profile.ExtraValidation is { } hostRule)
        {
            try
            {
                hostRule(new SnippetValidationContext(model, document, diagnostics));
            }
            catch (Exception e)
            {
                // A rule that cannot run is not a rule that passed.
                diagnostics.Add(CreateDiagnostic("SIGIL_SEC13", $"The host's snippet rule failed: {e.Message}", root.GetLocation()));
            }
        }

        // Deduplicate diagnostics by position and ID
        return diagnostics
            .GroupBy(d => (d.Id, d.StartLine, d.StartColumn, d.Message))
            .Select(g => g.First())
            .OrderBy(d => d.StartLine)
            .ThenBy(d => d.StartColumn)
            .ToList();
    }

    /// <summary>
    /// Rejects every preprocessor directive the user typed. <c>#line</c> in particular moves the compiler's idea of
    /// where code lives, which would let it hide from the checks that follow; <c>#if</c>, <c>#pragma</c> and the rest
    /// have no use in a one-expression snippet. Directives inside string literals are not trivia and are unaffected.
    /// </summary>
    private static void ValidateNoDirectives(SyntaxNode root, SnippetDocument document, List<SnippetDiagnostic> diagnostics)
    {
        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: false))
        {
            if (!trivia.IsDirective || !document.IsUserText(trivia.Span))
                continue;

            var (line, column) = document.LocateInUserText(trivia.SpanStart);
            int length = trivia.ToString().TrimEnd().Length;
            diagnostics.Add(new SnippetDiagnostic(
                "SIGIL_SEC12", IsError: true, "Preprocessor directives are not permitted in the REPL.",
                line, column, line, column + length));
        }
    }

    private static void ValidateStructure(SyntaxNode root, List<SnippetDiagnostic> diagnostics)
    {
        var classDecl = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => c.Identifier.Text == SnippetDocument.ClassName);

        if (classDecl is null)
        {
            diagnostics.Add(CreateDiagnostic("SIGIL_SEC01", "Snippet structure is invalid.", root.GetLocation()));
            return;
        }

        // Verify there are no other top-level types outside __Snippet
        var topLevelTypes = root.DescendantNodes()
            .OfType<BaseTypeDeclarationSyntax>()
            .Where(t => t.Parent is CompilationUnitSyntax or NamespaceDeclarationSyntax or FileScopedNamespaceDeclarationSyntax);

        foreach (var type in topLevelTypes)
        {
            if (type != classDecl)
            {
                diagnostics.Add(CreateDiagnostic(
                    "SIGIL_SEC01",
                    "Declaring types or namespaces outside the snippet body is not permitted.",
                    type.GetLocation()));
            }
        }
    }

    internal static SnippetDiagnostic CreateDiagnostic(string id, string message, Location location)
    {
        var span = location.GetMappedLineSpan();
        if (!location.IsInSource || !span.HasMappedPath || span.Path != SnippetDocument.FileName)
        {
            return new SnippetDiagnostic(id, IsError: true, message, 1, 1, 1, 1);
        }

        return new SnippetDiagnostic(
            id,
            IsError: true,
            message,
            span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1,
            span.EndLinePosition.Line + 1,
            span.EndLinePosition.Character + 1);
    }

    private sealed class SecurityWalker(SemanticModel model, SnippetDocument document, List<SnippetDiagnostic> diagnostics)
        : CSharpSyntaxWalker(SyntaxWalkerDepth.Node)
    {
        private readonly SnippetProfile profile = document.Profile;

        public override void VisitUnsafeStatement(UnsafeStatementSyntax node)
        {
            diagnostics.Add(CreateDiagnostic("SIGIL_SEC02", "'unsafe' code is not permitted in the REPL.", node.GetLocation()));
            base.VisitUnsafeStatement(node);
        }

        public override void VisitFixedStatement(FixedStatementSyntax node)
        {
            diagnostics.Add(CreateDiagnostic("SIGIL_SEC02", "'fixed' statements are not permitted in the REPL.", node.GetLocation()));
            base.VisitFixedStatement(node);
        }

        public override void VisitPointerType(PointerTypeSyntax node)
        {
            diagnostics.Add(CreateDiagnostic("SIGIL_SEC02", "Pointer types are not permitted in the REPL.", node.GetLocation()));
            base.VisitPointerType(node);
        }

        public override void VisitStackAllocArrayCreationExpression(StackAllocArrayCreationExpressionSyntax node)
        {
            diagnostics.Add(CreateDiagnostic("SIGIL_SEC11", "'stackalloc' is not permitted in the REPL.", node.GetLocation()));
            base.VisitStackAllocArrayCreationExpression(node);
        }

        public override void VisitTypeOfExpression(TypeOfExpressionSyntax node)
        {
            diagnostics.Add(CreateDiagnostic("SIGIL_SEC03", "'typeof' expressions are not permitted in the REPL.", node.GetLocation()));
            base.VisitTypeOfExpression(node);
        }

        public override void VisitAttributeList(AttributeListSyntax node)
        {
            diagnostics.Add(CreateDiagnostic("SIGIL_SEC04", "Attributes are not permitted in the REPL.", node.GetLocation()));
            base.VisitAttributeList(node);
        }

        public override void VisitDestructorDeclaration(DestructorDeclarationSyntax node)
        {
            diagnostics.Add(CreateDiagnostic("SIGIL_SEC06", "Destructors are not permitted in the REPL.", node.GetLocation()));
            base.VisitDestructorDeclaration(node);
        }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            if (node.Modifiers.Any(SyntaxKind.ExternKeyword))
                diagnostics.Add(CreateDiagnostic("SIGIL_SEC05", "'extern' methods are not permitted in the REPL.", node.GetLocation()));
            base.VisitMethodDeclaration(node);
        }

        public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            if (node.Modifiers.Any(SyntaxKind.ExternKeyword))
                diagnostics.Add(CreateDiagnostic("SIGIL_SEC05", "'extern' methods are not permitted in the REPL.", node.GetLocation()));
            base.VisitLocalFunctionStatement(node);
        }

        public override void VisitArrayCreationExpression(ArrayCreationExpressionSyntax node)
        {
            foreach (var rank in node.Type.RankSpecifiers)
            {
                foreach (var size in rank.Sizes)
                {
                    if (size is LiteralExpressionSyntax literal && literal.Token.Value is int intVal && intVal > 1_000_000)
                    {
                        diagnostics.Add(CreateDiagnostic(
                            "SIGIL_SEC10",
                            $"Array allocation of {intVal:N0} elements exceeds the maximum allowed size (1,000,000).",
                            size.GetLocation()));
                    }
                }
            }
            base.VisitArrayCreationExpression(node);
        }

        /// <summary>
        /// The rules for the few members that are never allowed, whatever the namespace and type rules say. They are decided on the
        /// symbol, so they hold wherever the member is named: a call, and also a method group turned into a delegate
        /// (<c>Func&lt;string, AudioBuffer&gt; load = AudioBuffer.FromWav;</c> or <c>var g = s.GetType;</c>), which never contains the call.
        /// </summary>
        private static (string Id, string Message)? Restriction(IMethodSymbol method)
        {
            if (method.Name == "GetType" &&
                (method.ContainingType?.SpecialType == SpecialType.System_Object ||
                 method.ReturnType?.ToDisplayString() == "System.Type"))
            {
                return ("SIGIL_SEC07", "'GetType' is not permitted in the REPL.");
            }

            if (method.Name == "RenderToWav" && method.ContainingType?.ToDisplayString() == "Sigil.Sound.Sound")
                return ("SIGIL_SEC09", "'RenderToWav' is not permitted in the REPL.");

            // The path overload reads a file from the host. Overloads taking a stream or bytes stay allowed.
            if (method.Name == "FromWav" && method.ContainingType?.ToDisplayString() == "Sigil.Sound.AudioBuffer" &&
                method.Parameters.Length > 0 && method.Parameters[0].Type.SpecialType == SpecialType.System_String)
            {
                return ("SIGIL_SEC09", "Loading a WAV file from disk with 'AudioBuffer.FromWav(path)' is not permitted in the REPL.");
            }

            if (method.Name == "Render" && method.ContainingType?.ToDisplayString() == "Sigil.Sound.Sound")
                return ("SIGIL_SEC09", "'Sound.Render' inside snippets is not permitted; snippets must return a Sound to play in real time.");

            return null;
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var symbolInfo = model.GetSymbolInfo(node);
            var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
            if (symbol is IMethodSymbol method && Restriction(method) is { } restriction)
            {
                diagnostics.Add(CreateDiagnostic(restriction.Id, restriction.Message, node.GetLocation()));
                return;
            }

            base.VisitInvocationExpression(node);
        }

        // A restricted method named without being called: it can be turned into a delegate and called later. A call that hit a rule has
        // already been reported and does not visit its children, so a name is reported once.
        private void CheckRestrictedMethodGroup(SimpleNameSyntax node)
        {
            if (!document.IsUserText(node.Span))
                return;

            var info = model.GetSymbolInfo(node);
            var candidates = info.Symbol is { } resolved ? [resolved] : info.CandidateSymbols;
            foreach (var candidate in candidates)
            {
                if (candidate is IMethodSymbol method && Restriction(method) is { } restriction)
                {
                    diagnostics.Add(CreateDiagnostic(restriction.Id, restriction.Message, node.GetLocation()));
                    return;
                }
            }
        }

        public override void VisitIdentifierName(IdentifierNameSyntax node)
        {
            CheckRestrictedMethodGroup(node);
            CheckSymbol(node);
            base.VisitIdentifierName(node);
        }

        public override void VisitGenericName(GenericNameSyntax node)
        {
            CheckSymbol(node);
            base.VisitGenericName(node);
        }

        private void CheckSymbol(ExpressionSyntax node)
        {
            // Skip the generated wrapper. Decided by position in the wrapped file, not by the mapped path,
            // so nothing in the user's text can move itself out of the checked region.
            if (!document.IsUserText(node.Span))
                return;

            var symbolInfo = model.GetSymbolInfo(node);
            var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
            if (symbol is null)
                return;

            // Safe symbol kinds
            if (symbol is ILocalSymbol or IParameterSymbol or IRangeVariableSymbol or ITypeParameterSymbol or IDiscardSymbol or ILabelSymbol)
                return;

            // Check namespace
            var ns = symbol.ContainingNamespace;
            if (symbol is INamespaceSymbol nsSymbol)
                ns = nsSymbol;

            if (ns is not null && !ns.IsGlobalNamespace)
            {
                string nsName = ns.ToDisplayString();

                if (profile.IsBlockedNamespace(nsName))
                {
                    diagnostics.Add(CreateDiagnostic(
                        "SIGIL_SEC08",
                        $"Access to '{symbol.ToDisplayString()}' in namespace '{nsName}' is restricted in the REPL.",
                        node.GetLocation()));
                    return;
                }

                if (!profile.IsAllowedNamespace(nsName))
                {
                    diagnostics.Add(CreateDiagnostic(
                        "SIGIL_SEC08",
                        $"Access to namespace '{nsName}' is restricted in the REPL.",
                        node.GetLocation()));
                    return;
                }
            }

            // Check forbidden types in System
            var type = symbol as ITypeSymbol ?? symbol.ContainingType;
            if (type is not null)
            {
                string typeNs = type.ContainingNamespace?.ToDisplayString() ?? "";
                if (typeNs == "System" && profile.IsForbiddenSystemType(type.Name))
                {
                    diagnostics.Add(CreateDiagnostic(
                        "SIGIL_SEC08",
                        $"Type '{type.ToDisplayString()}' is restricted in the REPL.",
                        node.GetLocation()));
                    return;
                }
            }
        }
    }
}

