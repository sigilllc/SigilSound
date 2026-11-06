# Effects

Effects are methods on a `Sound`. Each returns a new sound, so they chain. Order matters: a delay after a reverb is not the same as a reverb after a delay.

Effects that have a wet/dry balance take `mix`, from 0 (dry) to 1 (wet).

## Time-based

```csharp
// A dotted echo: 375 ms, fed back at half strength, mixed at 40%.
Saw(110).Adsr(0.005, 0.1, 0.4, 0.2).Delay(0.375, feedback: 0.5, mix: 0.4)
```

```csharp
// A tape-style delay that bounces echoes between the speakers.
Saw(110).Adsr(0.005, 0.1, 0.4, 0.2).TapeDelay(time: 0.375, feedback: 0.5, pingPong: true)
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
