namespace Sigil.Sound.Repl;

/// <summary>
/// The examples menu: the library's starter snippets plus the ones that need the page itself, such as the keyboard and the
/// samples. Kept here because the library's examples compile under its default policy, where <c>Keys</c> and <c>Samples</c>
/// do not exist.
/// </summary>
internal static class ReplExamples
{
    // Next to the Sampler that builds its audio in code, so the two read as a pair: the same sound, computed and loaded.
    private static readonly Examples.Example SamplerFromFile = new("Sampler (file)",
        """
        // Samples.Load gives the audio of a .wav file. "pluck.wav" comes with the page; drop your own .wav files on
        // this page and they join the Samples list, and the editor suggests their names when you type the quotes.
        //
        // The audio is the same kind of thing as the Sampler example builds in code: frames held in memory.
        // Sample(pluck, frequency, root) plays it at a pitch. The root is the pitch it was recorded at (C3 = 130.81 Hz).
        const double root = 130.81;
        var pluck = Samples.Load("pluck.wav");

        var tune = Notes.Parse("C3 E3 G3 B3 C4 B3 G3 E3")
            .Every(Beats.Eighth, gate: 2)
            .Loop();

        // Each note plays the sample at its own speed. A one-shot rings out once per note.
        tune.Play(v => Sample(pluck, v.Frequency, root) * v.Velocity, polyphony: 8) * 0.5
        """);

    private static readonly Examples.Example Keyboard = new("Keyboard",
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
        """);

    public static IReadOnlyList<Examples.Example> All { get; } = Build();

    private static List<Examples.Example> Build()
    {
        var all = Examples.All.ToList();
        int sampler = all.FindIndex(e => e.Name == "Sampler");
        all.Insert(sampler < 0 ? all.Count : sampler + 1, SamplerFromFile);
        all.Add(Keyboard);
        return all;
    }
}
