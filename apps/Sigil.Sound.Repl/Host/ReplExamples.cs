namespace Sigil.Sound.Repl;

/// <summary>
/// The examples menu: the library's starter snippets plus the ones that need the page itself, such as the keyboard.
/// Kept here because the library's examples compile under its default policy, where <c>Keys</c> does not exist.
/// </summary>
internal static class ReplExamples
{
    public static IReadOnlyList<Examples.Example> All { get; } =
    [
        .. Examples.All,
        new("Keyboard",
            """
            // Play this with the keyboard under the editor: click the keys, or click the keyboard and use
            // A S D F G H J K (white keys) and W E T Y U (black keys). Z and X change the octave.
            //
            // Keys is a NoteInput: live notes, as opposed to the written-out patterns of the Arpeggiator.
            // Play(...) turns it into sound: each key starts a voice built from this lambda, the voice
            // sustains while the key is down, and its Adsr releases when you let go.
            //
            // Try it: hold a chord, change the filter or the envelope and press Ctrl+Enter.
            // The keys you hold carry over to the new sound.

            var synth = Keys.Play(v =>
                (Saw(v.Frequency) + Saw(v.Frequency * 1.006) * 0.8)   // two slightly detuned saws
                    .LowPass(1800, q: 1.5)
                    .Adsr(attack: 0.01, decay: 0.2, sustain: 0.6, release: 0.5),
                polyphony: 8);

            (synth * 0.3).Reverb(decay: 2, mix: 0.2)
            """),
    ];
}
