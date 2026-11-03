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

    private static readonly Lazy<IReadOnlyList<MetadataReference>> Cached = new(Create);

    public static IReadOnlyList<MetadataReference> All => Cached.Value;

    private static IReadOnlyList<MetadataReference> Create()
    {
        var references = new List<MetadataReference>();
        foreach (var name in FrameworkAssemblies)
            references.Add(FromLoaded(Assembly.Load(name)));

        references.Add(FromLoaded(typeof(Sound).Assembly));
        references.Add(FromLoaded(typeof(Note).Assembly));
        return references;
    }

    private static unsafe MetadataReference FromLoaded(Assembly assembly)
    {
        if (!assembly.TryGetRawMetadata(out byte* blob, out int length))
            throw new InvalidOperationException($"The runtime does not expose metadata for {assembly.GetName().Name}.");

        var module = ModuleMetadata.CreateFromMetadata((IntPtr)blob, length);
        var name = assembly.GetName().Name!;
        return AssemblyMetadata.Create(module).GetReference(XmlDocumentation.For(name), display: name);
    }
}

/// <summary>
/// XML documentation for the Sigil assemblies, embedded at build time so completion can show it in the browser.
/// </summary>
internal sealed class XmlDocumentation : DocumentationProvider
{
    private readonly Dictionary<string, string> _members;

    private XmlDocumentation(Dictionary<string, string> members) => _members = members;

    public static DocumentationProvider? For(string assemblyName)
    {
        using var stream = typeof(XmlDocumentation).Assembly.GetManifestResourceStream($"{assemblyName}.xml");
        if (stream is null)
            return null;

        var members = XDocument.Load(stream).Descendants("member")
            .Where(m => m.Attribute("name") is not null)
            .GroupBy(m => m.Attribute("name")!.Value)
            .ToDictionary(g => g.Key, g => g.First().ToString());
        return new XmlDocumentation(members);
    }

    protected override string? GetDocumentationForSymbol(string documentationMemberID, System.Globalization.CultureInfo preferredCulture,
        CancellationToken cancellationToken = default) =>
        _members.GetValueOrDefault(documentationMemberID);

    public override bool Equals(object? obj) => ReferenceEquals(this, obj);

    public override int GetHashCode() => _members.GetHashCode();
}
