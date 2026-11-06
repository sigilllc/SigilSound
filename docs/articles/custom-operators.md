# Custom operators

Everything in the fluent API is built from one small interface, `IOperator`. You can write your own when no built-in does what you need.

## The model

- **`Sound`** is an immutable description. Composing sounds never renders anything.
- **`IOperator`** is the running instance. Its one method, `RenderBlock(Span<StereoFrame>)`, fills a block of frames.
- **`IResettable`** lets an operator return to its start state, so voices can be reused without allocating.

`RenderBlock` runs on the audio thread. It must not allocate, lock, or block.

## Wrapping a sound

`Through` puts an operator between a sound and whatever uses it. The wrapped operator receives the input's block and changes it. This one is a beat-repeat stutter, which the engine does not have: each 200 ms the sound plays live for 50 ms while it records that slice, then repeats the slice for the rest of the beat. The recording buffer and the position in the beat are captured state, kept between blocks.

```csharp
// Not part of the engine: a beat-repeat stutter, written in place with Through. Each 200 ms the sound
// plays live for 50 ms while it records that slice, then repeats the slice for the rest of the beat.
// The recording buffer and the position in the beat are captured state, kept between blocks.
(Saw(110) + Saw(165) * 0.5).Through(input =>
{
    const int slice = 2400;             // 50 ms at 48 kHz: the part that gets repeated
    const int beat = 9600;              // 200 ms: one live slice, then repeats of the recorded one
    var buffer = new StereoFrame[slice];
    var position = 0;                   // where we are in the beat, in samples

    return Operator.From(block =>
    {
        input.RenderBlock(block);       // pull the input, then rework it in place
        for (int i = 0; i != block.Length; i++)
        {
            var phase = position;
            position = (position + 1) % beat;
            if (phase < slice)
                buffer[phase] = block[i];                       // live: record and pass through
            else
                block[i] = buffer[(phase - slice) % slice];     // stutter: play the recording again
        }
    });
})
.LowPass(2500, q: 1.5)
* 0.4
```

Open it in the REPL under **Beat repeat** and change `slice`: a shorter slice gives a faster, more granular glitch. Change `beat` to set how often the stutter starts.

`Operator.From` builds an operator from a block function. Its state lives in the variables it captures, which is fine for a sound that is built once. Keep state in a class that implements `IOperator` and `IResettable` when a voice has to be reused.

## A source that keeps phasors

`Sound.FromOperator` is also the way to write an oscillator bank. A church organ is a good case: its sound is a stack of pipes, each with many overtones, which adds up to dozens of sine partials per note. Calling `Sin` for each of them, for every sample of every note, is the expensive way. A sine is one coordinate of a point going round a circle, and going round a circle is a fixed rotation:

```text
c' = c * cos(w) - s * sin(w)
s' = c * sin(w) + s * cos(w)       s is the sine, w is the phase step of one sample
```

That point is a **phasor**. It is the state of the partial, so the operator keeps 64 of them, one per harmonic. Each sample turns every phasor by its own `w` (four multiplies and two adds) and reads `s`. The trig runs only to aim the rotations, so it runs when the pitch changes, not per sample.

```csharp
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
```

Open it in the REPL under **Church organ**. What to look at:

- **Ranks.** The bank is the harmonics of the 16' pipe, an octave under the note. An organ stop (rank) is a pipe sounding at some multiple of that: 16' is the first harmonic, 8' (the note itself) the second, 4' the fourth, the mutations 2⅔', 1⅗' and 1⅓' the sixth, tenth and twelfth, 2' and 1' the eighth and sixteenth. `rankLevel` is how far each stop is drawn. Every rank brings its own overtones, which is how 8 stops fill 64 partials.
- **The loop order.** Partials are the outer loop and samples the inner one, so a phasor lives in the locals `c` and `s` for the whole block and is written back once. The partials add into `mix`, a buffer allocated in the factory.
- **Rounding drift.** Each rotation is very slightly too long or too short, and the error adds up. Once per block the phasor is divided by its own length, which puts it back on the circle. In `double` that is far more often than needed.
- **Pitch is a parameter.** `Param.Compile` gives a source that is read once per block, so `Organ(220)`, `Organ(Sine(5).Range(215, 225))` and `Organ(v.Frequency)` all work. The rotations are re-aimed only when the value changes. A glide costs 128 trig calls per block, against 16,384 for per-sample `Sin` calls.
- **Nyquist.** Partials above half the sample rate would fold back down as aliasing, so `count` leaves them out.
- **Reset.** The second argument of `Operator.From` puts every phasor back at phase 0, which is what a voice reused by `Play` needs.

Draw a rank in or out by changing its level in `rankLevel`: set the 4' and 2' to 0 for a dark flute chorus, or the 16' to 0 for a lighter tone. Change `partials` to 32 or 128 to hear what the upper harmonics add; the cost of the bank grows in step with it.

## Sources

`Sound.FromOperator` makes a sound from scratch. The factory gets a `RenderContext`, which holds the sample rate and block size, and returns the operator:

```csharp
using Sigil.Sound;

var ramp = Sound.FromOperator(context => Operator.From(block =>
{
    for (int i = 0; i < block.Length; i++)
        block[i] = StereoFrame.Mono(0.2f);
}), description: "Constant(0.2)");
```

## Rules

- Do not allocate inside `RenderBlock`. Allocate in the factory, which runs off the audio thread.
- Read frames as stereo: `StereoFrame` has `Left` and `Right`.
- Reset the state in `Reset`, so a reused voice starts clean.
- Give a description, so `ToString` shows what the sound is.

The built-in operators are in `src/Sigil.Sound/Operators` and are the best reference for the patterns above.
