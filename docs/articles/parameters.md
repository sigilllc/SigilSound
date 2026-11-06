# Parameters and modulation

Almost every argument in the library is a `Param`. A `Param` is one of three things:

- **A number**: a constant value, `800` or `0.4`.
- **A sound**: another `Sound`, evaluated sample by sample. This is audio-rate modulation.
- **A function of time**: `Fn(t => ...)` or a `Func<double, float>` in a project.

You never need to name the type. Pass whichever is convenient.

## A number

```csharp
// The filter sits at a fixed 800 Hz.
Saw(110).LowPass(800, q: 2) * 0.4
```

## A sound

The modulating sound runs at audio rate: its value changes on every sample, not once per block.

```csharp
// A slow sine sweeps the filter cutoff between 200 Hz and 2 kHz.
Saw(110).LowPass(Sine(0.25).Range(200, 2000), q: 4) * 0.4
```

`Range(min, max)` maps a sound from its own -1 to 1 onto a new span. Use it to turn an oscillator into a usable control.

## Frequency modulation

A sound given as a frequency gives frequency modulation (FM). This is true FM: the phase follows the instantaneous frequency on every sample.

```csharp
// A 220 Hz carrier, pitch-wobbled by a 440 Hz modulator, 300 Hz deep.
Sine(220 + Sine(440) * 300) * 0.4
```

The modulator's frequency sets the sideband spacing, and its depth sets how bright the sound is. Multiply the modulator by a larger number to brighten it.

## A function of time

```csharp
// A saw whose cutoff follows a formula: 800 Hz with a 600 Hz swing.
Saw(110).LowPass(Fn(t => 800 + 600 * Math.Sin(t)), q: 2) * 0.4
```

## Combining modulators

A parameter can take a sound that is itself modulated, and that sound can have its own modulators.

```csharp
// The filter's own cutoff is wobbled by a slower sine, while the saw is pitch-modulated too.
var wobble = Sine(0.2).Range(400, 1600);
Saw(110 + Sine(3) * 2).LowPass(wobble, q: 3) * 0.4
```

## Things to know

- **Frequencies are in hertz.** An oscillator at 0.25 Hz is an LFO, not a sound.
- **Modulators are sounds, so they cost CPU.** A constant costs nothing; a sound used as a parameter runs its own graph.
- **`Range` keeps the shape.** A sine from -1 to 1 becomes a sine between the new bounds, not a different wave.
