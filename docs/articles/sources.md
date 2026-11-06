# Sources

A source is a sound that makes signal without taking any input. They live in `Sigil.Sound.Sounds` (`using static Sigil.Sound.Sounds;` in a project; the REPL has them in scope already).

| Source | Makes |
| --- | --- |
| `Sine(frequency)` | A pure tone. Also a smooth LFO below 20 Hz. |
| `Saw(frequency)` | A bright sawtooth, band-limited so it stays clean at high pitches. |
| `Square(frequency, width)` | A pulse wave. `width` is the duty cycle, 0.5 is a square. |
| `Triangle(frequency, skew)` | A softer wave between sine and saw. `skew` moves the peak. |
| `Noise(seed)` | White noise. The same seed gives the same noise every time. |
| `Constant(value)` | A flat level. Useful as a building block, rarely as a sound. |
| `Fn(t => ...)` | Any function of time in seconds, evaluated per sample. |
| `Sample(buffer)` | A recorded sound. See [Samples](samples.md). |

`Silence` is also available as the empty sound.

Every frequency takes a number, another sound, or a function of time. The rest of the frequency handling is in [Parameters](parameters.md).

## Oscillators

```csharp
// A 220 Hz sine, turned down so it sits comfortably under a mix.
Sine(220) * 0.5
```

```csharp
// A pulse wave with a 30% duty cycle: thinner and more nasal than a square.
Square(110, 0.3) * 0.4
```

```csharp
// A triangle wave, softened by a gentle low-pass.
Triangle(165, 0.5).LowPass(2000) * 0.5
```

## Noise and constants

```csharp
// Filtered noise, quiet: the shape of a breath or a hi-hat tail.
Noise(seed: 7).HighPass(4000) * 0.2
```

```csharp
// A flat level of a quarter of full scale. Any sound can be offset by a constant.
Constant(0.25)
```

## Your own function of time

`Fn` takes `t`, the time in seconds from the start of the render, and returns the value for that instant.

```csharp
// A tone built directly from the formula: a 330 Hz sine at 40% level.
Fn(t => Math.Sin(2 * Math.PI * 330 * t)) * 0.4
```

## Samples

```csharp
// A sample from the REPL's built-in library, played at its recorded pitch.
Sample(Samples.Load("pluck.wav"))
```

See [Samples](samples.md) for playback speed, loop modes and sample files.
