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

Either end of the range can itself be a parameter, so a knob can set how far an LFO reaches:

```csharp
// The knob sets how high the sweep reaches; the bottom stays at 200 Hz.
var top = new Control(2000, 400, 8000, name: "Top");
Saw(110).LowPass(Sine(0.25).Range(200, top), q: 4) * 0.4
```

A modulator can also be shifted with `+` and `-`. `lfo - 0.5` moves a 0 to 1 signal down so it swings around zero, and `1 - lfo` turns it upside down:

```csharp
// A tremolo that dips down from full level, instead of rising up to it.
Saw(110) * (1 - Sine(4).Range(0, 0.8)) * 0.4
```

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

## Smoothing and stepping a modulator

`Slew(rise, fall)` limits how fast a signal can change, so a jump becomes a glide. On a pitch it is portamento; on a stepped control it smooths the steps; on a square LFO it rounds the corners. The glide covers about 63% of the way in the given time and nearly all of it in five times that, and rising and falling can take different times.

```csharp
// A two-note trill that glides between its notes.
Sine(Square(2).Range(220, 330).Slew(0.05)) * 0.3
```

`Hold(rate)` is sample and hold: it takes the signal's value `rate` times a second and holds it until the next time. Held noise is the classic stepped random modulation, and with a host clock the steps follow the host's time.

```csharp
// A new random pitch eight times a second.
Sine(Noise().Hold(8).Range(220, 880)) * 0.3
```

The two together give a random line that wanders smoothly instead of jumping:

```csharp
// A filter cutoff that drifts to a new random place twice a second.
Saw(110).LowPass(Noise().Hold(2).Slew(0.15).Range(300, 3000), q: 3) * 0.4
```

## Things to know

- **Frequencies are in hertz.** An oscillator at 0.25 Hz is an LFO, not a sound.
- **Modulators are sounds, so they cost CPU.** A constant costs nothing; a sound used as a parameter runs its own graph.
- **`Range` keeps the shape.** A sine from -1 to 1 becomes a sine between the new bounds, not a different wave.
