namespace Sigil.Sound.Repl;

/// <summary>Starter snippets. Each one only uses what the library has today.</summary>
public static class Examples
{
    public sealed record Example(string Name, string Code);

    public static IReadOnlyList<Example> All { get; } =
    [
        new("Filter sweep",
            """
            // Press Ctrl+Enter to play. Edit anything and press it again:
            // the new sound crossfades in while the old one plays.
            var sweep = Sine(0.25).Range(200, 2000);   // a sound used as a parameter

            Saw(110)
                .LowPass(cutoff: sweep, q: 4)
                * 0.4
            """),
        new("Arpeggiator",
            """
            // Sequences: Notes.Parse turns note names into notes, Every places them in time,
            // Loop repeats the pattern, and Play turns it into a polyphonic instrument.
            // Notes that overlap sound together, so this is polyphonic.

            var arp = Notes.Parse("C3 E3 G3 B3 C4 B3 G3 E3")
                .Every(Beats.Sixteenth, gate: 3)      // a note every sixteenth of a beat, each held for 3 steps
                .Loop();

            var bass = Notes.Parse("C2 G1 A1 F1")
                .Every(Beats.Whole)                   // one bass note per bar
                .Loop();

            // The lambda describes ONE note: v.Frequency is its pitch, and Adsr releases when the
            // note ends. Play also scales each voice by the note's velocity.
            Sound Pluck(Voice v) =>
                Saw(v.Frequency)
                    .LowPass(2400, q: 2)
                    .Adsr(attack: 0.003, decay: 0.15, sustain: 0.3, release: 0.4);

            (arp.Play(Pluck, bpm: 110, polyphony: 8) * 0.25
                + bass.Play(v => Sine(v.Frequency).Adsr(0.01, 0.2, 0.8, 0.5), bpm: 110) * 0.5)
                .Delay(time: 0.41, feedback: 0.35, mix: 0.25)   // a dotted eighth at 110 bpm
                .Reverb(decay: 2.5, mix: 0.2)
                .Limit()
            """),
        new("Pitch from a function",
            """
            // This is not a sequencer: it is ONE oscillator whose pitch and volume are functions
            // of time t (in seconds). Stepping the pitch every 0.25 s sounds like a sequence of notes.
            // For real notes, see the Arpeggiator example.

            var pitches = Notes.Parse("C3 E3 G3 B3 C4 B3 G3 E3")
                .Select(n => n.Frequency)
                .ToArray();
            var step = 0.125;                                      // seconds per note

            var pitch = Fn(t => pitches[(int)(t / step) % pitches.Length]);   // which note are we in? look up its pitch
            var pluck = Fn(t => Pow(1 - t % step / step, 3));      // t % step is the time inside the note: volume 1 -> 0

            Saw(pitch).LowPass(Sine(0.1).Range(500, 4000), q: 3) * pluck * 0.35
            """),
        new("Pulse width",
            """
            var width = Sine(0.3).Range(0.1, 0.9);

            (Square(55, width) + Square(110.4, width) * 0.5)
                .LowPass(1400, q: 1.5)
                * 0.3
            """),
        new("Ring modulation",
            """
            // Multiplying two sounds is ring modulation.
            var wobble = Sine(Fn(t => 2 + Sin(t * 0.7)));

            Sine(330) * Sine(Sine(0.2).Range(80, 120)) * wobble.Range(0.1, 0.5)
            """),
        new("Wind",
            """
            var gusts = Sine(0.07).Range(250, 1400);

            Noise().LowPass(gusts, q: 6) * Sine(0.13).Range(0.15, 0.5)
            """),
        new("Plucked string",
            """
            // A comb filter tuned to a pitch rings like a string when you feed it a short burst.
            var burst = Noise() * Fn(t => t % 1.5 < 0.01 ? 1 : 0);   // a click every 1.5 s

            burst
                .Comb(frequency: 110, feedback: 0.997, damping: 0.25)
                .Reverb(size: 1.5, decay: 3, mix: 0.25)
                * 0.5
            """),
        new("Echo and reverb",
            """
            // One oscillator whose pitch and volume are functions of time t (seconds):
            // the sound steps through four pitches, one every 0.25 s, and each one fades out
            // like a pluck. It sounds like several notes, but it is a single voice.

            var step = 0.25;                                // seconds per note

            var pitch = Fn(t =>
            {
                int n = (int)(t / step) % 4;                // which of the four notes are we in?
                return 220 * Pow(2, n * 0.75);              // each one 0.75 of an octave above the last
            });
            var fade = Fn(t => Pow(1 - t % step / step, 2));   // t % step = time inside the note; volume 1 -> 0

            var pluck = Saw(pitch).LowPass(1800) * fade;

            // The echoes and the room are what make it sound big:
            pluck
                .Delay(time: 0.375, feedback: 0.5, mix: 0.4)     // repeats every 0.375 s, each half as loud
                .Reverb(decay: 3, mix: 0.3)                      // a 3 second tail
                * 0.4
            """),
        new("Chorus pad",
            """
            var chord = Saw(110) + Saw(164.8) + Saw(220) + Saw(277.2);

            chord
                .LowPass(Sine(0.07).Range(600, 2200))
                .Chorus(rate: 0.6, depth: 0.7, voices: 5)
                .Reverb(decay: 4, mix: 0.3)
                * 0.18
            """),
        new("Phaser sweep",
            """
            Saw(82.4)
                .Distort(drive: 3)
                .Phaser(rate: 0.15, feedback: 0.7, stages: 6, minFrequency: 300, maxFrequency: 2500)
                .Pan(Sine(0.1) * 0.5)
                * 0.4
            """),
        new("Lo-fi",
            """
            var melody = Fn(t => 110 * Pow(2, new[] { 0, 3, 7, 10 }[(int)(t / 0.2) % 4] / 12.0));

            Square(melody)
                .Distort(drive: 2, DistortionMode.Fold)
                .BitCrush(bits: 5, rate: 6000)
                .HighPass(120)
                .Limit()
                * 0.5
            """),
        new("Custom operator",
            """
            // Through hands you the running input and expects an operator back.
            // Operator.From writes one as a lambda; state lives in captured variables,
            // declared inside Through so every compile starts fresh.
            Saw(110).Through(input =>
            {
                var held = StereoFrame.Silence;
                var step = 0;

                return Operator.From(block =>
                {
                    input.RenderBlock(block);          // pull the input, then rework it in place
                    for (int i = 0; i != block.Length; i++)
                    {
                        if (step == 0) held = block[i];   // sample and hold: a crude downsampler
                        step = (step + 1) % 8;
                        block[i] = held;
                    }
                });
            })
            .LowPass(3000)
            * 0.3
            """),
        new("Drone",
            """
            Sound Voice(double hz, double detune) =>
                Saw(hz * (1 + detune)) + Saw(hz * (1 - detune));

            (Voice(55, 0.003) + Voice(82.5, 0.004) * 0.6 + Voice(110, 0.002) * 0.4)
                .LowPass(Sine(0.05).Range(300, 1800), q: 1.2)
                .Adsr(4, 0, 1, 0)
                * 0.15
            """),
    ];
}
