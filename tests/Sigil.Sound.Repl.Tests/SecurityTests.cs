using Xunit;

namespace Sigil.Sound.Repl.Tests;

public class SecurityTests
{
    [Theory]
    [InlineData("System.IO.File.ReadAllText(\"secrets.txt\");\nSine(440)")]
    [InlineData("System.IO.Directory.GetCurrentDirectory();\nSine(440)")]
    [InlineData("System.Diagnostics.Process.Start(\"calc.exe\");\nSine(440)")]
    [InlineData("System.Threading.Thread.Sleep(1000);\nSine(440)")]
    [InlineData("var client = new System.Net.Http.HttpClient();\nSine(440)")]
    [InlineData("System.Reflection.Assembly.GetExecutingAssembly();\nSine(440)")]
    [InlineData("var domain = System.AppDomain.CurrentDomain;\nSine(440)")]
    [InlineData("System.Environment.Exit(0);\nSine(440)")]
    [InlineData("System.GC.Collect();\nSine(440)")]
    [InlineData("System.Activator.CreateInstance(null!);\nSine(440)")]
    [InlineData("Sigil.Sound.Repl.ExecutionGuard.Reset();\nSine(440)")]
    public void Blocked_namespaces_and_types_are_rejected_at_compile_time(string maliciousCode)
    {
        var result = SnippetCompiler.Compile(maliciousCode);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.IsError && d.Id == "SIGIL_SEC08");
    }

    [Fact]
    public void Typeof_reflection_expression_is_rejected()
    {
        var result = SnippetCompiler.Compile("var t = typeof(Sound);\nSine(440)");

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.IsError && d.Id == "SIGIL_SEC03");
    }

    [Fact]
    public void GetType_reflection_call_is_rejected()
    {
        var result = SnippetCompiler.Compile("var s = Sine(440);\nvar t = s.GetType();\ns");

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.IsError && d.Id == "SIGIL_SEC07");
    }

    [Fact]
    public void RenderToWav_disk_io_is_rejected()
    {
        var result = SnippetCompiler.Compile("Sine(440).RenderToWav(\"evil.wav\", 1.0)");

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.IsError && d.Id == "SIGIL_SEC09");
    }

    [Fact]
    public void Direct_Render_call_inside_snippet_is_rejected()
    {
        var result = SnippetCompiler.Compile("Sine(440).Render(1.0)");

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.IsError && d.Id == "SIGIL_SEC09");
    }

    [Theory]
    [InlineData("unsafe { int* p = null; }\nSine(440)")]
    [InlineData("Span<byte> s = stackalloc byte[64];\nSine(440)")]
    [InlineData("[System.Serializable] Sound Foo() => Sine(440);\nFoo()")]
    public void Low_level_and_unsafe_constructs_are_rejected(string code)
    {
        var result = SnippetCompiler.Compile(code);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.IsError && d.Id.StartsWith("SIGIL_SEC"));
    }

    [Fact]
    public void Excessive_array_allocation_is_rejected()
    {
        var result = SnippetCompiler.Compile("var big = new int[50_000_000];\nSine(440)");

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.IsError && d.Id == "SIGIL_SEC10");
    }

    [Fact]
    public void Escaping_snippet_wrapper_via_closing_braces_is_rejected()
    {
        var code = """
            Sine(440);
            }
            }
            namespace Malicious
            {
                public class Escape { }
            }
            """;

        var result = SnippetCompiler.Compile(code);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.IsError && d.Id == "SIGIL_SEC01");
    }

    [Fact]
    public void While_infinite_loop_trips_execution_limit_guard()
    {
        var result = SnippetCompiler.Compile("while (true) { }\nSine(440)");

        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        var ex = Assert.Throws<ExecutionLimitException>(() => result.Run());
        Assert.Contains("Loop execution step limit exceeded", ex.Message);
    }

    [Fact]
    public void For_infinite_loop_trips_execution_limit_guard()
    {
        var result = SnippetCompiler.Compile("for (long i = 0; i < long.MaxValue; i++) { }\nSine(440)");

        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        var ex = Assert.Throws<ExecutionLimitException>(() => result.Run());
        Assert.Contains("Loop execution step limit exceeded", ex.Message);
    }

    [Fact]
    public void Do_while_infinite_loop_trips_execution_limit_guard()
    {
        var result = SnippetCompiler.Compile("do { } while (true);\nSine(440)");

        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        var ex = Assert.Throws<ExecutionLimitException>(() => result.Run());
        Assert.Contains("Loop execution step limit exceeded", ex.Message);
    }

    [Fact]
    public void Infinite_recursion_trips_depth_guard_without_stack_overflow()
    {
        var result = SnippetCompiler.Compile("Sound Recurse() => Recurse();\nRecurse()");

        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        var ex = Assert.Throws<ExecutionLimitException>(() => result.Run());
        Assert.Contains("Recursion depth limit exceeded", ex.Message);
    }

    [Fact]
    public void Custom_operator_with_infinite_loop_trips_guard_during_audio_rendering()
    {
        var result = SnippetCompiler.Compile("""
            Sine(440).Through(input => Operator.From(block =>
            {
                while (true) { }
            }))
            """);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        var sound = result.Run();

        // Rendering the block trips the execution guard
        var ex = Assert.Throws<ExecutionLimitException>(() => sound.Render(0.01));
        Assert.Contains("Loop execution step limit exceeded", ex.Message);
    }

    [Fact]
    public void Security_diagnostics_surface_during_Check_while_typing()
    {
        var diagnostics = SnippetCompiler.Check("System.IO.File.ReadAllText(\"test.txt\");\nSine(440)");

        var error = Assert.Single(diagnostics, d => d.IsError && d.Id == "SIGIL_SEC08" && d.StartColumn == 11);
        Assert.Equal(1, error.StartLine);
        Assert.Equal(11, error.StartColumn);
        Assert.Contains("System.IO.File", error.Message);
    }

    [Fact]
    public void Blocked_namespaces_do_not_appear_in_editor_completions()
    {
        var completions = EditorServices.Complete("System.IO.", 10);

        Assert.DoesNotContain(completions, c => c.Label == "File");
        Assert.DoesNotContain(completions, c => c.Label == "Directory");
    }
}
