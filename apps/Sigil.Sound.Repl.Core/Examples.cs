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
        new("Church organ",
            """
            // A church organ of 64 sine partials that never calls Sin per sample. Each partial is a phasor, a point (c, s) on
            // the unit circle: turning it by one sample's worth of phase is a fixed rotation (4 multiplies, 2 adds), and s is
            // the sine. 64 sines cost 64 trig calls per sample; this costs 128 per block, and only when the pitch changes.
            // The bank is the harmonics of the 16' pipe, an octave under the note. Each stop (rank) sits on one of them and
            // brings its own overtones, so the ranks stack up into a full plenum.
            Sound Organ(Param frequency) => Sound.FromOperator(context =>
            {
                const int partials = 64;
                var pitch = frequency.Compile(context);   // a number, a sound, or v.Frequency: the pitch can move
                var re = new double[partials];            // the phasors: re = cos(phase), im = sin(phase)
                var im = new double[partials];
                var turnRe = new double[partials];        // each partial's rotation per sample
                var turnIm = new double[partials];
                var gain = new double[partials];
                var rankAt = new[] { 1, 2, 4, 6, 8, 10, 12, 16 };                   // 16', 8', 4', 2 2/3', 2', 1 3/5', 1 1/3', 1'
                var rankLevel = new[] { 0.6, 1.0, 0.8, 0.5, 0.6, 0.3, 0.4, 0.3 };   // how far each stop is drawn
                var mix = new double[context.BlockSize];
                var lastHz = 0f;
                long elapsed = 0;                         // samples rendered, for the slow brightness sweep

                Action restart = () =>                    // every phasor back to phase 0, as a reused voice needs
                {
                    Array.Fill(re, 1.0);
                    Array.Clear(im);
                    lastHz = 0;
                    elapsed = 0;
                };
                restart();

                return Operator.From(block =>
                {
                    var hz = Max(pitch.Next(block.Length)[0], 1f) / 2;   // the 16' pipe: the 8' rank is the note itself
                    var count = Min(partials, (int)(context.Nyquist / hz));   // partials past Nyquist would alias: drop them

                    if (hz != lastHz)                     // the only trig: when the pitch moves, re-aim the rotations
                    {
                        lastHz = hz;
                        for (int k = 0; k < partials; k++)
                        {
                            var angle = 2 * PI * hz * (k + 1) / context.SampleRate;
                            turnRe[k] = Cos(angle);
                            turnIm[k] = Sin(angle);
                        }
                    }

                    // The spectrum: every rank is a pipe with overtones m = 1, 2, 3... that fade as m^-tilt, placed on its
                    // harmonic of the 16'. Tilt sweeps slowly, like a swell box opening and closing.
                    var tilt = 1.6 + 0.9 * Sin(2 * PI * 0.08 * elapsed / context.SampleRate);
                    elapsed += block.Length;
                    Array.Clear(gain);
                    for (int r = 0; r != rankAt.Length; r++)
                        for (int m = 1; rankAt[r] * m <= count; m++)
                            gain[rankAt[r] * m - 1] += rankLevel[r] * Pow(m, -tilt);

                    double total = 0;
                    for (int k = 0; k < partials; k++)
                        total += gain[k];

                    mix.AsSpan(0, block.Length).Clear();
                    for (int k = 0; k < count; k++)
                    {
                        double c = re[k], s = im[k], dc = turnRe[k], ds = turnIm[k], g = gain[k] / total;
                        for (int i = 0; i != block.Length; i++)
                        {
                            mix[i] += s * g;
                            (c, s) = (c * dc - s * ds, c * ds + s * dc);   // rotate by one sample
                        }

                        var fix = 1 / Sqrt(c * c + s * s);   // rounding makes the point drift off the circle: pull it back
                        re[k] = c * fix;
                        im[k] = s * fix;
                    }

                    for (int i = 0; i != block.Length; i++)
                        block[i] = StereoFrame.Mono((float)mix[i]);
                }, restart);
            }, description: $"Organ({frequency})");

            var line = Notes.Parse("C3 E3 G3 C4 B2 D3 G3 B3")
                .Every(Beats.Half, gate: 3)
                .Loop();

            line.Play(v => Organ(v.Frequency).Adsr(attack: 0.06, decay: 0.1, sustain: 0.9, release: 0.5), bpm: 72, polyphony: 6)
                .Reverb(size: 1.8, decay: 5, mix: 0.35)     // a stone nave
                .Limit()                                    // the overlapping chords add up: keep them under full scale
                * 0.9
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
        new("Tube fuzz",
            """
            // Not part of the engine: a triode tube stage, written in place with Through. Crank the signal into a curve that
            // squashes the positive swing harder than the negative one, and an even-harmonic growl appears that a plain clipper
            // does not have. That lopsidedness leaves a DC offset, so a one-pole blocker removes it. The previous sample's
            // values are captured state.
            var riff = Notes.Parse("E2 E2 G2 E2 A2 E2 B2 A2")
                .Every(Beats.Eighth, gate: 0.8)
                .Loop();

            riff.Play(v => Saw(v.Frequency).Adsr(attack: 0.002, decay: 0.2, sustain: 0.8, release: 0.05), bpm: 100, polyphony: 1)
                .Through(input =>
                {
                    const double drive = 18;                        // how hard the signal is pushed into the tube
                    const double bias = 0.3;                        // the grid bias: shifts the curve so one side clips earlier
                    double lastIn = 0, lastOut = 0;                 // for the DC blocker
                    return Operator.From(block =>
                    {
                        input.RenderBlock(block);
                        for (int i = 0; i != block.Length; i++)
                        {
                            var x = (block[i].Left + block[i].Right) * 0.5 * drive + bias;
                            var y = x > 0 ? Tanh(x) : Tanh(x * 0.5) * 2;    // the positive half squashes, the negative half stays open
                            var clean = y - lastIn + 0.995 * lastOut;       // remove the offset the lopsided curve leaves
                            lastIn = y;
                            lastOut = clean;
                            block[i] = StereoFrame.Mono((float)clean);
                        }
                    });
                })
                .LowPass(3500)                                      // a guitar cabinet rolls off the fizz
                * 0.25
            """),
        new("Cabinet (IR)",
            """
            // A guitar cabinet, as it would load from a file: a short response whose resonance colours the sound.
            // This one is built in code, a 1.2 kHz ring that fades in 12 ms. Listen for the saw going from buzzy to boxy.
            const int rate = 48_000;
            var frames = new StereoFrame[rate * 12 / 1000];
            for (int i = 0; i < frames.Length; i++)
                frames[i] = StereoFrame.Mono((float)(Sin(2 * PI * 1200 * i / rate) * Exp(-i / 90.0)));
            var cabinet = new AudioBuffer(frames, rate);

            Saw(110).Distort(drive: 4).Convolve(cabinet) * 0.4
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
        new("Sampler",
            """
            // A sample is just frames of audio held in memory. This one is built in code: a plucked C3
            // with a few harmonics, fading over 1.5 s. Sample(pluck, frequency, root) plays it at a pitch:
            // the root is the pitch the sample was recorded at (C3 = 130.81 Hz).
            const int rate = 48_000;
            const double root = 130.81;

            var frames = new StereoFrame[rate * 3 / 2];
            for (int i = 0; i < frames.Length; i++)
            {
                double t = i / (double)rate;
                double w = 2 * PI * root * t;
                double tone = Sin(w) + 0.5 * Sin(2 * w) + 0.25 * Sin(3 * w);
                frames[i] = StereoFrame.Mono((float)(tone * Exp(-3 * t) * 0.4));
            }
            var pluck = new AudioBuffer(frames, rate);

            var tune = Notes.Parse("C3 E3 G3 B3 C4 B3 G3 E3")
                .Every(Beats.Eighth, gate: 2)
                .Loop();

            // Each note plays the sample at its own speed. A one-shot rings out once per note.
            tune.Play(v => Sample(pluck, v.Frequency, root) * v.Velocity, polyphony: 8) * 0.5
            """),
        new("Pumping pad",
            """
            // The kick is the sidechain: while it sounds, the compressor turns the pad down, then lets it
            // come back as the kick fades. This is ducking, the sound behind a lot of dance music.
            var kick = Sine(Fn(t => 45 + 80 * Exp(-(t % 0.5) * 30)))   // the pitch drops every half second
                * Fn(t => Exp(-(t % 0.5) * 7));                        // and the kick fades out

            var pad = (Saw(110) + Saw(165) + Saw(220))
                .LowPass(900, q: 1.5)
                .Chorus(rate: 0.5, depth: 0.5, voices: 3);

            (pad.Compress(thresholdDb: -24, ratio: 10, attack: 0.002, release: 0.25, sidechain: kick) * 0.3
                + kick * 0.6)
                .Limit()
            """),
        new("Tape echo",
            """
            // Each repeat is darker and more saturated than the one before. When the delay time wobbles,
            // the repeats glide in pitch instead of clicking, the way a tape head does.
            var pluck = (Saw(220) + Saw(330) * 0.5)
                .LowPass(1500)
                * Fn(t => Exp(-(t % 0.5) * 9));          // a pluck every half second

            pluck
                .TapeDelay(time: Sine(0.25).Range(0.36, 0.40), feedback: 0.55, damping: 2500, drive: 1.6, mix: 0.4)
                .Reverb(decay: 2.5, mix: 0.2)
                * 0.4
            """),
        new("Pattern groove",
            """
            // Patterns written as text. One cycle is 4 beats; the steps of a sequence share it equally:
            //   "a b c"      three steps          "a ~ b"     a rest
            //   "a [b c]"    b and c share a step "a*2"       twice as fast
            //   "a(3,8)"     3 hits spread over 8 steps (a Euclidean rhythm)
            //   "<a b>"      one per cycle, in turn   "a?"     sometimes dropped (always the same ones)
            // Scales turn degrees into notes; Every, Rev, Degrade and friends reshape a pattern like data.
            // Mini.Drums reads drum names (bd sn hh oh cp rim ...) instead of pitches.
            var bpm = 120;
            var scale = Scale.Parse("c2 minor");            // degree 0 is the root, 7 is the root an octave up

            var bass = scale.Notes("0 ~ 0 [3 5] ~ 7 <5 3> ~")
                .Every(4, p => p.Rev());                     // every fourth cycle plays backwards
            var lead = scale.Notes("7 9 [10 12] ~ 14(3,8) <10 9>")
                .Degrade(0.2, seed: 1);
            var kick = Mini.Drums("bd(5,8)");
            var snare = Mini.Drums("~ sn");
            var hats = Mini.Drums("hh*8").Degrade(0.25, seed: 3);

            // Each lambda describes ONE note. Inside it, Fn(t => ...) counts from the start of that note,
            // so every bass note gets its own filter sweep and every kick its own pitch drop.
            var bassLine = bass.Play(v =>
                Saw(v.Frequency).LowPass(Fn(t => 300 + 1800 * Exp(-t * 10)), q: 3).Adsr(0.003, 0.2, 0.4, 0.1), bpm);
            var leadLine = lead.Play(v =>
                (Square(v.Frequency) + Saw(v.Frequency * 1.005)).LowPass(3200).Adsr(0.005, 0.1, 0.4, 0.2) * 0.5, bpm);
            var drums =
                kick.Play(v => Sine(Fn(t => 48 + 110 * Exp(-t * 28))).Adsr(0.001, 0.25, 0, 0.02), bpm) * 0.8
                + snare.Play(v => Noise().BandPass(1800, q: 1.2).Adsr(0.001, 0.12, 0, 0.05), bpm) * 0.35
                + hats.Play(v => Noise(4).HighPass(7000).Adsr(0.001, 0.04, 0, 0.01), bpm) * 0.25;

            (bassLine * 0.4 + leadLine * 0.25 + drums * 0.8)
                .Reverb(decay: 1.5, mix: 0.12)
                .Limit()
            """),
        new("Chords, arps and shimmer",
            """
            // A progression written as chord names ("root:quality"), played two ways at once: as block chords
            // for the pad and as an up-and-down arpeggio. The arpeggio then feeds back through an echo that
            // shifts each repeat up an octave, so the repeats climb and fade into a shimmer.
            var bpm = 100;
            var changes = Mini.Chords("<c3:min7 ab2:maj7 eb3:maj7 bb2:7>");     // one chord per cycle

            var pad = changes.Notes().Play(v =>
                (Saw(v.Frequency) + Saw(v.Frequency * 1.004)).LowPass(1800).Adsr(0.4, 0.3, 0.7, 0.8) * 0.2,
                bpm, polyphony: 12);

            var arp = changes.Arp(ArpMode.UpDown, octaves: 2)     // each chord's cycle is divided among its notes
                .Transpose(12)
                .Play(v => (Sine(v.Frequency) + Saw(v.Frequency) * 0.25).LowPass(4500).Adsr(0.002, 0.12, 0, 0.1), bpm);

            // Feedback: the output goes round the loop again, a moment later. Keep the loop gain below 1.
            var shimmer = arp.Feedback(echo =>
                echo.Delay(0.3, mix: 1).PitchShift(12).LowPass(6000) * 0.5);

            (pad * 0.8 + shimmer * 0.3)
                .Reverb(decay: 3.5, mix: 0.3)
                .Limit()
            """),
        new("Notes by name",
            """
            // Notes are written the way a score names them: c4, eb3, f#2. A name without an octave is in octave 3,
            // and :0.6 sets the velocity. Mini.Notes, Notes.Parse, Scale.Parse, Chord.Parse and NoteInput all read the same text.
            //
            // A NoteInput is live notes: NoteOn starts them, NoteOff releases them. Given a string, every note in it starts
            // at once, so this is a chord. The three names below are the same notes as the numbers 45, 55 and 64.
            var keys = new NoteInput();
            var synth = keys.Play(v => Saw(v.Frequency).LowPass(1500).Adsr(0.01, 0.2, 0.7, 0.4), polyphony: 6);
            keys.NoteOn("a2 g3 e4:0.6");

            // The same names as a pattern: "[c3,e3,g3]" is a chord, "<...>" picks one per cycle, "~" is a rest.
            var tune = Mini.Notes("<[a3,c4,e4] [f3,a3,c4] [c3,e3,g3] [g3,b3,d4]>")
                .Play(v => Triangle(v.Frequency).Adsr(0.01, 0.3, 0.3, 0.4), bpm: 80, polyphony: 8);

            (synth * 0.2 + tune * 0.25).Reverb(decay: 1.8, mix: 0.2)
            """),
        new("Note streams",
            """
            // Live notes, changed on the way to the sound. A NoteInput's Stream is its notes as they happen: operators shape it,
            // Play turns it into sound, and the end of a key ends everything made from it, however many notes that was.
            //
            // The chord below is pressed with NoteOn. Where keeps the notes below C5 (a small function over each note), Harmonize adds
            // the tone two steps above in C major, and Echo brings every note back twice, softer, an eighth later.
            // Change the chord, swap Harmonize(...) for Strum(0.1), or add .Select(n => n.Snap(Scale.Parse("d dorian"))).
            var keys = new NoteInput();

            var plucks = keys.Stream
                .Where(n => n.Pitch < 72)
                .Harmonize(Scale.Parse("c major"), 0, 2)
                .Echo(2, Beats.Eighth, 0.5)
                .Play(v => Saw(v.Frequency).LowPass(2200).Adsr(0.003, 0.2, 0, 0.25));

            keys.NoteOn("c3 e3 g3");

            plucks * 0.25
            """),
        new("A tune through a stream",
            """
            // A written melody becomes a stream with ToStream, so it goes through the same operators as notes you play.
            // Latch lets each note ring until the next one starts (the tune becomes legato), Harmonize adds a fifth to every note,
            // and Strum spreads each pair a little, the lower note first.
            var tune = Mini.Notes("c3 ~ e3 [g3 b3] ~ a3 g3 ~").ToStream();

            tune.Latch().Harmonize(0, 7).Strum(0.08)
                .Play(v => Triangle(v.Frequency).LowPass(2400).Adsr(0.01, 0.2, 0.6, 0.4))
                .Reverb(decay: 1.5, mix: 0.2)
                * 0.3
            """),
        new("Acid line",
            """
            // A monosynth line whose filter is driven by a pattern of numbers, locked to the same tempo:
            // ToSignal turns "400 900 2400 ..." into a control signal that steps along with the notes.
            // Jux plays the line on the left and a transformed copy (up an octave, a sixteenth late) on the right.
            var bpm = 132;
            var line = Mini.Notes("c2:1 c2:0.6 [c3 c2:0.7] ~ eb2:1 c2:0.6 [g2:1 f2] c2:0.7");

            var cutoff = Mini.Numbers("400 900 2400 600 1500 350 2800 700")
                .ToSignal(bpm: bpm, smoothSeconds: 0.03);          // one value per step of the line

            line.Jux(p => p.Transpose(12).Shift(new Fraction(1, 16)),
                    v => Saw(v.Frequency).Adsr(0.002, 0.15, 0.5, 0.05),
                    bpm, polyphony: 4, width: 0.7)
                .LowPass(cutoff, q: 8)                              // the resonant sweep
                .Distort(drive: 4)
                .TapeDelay(time: 60.0 / bpm * 0.75, feedback: 0.45, damping: 3000, drive: 1.3, mix: 0.3)  // dotted eighth
                .Limit()
                * 0.4
            """),
        new("Harmonizer",
            """
            // One melody, three voices. `tune` appears three times below but is built once and shared, and
            // PitchShift moves its whole spectrum: a fifth above and an octave below. The shifted voices arrive
            // about 40 ms after the original (the spectral frame), which reads as a doubling.
            var tune = Mini.Notes("c4 [e4 g4] <b4 a4> g4")
                .Play(v => Saw(v.Frequency).LowPass(2600).Adsr(0.01, 0.15, 0.5, 0.25), bpm: 100);

            (tune * 0.5
                + tune.PitchShift(7) * 0.3
                + tune.PitchShift(-12) * 0.35)
                .Chorus(rate: 0.5, depth: 0.4, voices: 3)
                .Reverb(decay: 2.5, mix: 0.25)
                .Limit()
                * 1.2
            """),
        new("Bomb drop",
            """
            // A dive: the whole sound slides down two octaves over six seconds, then snaps back and falls again.
            // PitchShift moves the entire spectrum, so as the pitch drops the brightness goes with it:
            // the top of the spectrum falls to a quarter by the bottom of the dive, which is what makes it feel like falling.
            var cycle = 6.0;
            var dive = Fn(t => -24 * Pow(t % cycle / cycle, 1.5));      // 0 down to -24 semitones, accelerating

            var body = Saw(110) + Saw(110.7) + Saw(165) * 0.5 + Square(55) * 0.6
                + Noise(2).HighPass(2000) * 0.15;                        // the noise shows the top end falling

            body
                .LowPass(9000)
                .PitchShift(dive)
                .Reverb(decay: 2.5, mix: 0.2)
                .Limit()
                * 0.35
            """),
    ];
}
