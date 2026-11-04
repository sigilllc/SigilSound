using ReplHost;

namespace Sigil.Sound.Repl;

/// <summary>
/// The REPL page's snippet policy: the library's default plus <see cref="ReplApi"/>. Every compile, check and editor
/// request on the page passes this, so what you can type and what completion offers stay in step.
/// </summary>
internal static class ReplProfile
{
    public static SnippetProfile Instance { get; } = new(
        extraUsings: ["using static ReplHost.ReplApi;"],
        extraAssemblies: [typeof(ReplApi).Assembly],
        extraAllowedNamespaces: ["ReplHost"],
        samples: ReplSamples.Instance.Source);
}
