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

    // A drum groove in a recorded room: what Convolve is for, so this loads a real room rather than synthesising a tail. Placed after the Cabinet (IR) example.
    private static readonly Examples.Example RoomFromIr = new("Room from an IR",
        """
        // A drum groove played through a recorded room: IR_Speakers_Living_Room.wav comes with the page
        // (a living room, CC0, by mansardian on Freesound). Convolve puts the sound in that room, so every hit
        // comes back with the room's reflections, and the dry hits alone would sound flat and close by comparison.
        // Drop your own impulse responses on the page and load them the same way.
        var bpm = 110;
        var room = Samples.Load("IR_Speakers_Living_Room.wav");

        var kick = Mini.Drums("bd ~ ~ bd ~ bd ~ ~");
        var snare = Mini.Drums("~ sn ~ sn");
        var hats = Mini.Drums("hh*8").Degrade(0.2, seed: 3);

        var drums =
            kick.Play(v => Sine(Fn(t => 48 + 110 * Exp(-t * 28))).Adsr(0.001, 0.25, 0, 0.02), bpm) * 0.9
            + snare.Play(v => Noise().BandPass(1800, q: 1.2).Adsr(0.001, 0.12, 0, 0.05), bpm) * 0.5
            + hats.Play(v => Noise(4).HighPass(7000).Adsr(0.001, 0.04, 0, 0.01), bpm) * 0.25;

        drums.Convolve(room, mix: 0.5) * 0.7
        """);

    private static readonly Examples.Example Keyboard =new("Live notes (Keys)",
        """
        // For a Sigil.Sound project: Keys is a NoteInput, the library's live notes, and it exists on this page only.
        // To write the same thing for the Sigilgraph Sound Script device, use Poly instead (see the Sigilgraph "Keyboard" example).
        //
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

    private static readonly Examples.Example FallingNotes = new("Falling notes (Keys)",
        """
        // Play this with the keyboard under the editor (click the keys, or click it and use A S D F G H J K).
        //
        // Fall is a note modifier: a small function that takes a signal and changes it over the note's own life.
        // Inside a voice, Fn(t => ...) starts its clock again for every note, so t is the age of that note: each key
        // you press drops by an octave over the first second, whatever the other keys are doing.
        Sound Fall(Sound pitch, double octaves, double seconds) =>
            pitch * Fn(t => Pow(2, -octaves * Min(t / seconds, 1.0)));

        var synth = Keys.Play(v =>
            (Saw(Fall(v.Frequency, octaves: 1, seconds: 1)) * 0.8)
                .LowPass(2200, q: 1.5)
                .Adsr(attack: 0.005, decay: 0.2, sustain: 0.6, release: 0.4),
            polyphony: 8);

        (synth * 0.3).Reverb(decay: 1.5, mix: 0.15)
        """);

    public static IReadOnlyList<Examples.Example> All { get; } = Build();

    private static List<Examples.Example> Build()
    {
        var all = Examples.All.ToList();
        int sampler = all.FindIndex(e => e.Name == "Sampler");
        all.Insert(sampler < 0 ? all.Count : sampler + 1, SamplerFromFile);
        int cabinet = all.FindIndex(e => e.Name == "Cabinet (IR)");
        all.Insert(cabinet < 0 ? all.Count : cabinet + 1, RoomFromIr);
        all.Add(Keyboard);
        all.Add(FallingNotes);
        return all;
    }
}
