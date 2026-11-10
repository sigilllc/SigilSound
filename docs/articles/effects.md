# Effects

Effects are methods on a `Sound`. Each returns a new sound, so they chain. Order matters: a delay after a reverb is not the same as a reverb after a delay.

Effects that have a wet/dry balance take `mix`, from 0 (dry) to 1 (wet).

## Time-based

```csharp
// A hi-hat tick every half second. The dotted-eighth echoes fall between the ticks, so the rhythm doubles up.
(Noise().BandPass(7000, q: 2) * Fn(t => Exp(-(t % 0.5) * 60)) * 2.5).Delay(0.375, feedback: 0.5, mix: 0.4) * 0.8
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
// A clap every second in a large hall: each tail is still ringing when the next clap lands.
(Noise().BandPass(1500, q: 1) * Fn(t => Exp(-(t % 1) * 35)) * 3.5).Reverb(size: 2.5, decay: 4, damping: 0.4, mix: 0.5) * 0.8
```

## Impulse responses

`Convolve(impulse, mix)` plays a sound through an impulse response: a recording of how a space, a speaker cabinet or any other linear system answers a single click. It is how a guitar cabinet or a real concert hall is put on a sound. The convolver adds no latency, and a response of up to ten seconds, mono or stereo, runs comfortably in real time.

```csharp
// A drum groove in a recorded living room: every hit comes back with the room's reflections.
// IR_Speakers_Living_Room.wav comes with the page (CC0, by mansardian on Freesound). Samples.Load reads it by name.
var bpm = 110;
var room = Samples.Load("IR_Speakers_Living_Room.wav");

var kick = Mini.Notes("c1 ~ ~ c1 ~ c1 ~ ~");
var snare = Mini.Notes("~ c3 ~ c3");
var hats = Mini.Notes("c4*8").Degrade(0.2, seed: 3);

var drums =
    kick.Play(v => Sine(Fn(t => 48 + 110 * Exp(-t * 28))).Adsr(0.001, 0.25, 0, 0.02), bpm) * 0.9
    + snare.Play(v => Noise().BandPass(1800, q: 1.2).Adsr(0.001, 0.12, 0, 0.05), bpm) * 0.5
    + hats.Play(v => Noise(4).HighPass(7000).Adsr(0.001, 0.04, 0, 0.01), bpm) * 0.25;

drums.Convolve(room, mix: 0.5) * 0.7
```

A response is an `AudioBuffer`, so it can come from a file or be built in code. Here is a guitar cabinet built in code:

```csharp
// A guitar cabinet built in code: a 1.2 kHz ring that fades in 12 ms, so the riff goes from buzzy to boxy.
const int rate = 48_000;
var frames = new StereoFrame[rate * 12 / 1000];
for (int i = 0; i < frames.Length; i++)
    frames[i] = StereoFrame.Mono((float)(Sin(2 * PI * 1200 * i / rate) * Exp(-i / 90.0)));
var cabinet = new AudioBuffer(frames, rate);

Mini.Notes("e2 e2 ~ e2 g2 e2 d2 ~")
    .Play(v => Square(v.Frequency, 0.5).Adsr(0.002, 0.2, 0.3, 0.1).Distort(drive: 4), bpm: 120)
    .Convolve(cabinet) * 0.15
```

A response whose tail dies away over seconds is a reverb, and `mix` sets how much of it to blend in:

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

Mini.Notes("c3 e3 g3 c4 ~ g3 e3 ~")
    .Play(v => Noise().Adsr(0, 0.01, 0, 0.001).Comb(v.Frequency, feedback: 0.99), bpm: 120)
    .Convolve(room, mix: 0.4) * 1.4
```

What to know:

- **Level.** `normalize` scales the response so its energy is one by default (`IrNormalize.Energy`), so files recorded at different levels sit at about the same loudness. `Peak` makes the largest tap one, and `Off` uses the file as it is.
- **Sample rate.** A response recorded at another rate is converted to the rate of the render, so a 44.1 kHz file works in a 48 kHz sound.
- **Stereo.** A stereo response convolves the left channel with its left side and the right with its right, which keeps the image of a stereo room. A mono file, or two identical channels, costs half as much.
- **Length and memory.** Longer than ten seconds is cut there, with a short fade-out. A response takes about 32 bytes per tap when it is stereo (16 when mono), and every running instance, such as each voice of a `Play`, needs the same again for its own state. A ten second stereo response is about 16 MB plus 16 MB per instance, so keep responses short when many voices run at once.
- **Everything else is composition.** There is no pre-delay, filter or width parameter, because the library already has them: `Delay` before, `HighPass` and `LowPass` after, `Spread` for the width.

In a project, load a file with `AudioBuffer.FromWav(path)`. In the REPL, drop your own `.wav` on the page and name it with `Samples.Load`, as the first example does.

## Modulation

```csharp
// A chorused electric piano: the plucks shimmer as four copies drift in and out of step.
Mini.Notes("c4 e4 g4 b4 g4 e4 c4 ~")
    .Play(v => Triangle(v.Frequency).Adsr(0.003, 0.6, 0.2, 0.3), bpm: 110)
    .Chorus(rate: 0.5, voices: 4) * 0.4
```

```csharp
// A phaser on noise: the notches sweep up and down, so the hiss turns into a jet passing overhead.
Noise().LowPass(4000).Phaser(rate: 0.2, feedback: 0.8, stages: 8, mix: 0.6) * 0.7
```

## Distortion and grit

```csharp
// A sine that gets fuzzier: the drive sweeps from clean to heavy, and harmonics bloom as it does.
Sine(110).Distort(drive: Sine(0.25).Range(1, 12)) * 0.4
```

```csharp
// Wavefolding: the harmonics fold back on themselves, a West Coast sound.
Sine(110).Fold(amount: Sine(0.2).Range(1.0, 7.0), symmetry: 0.25, mix: 1.0).LowPass(4000) * 0.4
```

```csharp
// A melody that gets crushed and then cleans up: the bit depth falls to 2 and climbs back every 4 seconds.
Mini.Notes("c4 e4 g4 c5 ~ g4 e4 c4")
    .Play(v => Triangle(v.Frequency).Adsr(0.005, 0.25, 0.5, 0.2), bpm: 120)
    .BitCrush(bits: Sine(0.25).Range(2, 12), rate: 6000) * 0.5
```

## Pitch

`PitchShift(semitones, mix)` moves the whole spectrum up or down without changing the speed. It is spectral, so there is a delay of about 40 ms.

```csharp
// A melody with a fifth above it: the shifted copy is mixed in at half level, so the tune comes back in harmony.
Mini.Notes("c4 d4 e4 g4 a4 ~ g4 e4")
    .Play(v => Triangle(v.Frequency).Adsr(0.005, 0.2, 0.5, 0.2), bpm: 120)
    .PitchShift(7, mix: 0.5) * 0.4
```

## Feedback loops

`Feedback` feeds the output of a chain back into its input. Keep the loop gain below 1 or it grows without bound. A distortion in the loop has a gain of its drive at low levels, so its `level` brings that back: here 4 × 0.25 × 0.7 is below 1.

```csharp
// Echoes fed back through a distortion: each repeat comes back fuzzier and darker than the one before.
Sine(330).Adsr(0.002, 0.05, 0, 0.02)
    .Feedback(echo => echo.Delay(0.3, mix: 1).Distort(drive: 4, level: 0.25).LowPass(3000) * 0.7) * 0.6
```

## Stereo and dynamics

```csharp
// Equal-power panning: a bell line that swings from left to right and back every four seconds.
Mini.Notes("a3 c4 e4 a4 e4 c4 ~ ~")
    .Play(v => Sine(v.Frequency).Adsr(0.002, 0.5, 0, 0.3), bpm: 100)
    .Pan(Sine(0.25)) * 0.4
```

```csharp
// Balance turns one side down without narrowing the stereo image: here the balance swings from one ear to the other.
(Triangle(130.81) + Triangle(164.81) + Triangle(196)).Chorus(rate: 0.5, voices: 4).Balance(Sine(0.25)) * 0.3
```

```csharp
// The width swings from 0 (mono, the chorus collapses to the centre) to 2.5 (wide), so the pad opens and closes.
(Triangle(110) + Triangle(165)).LowPass(1200).Chorus(rate: 0.5, voices: 4).Spread(Sine(0.25).Range(0, 2.5)) * 0.3
```

```csharp
// Sidechain pumping: the kick ducks the pad on every beat, so the pad breathes in time with the drum.
var kick = Sine(Fn(t => 50 + 90 * Exp(-(t % 0.5) * 30))) * Fn(t => Exp(-(t % 0.5) * 7));
var pad = (Triangle(110) + Triangle(165) + Triangle(220)).LowPass(900);

(pad.Compress(thresholdDb: -20, ratio: 8, attack: 0.002, release: 0.25, sidechain: kick) * 0.3 + kick * 0.6)
```
