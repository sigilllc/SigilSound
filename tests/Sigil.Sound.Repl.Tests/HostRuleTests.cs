using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Sigil.Sound.Repl.Tests;

/// <summary>A host can add its own rule to a <see cref="SnippetProfile"/>; these pin down when it runs and what it can do.</summary>
public class HostRuleTests
{
    private static SnippetProfile NoLoops() => new(extraValidation: context =>
    {
        foreach (var loop in context.Root.DescendantNodes().OfType<StatementSyntax>()
                     .Where(s => s is ForStatementSyntax or WhileStatementSyntax or ForEachStatementSyntax or DoStatementSyntax))
        {
            if (context.IsUserText(loop))
                context.Report("HOST01", "Loops are not allowed here.", loop);
        }
    });

    [Fact]
    public void A_host_rule_can_reject_a_snippet_the_base_policy_accepts()
    {
        const string code = "var n = 0;\nfor (var i = 0; i < 3; i++) n += i;\nSine(440 + n)";

        Assert.True(SnippetCompiler.Compile(code).Success);

        var result = SnippetCompiler.Compile(code, NoLoops());
        Assert.False(result.Success);
        var error = Assert.Single(result.Diagnostics, d => d.Id == "HOST01");
        Assert.Equal(2, error.StartLine);
    }

    [Fact]
    public void A_host_rule_that_reports_nothing_lets_the_snippet_through()
    {
        var result = SnippetCompiler.Compile("Sine(440)", NoLoops());

        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
    }

    [Fact]
    public void A_host_rule_does_not_run_on_a_snippet_the_base_policy_already_rejected()
    {
        int runs = 0;
        var profile = new SnippetProfile(extraValidation: _ => runs++);

        var result = SnippetCompiler.Compile("System.IO.File.Exists(\"x\");\nSine(440)", profile);

        Assert.False(result.Success);
        Assert.Equal(0, runs);
    }

    [Fact]
    public void A_host_rule_that_throws_rejects_the_snippet_instead_of_passing_it()
    {
        var profile = new SnippetProfile(extraValidation: _ => throw new InvalidOperationException("boom"));

        var result = SnippetCompiler.Compile("Sine(440)", profile);

        Assert.False(result.Success);
        var error = Assert.Single(result.Diagnostics, d => d.Id == "SIGIL_SEC13");
        Assert.Contains("boom", error.Message);
    }

    [Fact]
    public void A_host_rule_sees_the_semantic_model_so_it_can_judge_members_not_just_names()
    {
        var profile = new SnippetProfile(extraValidation: context =>
        {
            foreach (var call in context.Root.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(context.IsUserText))
            {
                if (context.Model.GetSymbolInfo(call).Symbol is IMethodSymbol { Name: "Reverb" })
                    context.Report("HOST02", "Reverb is not allowed here.", call);
            }
        });

        Assert.True(SnippetCompiler.Compile("Sine(440).Delay(0.2)", profile).Success);
        Assert.Contains(SnippetCompiler.Compile("Sine(440).Reverb()", profile).Diagnostics, d => d.Id == "HOST02");
    }

    [Fact]
    public void A_host_rule_also_applies_while_typing()
    {
        var diagnostics = SnippetCompiler.Check("while (false) { }\nSine(440)", NoLoops());

        Assert.Contains(diagnostics, d => d.Id == "HOST01");
    }

    [Fact]
    public void The_default_profile_has_no_host_rule()
    {
        Assert.True(SnippetCompiler.Compile("for (var i = 0; i < 2; i++) { }\nSine(440)").Success);
    }
}
