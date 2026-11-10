using Xunit;

namespace Sigil.Sound.Repl.Tests;

/// <summary>
/// Scripts written to get past the validator without writing the name of a blocked type: through an exception, a delegate,
/// the undocumented <c>__makeref</c> keywords, an alias, an attribute, a struct layout or a language feature that hides the call.
/// Each must be refused by a security rule (a <c>SIGIL_SEC</c> id), not by a typo, so a regression in a rule shows up here.
/// </summary>
public class SandboxProbeTests
{
    private const string Tail = "\nSine(440)";

    [Theory]
    // A blocked type reached without naming it.
    [InlineData("var m = new Exception().TargetSite;")]
    [InlineData("try { throw new Exception(); } catch (Exception e) { var t = e.TargetSite.DeclaringType.Assembly; }")]
    [InlineData("int x = 1; var tr = __makeref(x); var t = __reftype(tr);")]
    [InlineData("int x = 1; var tr = __makeref(x); int y = __refvalue(tr, int);")]
    [InlineData("Func<int> f = () => 1; var m = f.Method;")]
    [InlineData("Func<int> f = () => 1; var t = f.Target;")]
    [InlineData("Func<int> f = () => 1; f.DynamicInvoke();")]
    [InlineData("var n = nameof(System.IO.File);")]
    [InlineData("object o = 1; var t = o?.GetType();")]
    [InlineData("Func<object, object> g = o => o.GetType();")]
    [InlineData("object o = 1; if (o.GetType() is var t) { }")]
    [InlineData("var t = Enum.GetUnderlyingType(DayOfWeek.Monday.GetType());")]
    [InlineData("var l = new List<Type>();")]
    [InlineData("var h = typeof(int).TypeHandle;")]
    // Aliases and usings.
    [InlineData("using E = System.Environment;\nE.Exit(0);")]
    [InlineData("using static System.IO.File;\nReadAllText(\"x\");")]
    [InlineData("using System.IO;\nFile.ReadAllText(\"x\");")]
    [InlineData("global::System.IO.File.Delete(\"x\");")]
    [InlineData("var f = System.IO.File.ReadAllText;")]
    // Native code, memory layout and unsafe.
    [InlineData("[System.Runtime.InteropServices.DllImport(\"kernel32.dll\")] static extern int GetTickCount();\nGetTickCount();")]
    [InlineData("using System.Runtime.InteropServices;\n[DllImport(\"kernel32.dll\")] static extern int GetTickCount();\nGetTickCount();")]
    [InlineData("S s = default; s.L = 1; var o = s.O;\n[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)] struct S { [System.Runtime.InteropServices.FieldOffset(0)] public object O; [System.Runtime.InteropServices.FieldOffset(0)] public long L; }")]
    [InlineData("unsafe { int x = 1; int* p = &x; *p = 2; }")]
    [InlineData("unsafe { delegate*<int> f = null; }")]
    [InlineData("Span<int> s = stackalloc int[4]; s[0] = 1;")]
    // Declaring something inside a protected namespace.
    [InlineData("[assembly: System.Runtime.CompilerServices.IgnoresAccessChecksTo(\"System.Private.CoreLib\")]\nnamespace System.Runtime.CompilerServices { class IgnoresAccessChecksToAttribute : Attribute { public IgnoresAccessChecksToAttribute(string n) { } } }")]
    [InlineData("namespace System.IO { class Evil { } }")]
    // Compiler directives.
    [InlineData("#r \"System.IO.FileSystem\"\nSine(440)")]
    [InlineData("#load \"x.csx\"\nSine(440)")]
    // Members of allowed types that reach the host, and language features that hide a call.
    [InlineData("var s = Sine(440); var r = s.RenderToWav;")]
    [InlineData("var b = AudioBuffer.FromWav(\"x.wav\");")]
    [InlineData("class K { ~K() { } }")]
    [InlineData("async System.Threading.Tasks.Task M() { await System.Threading.Tasks.Task.Delay(1); }")]
    [InlineData("var t = System.Threading.Tasks.Task.Run(() => 1);")]
    public void A_script_that_avoids_naming_a_blocked_type_is_still_refused(string code)
    {
        var result = SnippetCompiler.Compile(code.EndsWith("Sine(440)") ? code : code + Tail);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.IsError && d.Id.StartsWith("SIGIL_SEC", StringComparison.Ordinal));
    }

    // These two are closed by what the compiler can see, not by a rule of ours: Microsoft.CSharp is not referenced, and Interop
    // types are internal. If either changes (a reference added, a type made public) these fail and the gap needs a rule.
    [Theory]
    [InlineData("dynamic d = \"x\"; var n = d.Length;")]
    [InlineData("dynamic d = 1; var t = d.GetType();")]
    [InlineData("var x = Interop.Sys.GetPid();")]
    public void A_script_that_only_the_compilers_own_limits_stop_is_refused(string code)
    {
        var result = SnippetCompiler.Compile(code + Tail);

        Assert.False(result.Success);
    }

    [Fact]
    public void Ordinary_scripts_that_look_close_to_the_forbidden_ones_still_compile()
    {
        Assert.True(SnippetCompiler.Compile("var s = $\"{1 + 1}\";" + Tail).Success);
        Assert.True(SnippetCompiler.Compile("var n = Enumerable.Range(0, 4).Select(i => i * 2).Sum();" + Tail).Success);
        Assert.True(SnippetCompiler.Compile("lock (new object()) { }" + Tail).Success);
        Assert.True(SnippetCompiler.Compile("var c = DayOfWeek.Monday.GetTypeCode();" + Tail).Success);
    }
}
