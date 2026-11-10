# Effects

Effects are methods on a `Sound`. Each returns a new sound, so they chain. Order matters: a delay after a reverb is not the same as a reverb after a delay.

Effects that have a wet/dry balance take `mix`, from 0 (dry) to 1 (wet).

## Time-based

```csharp
// A dotted echo: 375 ms, fed back at half strength, mixed at 40%.
Saw(110).Adsr(0.005, 0.1, 0.4, 0.2).Delay(0.375, feedback: 0.5, mix: 0.4)
```

```csharp
// Staccato plucks on eighth notes. Each echo lands between two notes and bounces across the stereo field.
Mini.Notes("c4 e4 g4 e4 a4 g4 e4 ~")
    .Play(v => Saw(v.Frequency).LowPass(2000, q: 1).Adsr(0.002, 0.15, 0, 0.05), bpm: 120)
    .TapeDelay(time: 0.375, feedback: 0.5, pingPong: true) * 0.4
```

```csharp
// A plucked string: a noise burst into a tuned comb resonator.
Noise().Adsr(0, 0.01, 0, 0.001).Comb(220, feedback: 0.995)
```

`Reverb(size, decay, damping, mix)` places a sound in a room:

```csharp
// A short, damp room.
Sine(440).Adsr(0.005, 0.2, 0, 0.1).Reverb(size: 0.6, decay: 1.5, damping: 0.6, mix: 0.35)
```

## Impulse responses

`Convolve(impulse, mix)` plays a sound through an impulse response: a recording of how a space, a speaker cabinet or any other linear system answers a single click. It is how a guitar cabinet or a real concert hall is put on a sound. The convolver adds no latency, and a response of up to ten seconds, mono or stereo, runs comfortably in real time.

```csharp
// A guitar cabinet, as it would load from a file: a short response whose resonance colours the sound.
// This one is built in code, a 1.2 kHz ring that fades in 12 ms. Listen for the saw going from buzzy to boxy.
const int rate = 48_000;
var frames = new StereoFrame[rate * 12 / 1000];
for (int i = 0; i < frames.Length; i++)
    frames[i] = StereoFrame.Mono((float)(Sin(2 * PI * 1200 * i / rate) * Exp(-i / 90.0)));
var cabinet = new AudioBuffer(frames, rate);

Saw(110).Distort(drive: 4).Convolve(cabinet) * 0.4
```

A response is an `AudioBuffer`, so it can come from a file or be built in code. A response whose tail dies away over seconds is a reverb, and `mix` sets how much of it to blend in:

```csharp
// A room from noise: a response that starts loud and dies away over 1.5 s is what a reverb tail is.
// Left and right get different noise, so the room is wide. Plucks go in, the room comes back with them.
const int rate = 48_000;
var random = new Random(7);
var frames = new StereoFrame[rate * 3 / 2];
for (int i = 0; i < frames.Length; i++)
{
    double fade = Exp(-4.6 * i / frames.Length);
    frames[i] = new StereoFrame((float)((random.NextDouble() * 2 - 1) * fade), (float)((random.NextDouble() * 2 - 1) * fade));
}
var room = new AudioBuffer(frames, rate);

var pluck = Saw(220).LowPass(2000).Adsr(0.002, 0.2, 0, 0.1) * Fn(t => t % 1 < 0.5 ? 1 : 0);
pluck.Convolve(room, mix: 0.4) * 0.5
```

What to know:

- **Level.** `normalize` scales the response so its energy is one by default (`IrNormalize.Energy`), so files recorded at different levels sit at about the same loudness. `Peak` makes the largest tap one, and `Off` uses the file as it is.
- **Sample rate.** A response recorded at another rate is converted to the rate of the render, so a 44.1 kHz file works in a 48 kHz sound.
- **Stereo.** A stereo response convolves the left channel with its left side and the right with its right, which keeps the image of a stereo room. A mono file, or two identical channels, costs half as much.
- **Length and memory.** Longer than ten seconds is cut there, with a short fade-out. A response takes about 32 bytes per tap when it is stereo (16 when mono), and every running instance, such as each voice of a `Play`, needs the same again for its own state. A ten second stereo response is about 16 MB plus 16 MB per instance, so keep responses short when many voices run at once.
- **Everything else is composition.** There is no pre-delay, filter or width parameter, because the library already has them: `Delay` before, `HighPass` and `LowPass` after, `Spread` for the width.

In a project, load the file yourself. In the REPL, drop the `.wav` on the page and name it with `Samples.Load`.

```csharp
using static Sigil.Sound.Sounds;
using Sigil.Sound;

// A cabinet from disk, under a distorted saw, with a low cut after it.
var cabinet = AudioBuffer.FromWav("cab.wav");
Saw(110).Distort(drive: 4).Convolve(cabinet).HighPass(80) * 0.4
```

## Modulation

```csharp
// A slow chorus: three voices, detuned slightly by a 0.5 Hz LFO.
(Saw(110) + Saw(165)).LowPass(1500).Chorus(rate: 0.5, voices: 3) * 0.3
```

```csharp
// A phaser sweeps notches through the spectrum.
Saw(110).Phaser(rate: 0.2, feedback: 0.6, stages: 6) * 0.4
```

## Distortion and grit

```csharp
// Soft clipping, pushed hard: a warm overdrive.
Saw(110).Distort(drive: 8) * 0.4
```

```csharp
// Wavefolding: the harmonics fold back on themselves, a West Coast sound.
Sine(110).Fold(amount: Sine(0.2).Range(1.0, 7.0), symmetry: 0.25, mix: 1.0).LowPass(4000) * 0.4
```

```csharp
// Bit reduction and sample-rate reduction together: lo-fi and gritty.
Saw(110).BitCrush(bits: 5, rate: 6000) * 0.4
```

## Pitch

`PitchShift(semitones, mix)` moves the whole spectrum up or down without changing the speed. It is spectral, so there is a delay of about 40 ms.

```csharp
// A fifth up, mixed with the dry signal.
Saw(110).PitchShift(7, mix: 0.5) * 0.4
```

## Feedback loops

`Feedback` feeds the output of a chain back into its input. Keep the loop gain below 1 or it grows without bound.

```csharp
// A pitch-climbing echo: each repeat rises an octave and darkens slightly.
Noise().Adsr(0, 0.005, 0, 0.001)
    .Feedback(echo => echo.Delay(0.3, mix: 1).PitchShift(12).LowPass(5000) * 0.5)
```

## Stereo and dynamics

```csharp
// Equal-power panning: a saw drifting between the speakers.
Saw(110).Pan(Sine(0.3)) * 0.4
```

```csharp
// Balance turns one side down without narrowing the stereo image.
(Saw(110) + Saw(165)).Chorus().Balance(-0.3) * 0.3
```

```csharp
// Width 0 collapses a stereo sound to mono, which keeps the bass centred.
Saw(55).LowPass(300).Chorus(rate: 0.4, voices: 3).Spread(0.0) * 0.5
```

```csharp
// Compression: a sine pushed down by 4:1 above -16 dB, then made up.
Sine(110).Adsr(0.005, 0.2, 0.8, 0.3).Compress(thresholdDb: -16, ratio: 4, makeupGainDb: 3)
```
