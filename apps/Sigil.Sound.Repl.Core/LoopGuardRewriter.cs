using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Sigil.Sound.Repl;

/// <summary>
/// Rewrites the Roslyn AST of a user snippet before compilation to inject:
/// 1. Loop step counters (<see cref="ExecutionGuard.CountStep"/>) into all while, for, foreach, and do loops.
/// 2. Recursion depth guards (<see cref="ExecutionGuard.EnterCall"/> and <see cref="ExecutionGuard.ExitCall"/>) into
///    user-defined methods, local functions, lambdas and anonymous methods.
/// </summary>
/// <remarks>
/// Lambdas matter because they are the one way a snippet can recurse without declaring a function: a delegate that
/// calls itself overflows the stack, and .NET cannot catch that, so it ends the whole process. Rewriting an
/// expression-bodied lambda into a block turns on whether its delegate returns a value, which only the semantic
/// model of the unrewritten tree knows; that is why <see cref="Apply"/> takes one.
/// </remarks>
internal sealed class LoopGuardRewriter(SemanticModel? model) : CSharpSyntaxRewriter
{
    private static readonly StatementSyntax StepStatement =
        SyntaxFactory.ParseStatement("global::Sigil.Sound.Repl.ExecutionGuard.CountStep();");

    private static readonly StatementSyntax EnterStatement =
        SyntaxFactory.ParseStatement("global::Sigil.Sound.Repl.ExecutionGuard.EnterCall();");

    private static readonly StatementSyntax ExitStatement =
        SyntaxFactory.ParseStatement("global::Sigil.Sound.Repl.ExecutionGuard.ExitCall();");

    /// <param name="tree">The wrapped snippet to guard.</param>
    /// <param name="model">The semantic model of <paramref name="tree"/> as it is now, used to type the lambdas.</param>
    public static SyntaxTree Apply(SyntaxTree tree, SemanticModel? model = null)
    {
        var rewriter = new LoopGuardRewriter(model);
        var root = tree.GetRoot();
        var rewritten = rewriter.Visit(root);
        return tree.WithRootAndOptions(rewritten, tree.Options);
    }

    public override SyntaxNode? VisitWhileStatement(WhileStatementSyntax node)
    {
        var updated = (WhileStatementSyntax)base.VisitWhileStatement(node)!;
        return updated.WithStatement(WrapLoopBody(updated.Statement));
    }

    public override SyntaxNode? VisitForStatement(ForStatementSyntax node)
    {
        var updated = (ForStatementSyntax)base.VisitForStatement(node)!;
        return updated.WithStatement(WrapLoopBody(updated.Statement));
    }

    public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node)
    {
        var updated = (ForEachStatementSyntax)base.VisitForEachStatement(node)!;
        return updated.WithStatement(WrapLoopBody(updated.Statement));
    }

    public override SyntaxNode? VisitDoStatement(DoStatementSyntax node)
    {
        var updated = (DoStatementSyntax)base.VisitDoStatement(node)!;
        return updated.WithStatement(WrapLoopBody(updated.Statement));
    }

    public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
    {
        var updated = (LocalFunctionStatementSyntax)base.VisitLocalFunctionStatement(node)!;
        if (updated.Body is null && updated.ExpressionBody is null)
            return updated;

        return updated
            .WithExpressionBody(null)
            .WithSemicolonToken(default)
            .WithBody(GuardedBody(updated.Body, updated.ExpressionBody?.Expression, IsVoid(updated.ReturnType)));
    }

    public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        // Never rewrite the wrapper entry point method (__Run).
        if (node.Identifier.Text == SnippetDocument.MethodName)
            return base.VisitMethodDeclaration(node);

        var updated = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node)!;
        if (updated.Body is null && updated.ExpressionBody is null)
            return updated;

        return updated
            .WithExpressionBody(null)
            .WithSemicolonToken(default)
            .WithBody(GuardedBody(updated.Body, updated.ExpressionBody?.Expression, IsVoid(updated.ReturnType)));
    }

    public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
    {
        bool returnsNothing = ReturnsNothing(node);
        var updated = (SimpleLambdaExpressionSyntax)base.VisitSimpleLambdaExpression(node)!;
        return updated
            .WithExpressionBody(null)
            .WithBlock(GuardedBody(updated.Block, updated.ExpressionBody, returnsNothing));
    }

    public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
    {
        bool returnsNothing = ReturnsNothing(node);
        var updated = (ParenthesizedLambdaExpressionSyntax)base.VisitParenthesizedLambdaExpression(node)!;
        return updated
            .WithExpressionBody(null)
            .WithBlock(GuardedBody(updated.Block, updated.ExpressionBody, returnsNothing));
    }

    public override SyntaxNode? VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
    {
        var updated = (AnonymousMethodExpressionSyntax)base.VisitAnonymousMethodExpression(node)!;
        return updated.WithBlock(GuardedBody(updated.Block, null, returnsNothing: false));
    }

    private static StatementSyntax WrapLoopBody(StatementSyntax statement)
    {
        if (statement is BlockSyntax block)
        {
            return block.WithStatements(block.Statements.Insert(0, StepStatement));
        }

        if (statement is EmptyStatementSyntax)
        {
            return SyntaxFactory.Block(StepStatement);
        }

        return SyntaxFactory.Block(StepStatement, statement);
    }

    /// <summary>
    /// <c>{ EnterCall(); try { body } finally { ExitCall(); } }</c>, where an expression body becomes either a
    /// statement (a delegate that returns nothing) or a <c>return</c>.
    /// </summary>
    private static BlockSyntax GuardedBody(BlockSyntax? body, ExpressionSyntax? expression, bool returnsNothing)
    {
        StatementSyntax? single = body is not null ? null : expression switch
        {
            // "x => throw ..." has a throw expression for a body, which is not legal after "return".
            ThrowExpressionSyntax thrown => SyntaxFactory.ThrowStatement(thrown.Expression),
            _ when returnsNothing => SyntaxFactory.ExpressionStatement(expression!),
            _ => SyntaxFactory.ReturnStatement(expression!),
        };
        BlockSyntax inner = body ?? SyntaxFactory.Block(single!);

        var tryFinally = SyntaxFactory.TryStatement(
            inner,
            default,
            SyntaxFactory.FinallyClause(SyntaxFactory.Block(ExitStatement)));

        return SyntaxFactory.Block(EnterStatement, tryFinally);
    }

    private static bool IsVoid(TypeSyntax returnType) =>
        returnType is PredefinedTypeSyntax p && p.Keyword.IsKind(SyntaxKind.VoidKeyword);

    /// <summary>Whether the delegate this lambda was bound to returns nothing, from the unrewritten tree's model.</summary>
    private bool ReturnsNothing(SyntaxNode lambda)
    {
        if (model?.GetSymbolInfo(lambda).Symbol is not IMethodSymbol method)
            return false;

        // An async lambda that returns Task or ValueTask (not Task<T>) returns nothing to its body either.
        return method.ReturnsVoid || method is { IsAsync: true, ReturnType: INamedTypeSymbol { Arity: 0 } };
    }
}
