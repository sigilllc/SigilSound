using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Sigil.Sound.Repl;

/// <summary>A compiler message mapped to the user's text. Lines and columns are 1-based; line 0 means "no position".</summary>
public sealed record SnippetDiagnostic(
    string Id, bool IsError, string Message, int StartLine, int StartColumn, int EndLine, int EndColumn);

/// <summary>The outcome of compiling a snippet: either something to run, or the errors that stopped it.</summary>
public sealed class CompileResult
{
    private readonly MethodInfo? _entryPoint;

    internal CompileResult(MethodInfo? entryPoint, IReadOnlyList<SnippetDiagnostic> diagnostics)
    {
        _entryPoint = entryPoint;
        Diagnostics = diagnostics;
    }

    public bool Success => _entryPoint is not null;

    public IReadOnlyList<SnippetDiagnostic> Diagnostics { get; }

    /// <summary>
    /// Runs the snippet and returns the sound it describes. Exceptions thrown by the snippet
    /// (for example an argument out of range) surface as themselves, not wrapped in reflection exceptions.
    /// </summary>
    public Sound Run()
    {
        if (_entryPoint is null)
            throw new InvalidOperationException("The snippet did not compile.");
        ExecutionGuard.Reset();
        try
        {
            return (Sound?)_entryPoint.Invoke(null, null)
                   ?? throw new InvalidOperationException("The snippet returned null instead of a sound.");
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(e.InnerException);
            throw;
        }
    }
}

/// <summary>Compiles snippets to in-memory assemblies with Roslyn, entirely in-process.</summary>
public static class SnippetCompiler
{
    private static int _count;

    private static readonly CSharpCompilationOptions Options = new(
        OutputKind.DynamicallyLinkedLibrary,
        optimizationLevel: OptimizationLevel.Release,
        // The browser runtime has one thread; Roslyn must not try to spread work across more.
        concurrentBuild: false,
        nullableContextOptions: NullableContextOptions.Disable);

    // The references are bound once per profile. Editor requests (completion, hover, checking while typing) add their
    // snippet to this compilation instead of building one from scratch, which keeps every keystroke from re-reading
    // all the metadata.
    private static readonly ConditionalWeakTable<SnippetProfile, Lazy<CSharpCompilation>> Shared = new();

    private static CSharpCompilation SharedFor(SnippetProfile profile) =>
        Shared.GetValue(profile, static p => new Lazy<CSharpCompilation>(() =>
            CSharpCompilation.Create("snippet", references: p.References, options: Options))).Value;

    /// <summary>
    /// A compilation of <paramref name="document"/>. Without an assembly name it reuses the shared reference binding,
    /// which is what editor services want; a name gives a standalone compilation to emit.
    /// </summary>
    internal static CSharpCompilation CreateCompilation(SnippetDocument document, string? assemblyName = null) =>
        assemblyName is null
            ? SharedFor(document.Profile).AddSyntaxTrees(document.Tree)
            : CSharpCompilation.Create(assemblyName, [document.Tree], document.Profile.References, Options);

    // Every run of a snippet produces an assembly. In a collectible context they can be freed once the sound that
    // uses one has been replaced; a long live-coding session would otherwise pile them up for good.
    private static Assembly Load(byte[] image)
    {
        try
        {
            var host = AssemblyLoadContext.GetLoadContext(typeof(SnippetCompiler).Assembly) ?? AssemblyLoadContext.Default;
            return new SnippetLoadContext(host).LoadFromStream(new MemoryStream(image));
        }
        catch (Exception e) when (e is PlatformNotSupportedException or NotSupportedException)
        {
            return Assembly.Load(image);
        }
    }
    /// <summary>
    /// Where a snippet is loaded. It finds the assemblies it was compiled against (Sigil.Sound and whatever the host added) in the context
    /// that loaded this compiler, and only then falls back to the default one.
    /// </summary>
    /// <remarks>
    /// A host that keeps its assemblies in a context of its own, as Godot does for a game, would otherwise have the snippet look for them in
    /// the default context: not finding them, or worse, finding a second copy whose types are not the host's.
    /// </remarks>
    private sealed class SnippetLoadContext(AssemblyLoadContext host) : AssemblyLoadContext(null, isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            foreach (var loaded in host.Assemblies)
            {
                if (AssemblyName.ReferenceMatchesDefinition(assemblyName, loaded.GetName()))
                    return loaded;
            }

            return null;
        }
    }

    // Touches every operator, lambdas, arrays, LINQ and the custom-operator escape hatch, so the runtime has seen them.
    private const string WarmSnippet =
        """
        var pitch = Fn(t => 110 * Pow(2, new[] { 0, 3, 7 }.Select(n => n / 12.0).ToArray()[(int)(t / 0.2) % 3]));
        var arp = Notes.Parse("C3 E3 G3").Every(Beats.Sixteenth, gate: 2).Loop().Play(v => Sine(v.Frequency).Adsr(0.01, 0.1, 0.5, 0.1), bpm: 240);
        (Saw(pitch) + Square(220, 0.5) + Noise() * 0.1 + arp)
            .LowPass(900, q: 1).HighPass(80).Adsr(0.01, 0.1, 0.5, 0.1).Gain(0.5)
            .Delay(0.3).Reverb().Chorus().Phaser().Comb(220).BitCrush(6, 8000)
            .Distort(3, DistortionMode.Fold).Limit().Pan(0.1)
            .Through(input => Operator.From(block => input.RenderBlock(block)))
        """;

    /// <summary>
    /// Compiles and briefly renders a snippet that touches the whole API, so the first real compile and the first
    /// run don't pay for loading references and for first-time execution of the operators.
    /// </summary>
    public static void Warm() => Compile(WarmSnippet).Run().Render(0.05);

    /// <param name="code">What the user typed.</param>
    /// <param name="profile">The host's policy; <see cref="SnippetProfile.Default"/> when omitted.</param>
    public static CompileResult Compile(string code, SnippetProfile? profile = null)
    {
        var document = SnippetDocument.Create(code, profile);
        var compilation = CreateCompilation(document, $"snippet{Interlocked.Increment(ref _count)}");

        // 1. Validate AST and symbols against the REPL security sandbox allowlist
        var securityDiagnostics = SnippetSecurityValidator.Validate(compilation, document);
        if (securityDiagnostics.Any(d => d.IsError))
            return new CompileResult(null, securityDiagnostics);

        // 2. Rewrite AST to inject loop step counters and recursion depth guards
        var guardedTree = LoopGuardRewriter.Apply(document.Tree, compilation.GetSemanticModel(document.Tree));
        var guardedCompilation = CSharpCompilation.Create(
            compilation.AssemblyName!,
            [guardedTree],
            document.Profile.References,
            Options);

        using var image = new MemoryStream();
        var emit = guardedCompilation.Emit(image);
        var diagnostics = Map(emit.Diagnostics);
        if (!emit.Success)
            return new CompileResult(null, diagnostics);

        var assembly = Load(image.ToArray());
        var entryPoint = assembly.GetType(SnippetDocument.ClassName)!.GetMethod(SnippetDocument.MethodName)!;
        return new CompileResult(entryPoint, diagnostics);
    }

    /// <summary>Errors and warnings in the user's text, without emitting; cheap enough to run while typing.</summary>
    public static IReadOnlyList<SnippetDiagnostic> Check(string code, SnippetProfile? profile = null)
    {
        var document = SnippetDocument.Create(code, profile);
        var compilation = CreateCompilation(document);
        var securityDiagnostics = SnippetSecurityValidator.Validate(compilation, document);
        var compilerDiagnostics = Map(compilation.GetDiagnostics());

        return securityDiagnostics
            .Concat(compilerDiagnostics)
            .OrderBy(d => !d.IsError)
            .ThenBy(d => d.StartLine)
            .ThenBy(d => d.StartColumn)
            .ToList();
    }

    internal static IReadOnlyList<SnippetDiagnostic> Map(IEnumerable<Diagnostic> diagnostics) =>
        diagnostics
            .Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Select(MapOne)
            .OrderBy(d => !d.IsError)
            .ThenBy(d => d.StartLine)
            .ThenBy(d => d.StartColumn)
            .ToList();

    private static SnippetDiagnostic MapOne(Diagnostic diagnostic)
    {
        bool isError = diagnostic.Severity == DiagnosticSeverity.Error;
        string message = diagnostic.Id switch
        {
            // Raised on the hidden wrapper method when nothing is returned.
            "CS0161" => "End the snippet with an expression that makes a Sound, without a semicolon, for example: Sine(440)",
            _ => diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
        };

        var span = diagnostic.Location.GetMappedLineSpan();
        if (!diagnostic.Location.IsInSource || !span.HasMappedPath || span.Path != SnippetDocument.FileName)
            return new SnippetDiagnostic(diagnostic.Id, isError, message, 0, 0, 0, 0);

        return new SnippetDiagnostic(diagnostic.Id, isError, message,
            span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1,
            span.EndLinePosition.Line + 1, span.EndLinePosition.Character + 1);
    }
}
