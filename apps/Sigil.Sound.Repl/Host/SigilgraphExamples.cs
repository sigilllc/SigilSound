namespace Sigil.Sound.Repl;

/// <summary>
/// Starter snippets for the Sigilgraph mode. They use the device API (<c>Knob</c>, <c>Slider</c>, <c>In</c>, <c>Poly</c>) and so
/// run in the REPL's Sigilgraph mode and in the Workbench's Sound Script device, not in a plain Sigil.Sound project.
/// </summary>
/// <remarks>
/// Each one is compiled and rendered by the tests in this repository and, through the real device engine, by the Workbench's
/// own, so an example cannot rot unnoticed on either side.
/// </remarks>
internal static class SigilgraphExamples
{
    public static IReadOnlyList<Examples.Example> All { get; } =
    [
        new("Knob and slider filter",
            """
            // Sigilgraph: the knob and slider appear under the editor, left to right, as on the Sound Script device.
            // In.Audio is the test source chosen on the page (a saw; try noise). Move the controls while it plays:
            // they glide and never rebuild the sound, and they keep their values when you edit the code.
            //
            // Curve.Log gives a knob equal steps per octave, which is how a cutoff is heard.
            var cutoff = Knob("Cutoff", 1200, 40, 18000, Curve.Log);
            var resonance = Knob("Resonance", 0.7, 0.5, 12);
            var level = Slider("Level", 0.5, 0, 1);

            In.Audio.LowPass(cutoff, q: resonance) * level
            """),
        new("Tremolo",
            """
            // Sigilgraph: a knob for how fast, one for how deep. The rate is a log knob, so it is easy to set both
            // a slow swell (0.3 Hz) and a fast flutter (15 Hz).
            var rate = Knob("Rate", 4, 0.1, 20, Curve.Log);
            var depth = Knob("Depth", 0.6, 0, 1);

            // The wave swings between -1 and 0, so the gain swings between 1 and 1 - depth.
            var gain = 1 + depth * Sine(rate).Range(-1, 0);

            (In.Audio * gain)   // parentheses: a bare "In.Audio * gain" line reads to C# as a pointer declaration
            """),
        new("Keyboard",
            """
            // Sigilgraph: Poly is the device's note input, so this script copies straight into a Sound Script device.
            // Each note that arrives starts a voice built from this lambda; it sustains while the key is down and its
            // Adsr releases when the key is let go. Here the notes come from the keyboard under the editor
            // (click the keys, or click it and use A S D F G H J K, W E T Y U, and Z X for the octave).
            // On the device they are the notes wired to its Note In.
            //
            // Try it: hold a chord, change the filter or the envelope and press Ctrl+Enter.
            // The keys you hold carry over to the new sound.
            var synth = Poly(8, v =>
                (Saw(v.Frequency) + Saw(v.Frequency * 1.006) * 0.8)   // two slightly detuned saws
                    .LowPass(1800, q: 1.5)
                    .Adsr(attack: 0.01, decay: 0.2, sustain: 0.6, release: 0.5));

            (synth * 0.3).Reverb(decay: 2, mix: 0.2)
            """),
        new("Keyboard with a cutoff knob",
            """
            // Sigilgraph: Poly plays the keyboard under the editor, as the device plays the notes wired to its Note In.
            // The knob and slider sit above the keys. Declare controls outside the voice, then use them inside it,
            // so every voice shares the one knob.
            var cutoff = Knob("Cutoff", 2000, 100, 12000, Curve.Log);
            var level = Slider("Level", 0.3, 0, 1);

            Poly(8, v => Saw(v.Frequency)
                .LowPass(cutoff, q: 1.5)
                .Adsr(attack: 0.01, decay: 0.2, sustain: 0.6, release: 0.4)) * level
            """),
    ];
}
