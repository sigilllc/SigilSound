using System.Reflection;
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

    internal static CSharpCompilation CreateCompilation(SnippetDocument document, string assemblyName = "snippet") =>
        CSharpCompilation.Create(
            assemblyName,
            [document.Tree],
            SnippetReferences.All,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                // The browser runtime has one thread; Roslyn must not try to spread work across more.
                concurrentBuild: false,
                nullableContextOptions: NullableContextOptions.Disable));

    /// <summary>Loads the references up front, so the first real compile doesn't pay for it.</summary>
    public static void Warm() => _ = Compile("Sine(440)");

    public static CompileResult Compile(string code)
    {
        var document = SnippetDocument.Create(code);
        var compilation = CreateCompilation(document, $"snippet{Interlocked.Increment(ref _count)}");

        using var image = new MemoryStream();
        var emit = compilation.Emit(image);
        var diagnostics = Map(emit.Diagnostics);
        if (!emit.Success)
            return new CompileResult(null, diagnostics);

        var assembly = Assembly.Load(image.ToArray());
        var entryPoint = assembly.GetType(SnippetDocument.ClassName)!.GetMethod(SnippetDocument.MethodName)!;
        return new CompileResult(entryPoint, diagnostics);
    }

    /// <summary>Errors and warnings in the user's text, without emitting; cheap enough to run while typing.</summary>
    public static IReadOnlyList<SnippetDiagnostic> Check(string code) =>
        Map(CreateCompilation(SnippetDocument.Create(code)).GetDiagnostics());

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
