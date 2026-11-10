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
        new("Notes out: a tune",
            """
            // Sigilgraph: Out.Notes sends notes out of the device's Note Out jack, to any instrument it is wired to. A pattern is the
            // simplest thing to send; here a melody with <a b> (one per cycle) and Every (reversed every fourth cycle).
            //
            // On the page there is no device to send to, so one voice plays them, and the Notes out strip beside the scope shows what
            // is sent: time runs right to left under the playhead, one row per semitone.
            Out.Notes(Mini.Notes("c3 [e3 g3]*2 ~ <b3 d4>").Every(4, p => p.Rev()));

            Silence
            """),
        new("Notes out: harmonizer",
            """
            // Sigilgraph: In.Notes is the notes arriving at the device's Note In, as a stream. Play the keyboard under the editor:
            // every key also sends the tone two steps above it in C major, and both come back twice, softer, an eighth later.
            // Out.Notes sends the result out of Note Out. Wire it to an instrument on the device; on this page one voice plays it.
            //
            // A script that only sends notes ends with Silence: it has no sound of its own.
            Out.Notes(
                In.Notes
                    .Harmonize(Scale.Parse("c major"), 0, 2)
                    .Echo(2, Beats.Eighth, 0.5));

            Silence
            """),
        new("Notes out: follow the chord",
            """
            // Sigilgraph: hold a chord on the keyboard (on the device, wire a Tonnetz or a keyboard to Note In) and a run falls through it.
            // The run is written once as steps of the chord: 0 is its lowest tone, 1 the next, past the top it climbs an octave,
            // -1 is the tone below the lowest. Sometimes and Degrade add the dice, the same way every time (the numbers are seeds).
            //
            // In.Notes.Chord() groups notes that start within 35 ms into one chord and, with Cycle, hands a new chord over only at the
            // bar line, so a run that starts on one chord finishes on it. Hold a chord for a bar before the run starts.
            var chord = In.Notes.Chord(switchOn: ChordSwitch.Cycle);

            var runs = Mini.Integers("<[7 6 5 4 3 2 1 0] [8 6 5 3 2 0]> ~ [4 3 2 1 0] ~")
                .Sometimes(0.25, p => p.Shift(0.0625), 9)
                .Degrade(0.12, 3);

            Out.Notes(
                chord.Steps(runs)
                    .Transpose(12)
                    .Sometimes(0.2, p => p.Transpose(12), 21)
                    .Velocity(0.7));

            Silence
            """),
        new("Notes out: lock to a scale",
            """
            // Sigilgraph: functions of your own shape the notes. In Graph mode a function may use only the note, numbers, comparisons
            // and Math, plus the note's own helpers; make anything else (a scale, a pattern) in a variable above it.
            //
            // Snap moves every key to the nearest tone of D dorian, Where keeps two notes in three (Chance picks the same ones every time),
            // and the kept notes also send a fifth above. Play the keyboard, including the black keys: they land on the scale.
            var dorian = Scale.Parse("d dorian");
            var locked = In.Notes.Select(n => n.Snap(dorian));
            var fifths = locked.Where(n => n.Chance(0.66, 5)).Transpose(7);

            Out.Notes(locked + fifths);

            Silence
            """),
    ];
}
