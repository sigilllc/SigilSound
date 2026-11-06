using ReplHost;
using SigilgraphHost;

namespace Sigil.Sound.Repl;

/// <summary>
/// The REPL page's snippet policy in Sigilgraph mode: the plain page's (<see cref="ReplProfile"/>) plus the device API of
/// <see cref="SigilgraphApi"/>, so a snippet written for the Sound Script device compiles here. Plain mode does not get these
/// names, so a game developer's completion never offers <c>Knob</c>.
/// </summary>
internal static class SigilgraphProfile
{
    public static SnippetProfile Instance { get; } = new(
        extraUsings: ["using static ReplHost.ReplApi;", "using SigilgraphHost;", "using static SigilgraphHost.SigilgraphApi;"],
        extraAssemblies: [typeof(ReplApi).Assembly],
        extraAllowedNamespaces: ["ReplHost", "SigilgraphHost"],
        samples: ReplSamples.Instance.Source);
}
