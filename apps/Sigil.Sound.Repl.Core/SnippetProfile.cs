using System.Reflection;
using Microsoft.CodeAnalysis;

namespace Sigil.Sound.Repl;

/// <summary>
/// What a host lets snippets see: the usings every snippet starts with, the assemblies it compiles against and
/// which namespaces and types it may touch. <see cref="Default"/> is the browser REPL's policy; another host
/// (a game, an app with its own API) builds a profile that adds to it.
/// </summary>
/// <remarks>
/// Profiles only widen what a snippet can <i>see</i> (<c>extraUsings</c>, <c>extraAssemblies</c>,
/// <c>extraAllowedNamespaces</c>) and only narrow what it may <i>use</i> (<c>extraBlockedNamespaces</c>,
/// <c>extraForbiddenSystemTypes</c>). Nothing a profile adds can unblock a namespace the base policy blocks,
/// so a host cannot reopen file, network, reflection or process access by accident.
/// One profile instance caches its metadata references and compilation, so create it once and reuse it.
/// </remarks>
public sealed class SnippetProfile
{
    private static readonly string[] BaseUsings =
    [
        "using System;",
        "using System.Collections.Generic;",
        "using System.Linq;",
        "using Sigil.Sound;",
        "using static System.Math;",
        "using static Sigil.Sound.Sounds;",
    ];

    private static readonly string[] BaseBlockedNamespaces =
    [
        "System.IO",
        "System.Reflection",
        "System.Runtime",
        "System.Diagnostics",
        "System.Threading",
        "System.Net",
        "System.Security",
        "System.Management",
        "Microsoft.",
        "Sigil.Sound.Repl",
    ];

    private static readonly string[] BaseForbiddenSystemTypes =
    [
        "Type", "AppDomain", "Environment", "Activator", "GC", "Console", "AppContext", "OperatingSystem",
        "MarshalByRefObject", "TypedReference", "ArgIterator", "RuntimeTypeHandle", "RuntimeFieldHandle",
        "RuntimeMethodHandle", "Guid", "Uri",
        // A delegate's Method and Target, DynamicInvoke and CreateDelegate are reflection by another name (Func and Action are unaffected).
        "Delegate", "MulticastDelegate",
    ];

    private readonly string[] _blocked;
    private readonly string[] _allowed;
    private readonly HashSet<string> _forbiddenSystemTypes;
    private readonly Lazy<IReadOnlyList<MetadataReference>> _references;
    private readonly Action<SnippetValidationContext>? _extraValidation;
    private readonly ISampleSource? _samples;

    /// <summary>The policy the browser REPL runs with.</summary>
    public static SnippetProfile Default { get; } = new();

    /// <param name="extraUsings">Complete directives added to every snippet, such as <c>using static My.Api;</c>.</param>
    /// <param name="extraAssemblies">Assemblies snippets compile against besides the Sigil ones; they must be loaded already.</param>
    /// <param name="extraAllowedNamespaces">Namespaces (and everything under them) snippets may use, such as the host's API.</param>
    /// <param name="extraBlockedNamespaces">Namespace prefixes snippets may not use, in addition to the base policy.</param>
    /// <param name="extraForbiddenSystemTypes">Names of types in <c>System</c> snippets may not use, in addition to the base policy.</param>
    /// <param name="extraValidation">
    /// The host's own rule, run on every snippet after the built-in checks passed. It reports problems through the context it is given,
    /// and can only add errors. Use it for rules about syntax or members that a list of namespaces cannot express. If it throws, the
    /// snippet is rejected.
    /// </param>
    /// <param name="samples">
    /// The sample library the host gives its snippets (the same source behind the <see cref="SampleLibrary"/> the host exposes).
    /// The editor reads its <see cref="ISampleSource.Names"/> to offer file names while a <c>[SampleName]</c> argument is typed.
    /// It is read on every request, so names added later show up without creating a new profile.
    /// </param>
    public SnippetProfile(
        IEnumerable<string>? extraUsings = null,
        IEnumerable<Assembly>? extraAssemblies = null,
        IEnumerable<string>? extraAllowedNamespaces = null,
        IEnumerable<string>? extraBlockedNamespaces = null,
        IEnumerable<string>? extraForbiddenSystemTypes = null,
        Action<SnippetValidationContext>? extraValidation = null,
        ISampleSource? samples = null)
    {
        _extraValidation = extraValidation;
        _samples = samples;
        Usings = [.. BaseUsings, .. extraUsings ?? []];
        _blocked = [.. BaseBlockedNamespaces, .. extraBlockedNamespaces ?? []];
        _allowed = [.. extraAllowedNamespaces ?? []];
        _forbiddenSystemTypes = new HashSet<string>(BaseForbiddenSystemTypes.Concat(extraForbiddenSystemTypes ?? []), StringComparer.Ordinal);

        var assemblies = extraAssemblies?.ToArray() ?? [];
        _references = new Lazy<IReadOnlyList<MetadataReference>>(() => SnippetReferences.Create(assemblies));
    }

    /// <summary>The directives placed above every snippet.</summary>
    public IReadOnlyList<string> Usings { get; }

    internal IReadOnlyList<MetadataReference> References => _references.Value;

    internal Action<SnippetValidationContext>? ExtraValidation => _extraValidation;

    /// <summary>The sample names the host offers its snippets right now; empty when the host has no sample library.</summary>
    public IReadOnlyList<string> SampleNames => _samples?.Names ?? [];

    /// <summary>True when snippets may not touch anything in <paramref name="ns"/>, however it is reached.</summary>
    public bool IsBlockedNamespace(string ns) => _blocked.Any(prefix => ns.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>True when <paramref name="ns"/> is one snippets are meant to use. Blocked namespaces are never allowed.</summary>
    public bool IsAllowedNamespace(string ns)
    {
        if (IsBlockedNamespace(ns))
            return false;

        return ns == "System"
               || ns == "Sigil.Sound" || ns.StartsWith("Sigil.Sound.", StringComparison.Ordinal)
               || ns == "System.Collections" || ns.StartsWith("System.Collections.", StringComparison.Ordinal)
               || ns == "System.Linq" || ns.StartsWith("System.Linq.", StringComparison.Ordinal)
               || _allowed.Any(a => ns == a || ns.StartsWith(a + ".", StringComparison.Ordinal));
    }

    /// <summary>True for a type in <c>System</c> that is never available to snippets, such as <c>Environment</c>.</summary>
    public bool IsForbiddenSystemType(string name) => _forbiddenSystemTypes.Contains(name);
}
