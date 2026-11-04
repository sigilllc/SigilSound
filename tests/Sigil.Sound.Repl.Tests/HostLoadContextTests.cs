using System.Reflection;
using System.Runtime.Loader;

namespace Sigil.Sound.Repl.Tests;

/// <summary>
/// A host that loads the library into a context of its own (Godot does this for a game's assemblies). A snippet compiled there has to bind to
/// that host's copy of the library, not go looking in the default context.
/// </summary>
public class HostLoadContextTests
{
    // Loads the library's assemblies from this test's folder into its own context, as a host that isolates its plugins would.
    private sealed class HostContext() : AssemblyLoadContext("test host", isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName name)
        {
            string path = Path.Combine(AppContext.BaseDirectory, name.Name + ".dll");
            bool ours = name.Name is not null && (name.Name.StartsWith("Sigil.Sound", StringComparison.Ordinal) || name.Name.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal));
            return ours && File.Exists(path) ? LoadFromAssemblyPath(path) : null;
        }
    }

    [Fact]
    public void A_snippet_compiled_in_a_hosts_own_load_context_runs_against_the_hosts_copy_of_the_library()
    {
        var host = new HostContext();
        var core = host.LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory, "Sigil.Sound.Repl.Core.dll"));
        Assert.NotSame(typeof(SnippetCompiler).Assembly, core); // really a second copy, in another context

        var compiler = core.GetType("Sigil.Sound.Repl.SnippetCompiler")!;
        var compile = compiler.GetMethod("Compile", [typeof(string), core.GetType("Sigil.Sound.Repl.SnippetProfile")!])!;
        object result = compile.Invoke(null, ["Sine(440).LowPass(800)", null])!;
        bool success = (bool)result.GetType().GetProperty("Success")!.GetValue(result)!;
        Assert.True(success);

        // Before the fix this threw FileNotFoundException (the snippet looked for Sigil.Sound in the default context) or, with a copy there, InvalidCastException.
        object sound = result.GetType().GetMethod("Run")!.Invoke(result, null)!;

        Assert.Equal("Sine(440).LowPass(800, 0.7071)", sound.ToString());
        Assert.Same(host, AssemblyLoadContext.GetLoadContext(sound.GetType().Assembly));
    }
}
