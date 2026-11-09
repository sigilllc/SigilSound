using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Sigil.Sound;

namespace Sigil.Sound.Repl;

/// <summary>
/// The assemblies a snippet compiles against, read straight from the running process.
/// In the browser there is no file system to load reference assemblies from, but the runtime
/// still holds each loaded assembly's metadata, which is all Roslyn needs.
/// </summary>
internal static class SnippetReferences
{
    private static readonly string[] FrameworkAssemblies =
    [
        "System.Private.CoreLib",
        "System.Runtime",
        "System.Collections",
        "System.Linq",
    ];

    /// <summary>The references of <see cref="SnippetProfile.Default"/>.</summary>
    public static IReadOnlyList<MetadataReference> All => SnippetProfile.Default.References;

    /// <summary>The framework, the Sigil assemblies and the snippet guards, plus whatever a host profile adds.</summary>
    internal static IReadOnlyList<MetadataReference> Create(IEnumerable<Assembly> extraAssemblies)
    {
        var references = new List<MetadataReference>();
        foreach (var name in FrameworkAssemblies)
            references.Add(FromLoaded(Assembly.Load(name)));

        references.Add(FromLoaded(typeof(Sound).Assembly));
        references.Add(FromLoaded(typeof(Note).Assembly));
        references.Add(FromLoaded(typeof(ExecutionGuard).Assembly));
        foreach (var assembly in extraAssemblies)
            references.Add(FromLoaded(assembly));
        return references;
    }

    private static unsafe MetadataReference FromLoaded(Assembly assembly)
    {
        if (!assembly.TryGetRawMetadata(out byte* blob, out int length))
            throw new InvalidOperationException($"The runtime does not expose metadata for {assembly.GetName().Name}.");

        var module = ModuleMetadata.CreateFromMetadata((IntPtr)blob, length);
        var name = assembly.GetName().Name!;
        return AssemblyMetadata.Create(module).GetReference(XmlDocumentation.For(assembly), display: name);
    }
}

/// <summary>
/// XML documentation for assemblies: embedded in the assembly itself, embedded in Sigil.Sound.Repl.Core at build time,
/// or located alongside the assembly DLL on disk.
/// </summary>
internal sealed class XmlDocumentation : DocumentationProvider
{
    private readonly Dictionary<string, string> _members;

    private XmlDocumentation(Dictionary<string, string> members) => _members = members;

    public static DocumentationProvider? For(Assembly assembly)
    {
        var name = assembly.GetName().Name!;

        // 1. Embedded resource in the assembly itself
        Stream? stream = assembly.GetManifestResourceStream($"{name}.xml");
        if (stream is null)
        {
            var match = assembly.GetManifestResourceNames()
                .FirstOrDefault(r => r.EndsWith($"{name}.xml", StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                stream = assembly.GetManifestResourceStream(match);
        }

        // 2. Embedded in Sigil.Sound.Repl.Core (built-in Sigil docs for browser REPL)
        stream ??= typeof(XmlDocumentation).Assembly.GetManifestResourceStream($"{name}.xml");

        // 3. Search disk paths (handles desktop Godot where in-memory ALC leaves assembly.Location empty)
        if (stream is null)
        {
            var candidates = new List<string>();

            if (!string.IsNullOrEmpty(assembly.Location))
                candidates.Add(Path.ChangeExtension(assembly.Location, ".xml"));

            string? baseDir = AppContext.BaseDirectory;
            if (!string.IsNullOrEmpty(baseDir))
            {
                candidates.Add(Path.Combine(baseDir, $"{name}.xml"));
                candidates.Add(Path.Combine(baseDir, ".godot", "mono", "temp", "bin", "Debug", $"{name}.xml"));
                candidates.Add(Path.Combine(baseDir, ".godot", "mono", "temp", "bin", "Release", $"{name}.xml"));
            }

            string? domainDir = AppDomain.CurrentDomain.BaseDirectory;
            if (!string.IsNullOrEmpty(domainDir) && !string.Equals(domainDir, baseDir, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(Path.Combine(domainDir, $"{name}.xml"));
                candidates.Add(Path.Combine(domainDir, ".godot", "mono", "temp", "bin", "Debug", $"{name}.xml"));
                candidates.Add(Path.Combine(domainDir, ".godot", "mono", "temp", "bin", "Release", $"{name}.xml"));
            }

            foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (!string.IsNullOrEmpty(loaded.Location))
                    {
                        string dir = Path.GetDirectoryName(loaded.Location)!;
                        if (!string.IsNullOrEmpty(dir))
                            candidates.Add(Path.Combine(dir, $"{name}.xml"));
                    }
                }
                catch
                {
                    // Ignore reflection errors on dynamic assemblies
                }
            }

            foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (File.Exists(candidate))
                {
                    try
                    {
                        stream = File.OpenRead(candidate);
                        break;
                    }
                    catch
                    {
                        // Fall through on access errors
                    }
                }
            }
        }

        return stream is null ? null : FromStream(stream);
    }

    public static DocumentationProvider? For(string assemblyName)
    {
        var stream = typeof(XmlDocumentation).Assembly.GetManifestResourceStream($"{assemblyName}.xml");
        return stream is null ? null : FromStream(stream);
    }

    private static DocumentationProvider FromStream(Stream stream)
    {
        using (stream)
        {
            var members = XDocument.Load(stream).Descendants("member")
                .Where(m => m.Attribute("name") is not null)
                .GroupBy(m => m.Attribute("name")!.Value)
                .ToDictionary(g => g.Key, g => g.First().ToString());
            return new XmlDocumentation(members);
        }
    }

    protected override string? GetDocumentationForSymbol(string documentationMemberID, System.Globalization.CultureInfo preferredCulture,
        CancellationToken cancellationToken = default) =>
        _members.GetValueOrDefault(documentationMemberID);

    public override bool Equals(object? obj) => ReferenceEquals(this, obj);

    public override int GetHashCode() => _members.GetHashCode();
}
