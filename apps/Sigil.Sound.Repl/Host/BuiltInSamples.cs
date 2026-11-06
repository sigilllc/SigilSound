using System.Reflection;

namespace Sigil.Sound.Repl;

/// <summary>
/// The samples that ship with the page, so an example that loads a file works before anything has been dropped. They are
/// .wav files in <c>Host/BuiltIn</c>, embedded in the assembly under the logical name <c>BuiltIn/&lt;file&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>pluck.wav</c>: a plucked C3 (130.81 Hz), mono, 16-bit, 32 kHz, 1.2 s. Computed rather than recorded: eight harmonics
/// whose upper partials die away faster, plus a few milliseconds of noise for the pick, so it has no licence to carry.
/// </para>
/// <para>
/// <c>IR_Speakers_Living_Room.wav</c>: a recorded living room for the "Room from an IR" example. Creative Commons 0 (public domain),
/// by mansardian, https://freesound.org/people/mansardian/sounds/172677/.
/// </para>
/// </remarks>
internal static class BuiltInSamples
{
    private const string Prefix = "BuiltIn/";

    /// <summary>The built-in samples by name (such as <c>pluck.wav</c>), as the bytes of each file.</summary>
    public static IReadOnlyDictionary<string, byte[]> FromAssembly()
    {
        var assembly = typeof(BuiltInSamples).Assembly;
        var samples = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(Prefix, StringComparison.Ordinal) || !resource.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                continue;

            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            samples[resource[Prefix.Length..]] = copy.ToArray();
        }
        return samples;
    }
}
