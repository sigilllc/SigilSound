# Sigil.Sound

Composable sound synthesis for .NET: describe a sound as an immutable value, compose it like LINQ, and render it when you need it.

> **Status: pre-alpha, private.** Nothing here is a stable API yet. No license has been chosen, so no `LICENSE` file is included and no rights are granted.

## Where this is going

The example below shows the **direction** of the API, not what works today. Some names (`Unison`, `Chorus`, `Reverb`, `Limit`, note timing and `Play`) don't exist yet.

```csharp
using static Sigil.Sound.Sounds;

var sweep = Sine(0.25).Range(200, 2000);   // a sound used as a parameter

var pad = Saw(110).Unison(voices: 3, detuneCents: 7)
    .LowPass(cutoff: sweep, q: 0.7)
    .Adsr(0.01, 0.2, 0.6, 1.5)
    .Chorus()
    .Reverb(size: 0.8, mix: 0.3);

var melody = Notes.Parse("C4 E4 G4 B4").Repeat()
    .Every(Beats.Eighth)
    .Where(n => n.Velocity > 0.5)
    .Transpose(12);

var lead = melody.Play(n => Square(n.Frequency).Adsr(0.005, 0.1, 0.7, 0.2), polyphony: 8);

var mix = (pad * 0.5 + lead * 0.3).Limit();
mix.RenderToWav("demo.wav", seconds: 8, sampleRate: 48_000);
```

## What works today

```csharp
using static Sigil.Sound.Sounds;

var sweep = Sine(0.25).Range(200, 2000);

var pad = Saw(110)
    .LowPass(cutoff: sweep, q: 0.7)
    .Adsr(0.01, 0.2, 0.6, 1.5, hold: 3);

var mix = pad * 0.5 + Noise() * Fn(t => t < 0.1 ? 0.2 : 0);
mix.RenderToWav("demo.wav", seconds: 5);
```

- **Sources:** `Sine`, `Saw` and `Square` (PolyBLEP band-limited, variable pulse width), `Noise` (seeded), `Constant`, `Fn(t => ...)`.
- **Operators:** `Gain` / `*`, mix / `+`, ring modulation (`sound * sound`), `Range`, `Adsr`, `LowPass` (resonant biquad).
- **Parameters:** every `Param` accepts a number, another `Sound` (audio-rate modulation) or a `Func<double, float>` of time.
- **Rendering:** `Render(seconds)` returns an in-memory `AudioBuffer`; `RenderToWav` writes 16-bit, 24-bit or 32-bit float WAV to a path or a `Stream`.

## Two layers

1. **`Sound`**: an immutable description. Composing never renders or mutates anything, so a sound can be reused and rendered many times.
2. **`IOperator`**: the running instance that `sound.Compile(RenderContext)` produces. It has one method, `RenderBlock(Span<StereoFrame>)`, which must be real-time safe (no allocations, locks or blocking I/O). `RenderContext` passes the sample rate and block size explicitly; there is no global audio context.

`sound.Through(input => new MyOperator(input))` and `Sound.FromOperator(ctx => ...)` connect your own operators to the fluent layer.

## Packages

| Package | Contents | Status |
| --- | --- | --- |
| `Sigil.Sound` | `Sound`, `Param`, `RenderContext`, `IOperator`, oscillators, envelopes, filters, offline render, WAV writing. No package dependencies, no P/Invoke, must stay `browser-wasm` clean. | Scaffold |
| `Sigil.Sound.Notes` | Notes, pitch parsing, note sequences. Later: timing, scales and chords, MIDI file reading, polyphony. | Scaffold |
| `Sigil.Sound.Testing` | `SignalAnalysis` and `AudioAssert` for tolerance-based audio tests | Scaffold |
| `Sigil.Sound.SoundFont` | SoundFont playback | Not started |
| `Sigil.Sound.Playback` | Real-time output and input with native binaries per platform | Not started |

## Next steps

- Hot-swappable `Slot` with a short crossfade (compile off the audio thread, swap on the next block).
- Port the remaining DSP (delay, reverb, dynamics, FX, granular, convolution, spectral, tuning) to the `IOperator` contract.
- Note timing (`Every`, `Beats`) and polyphonic `Play`.
- Golden-render tests, BenchmarkDotNet suite, public API baseline, CI on Windows, macOS and Linux.
- Choose a license and add `LICENSE`, `NOTICE` and `THIRD-PARTY-NOTICES`.

## Build

Requires the .NET 10 SDK.

```bash
dotnet build
dotnet test
```

## Heritage

Sigil.Sound is a clean rewrite of the engine behind Sigilgraph Audioworkbench (`AudioWorkbench.Core`). It keeps the familiar concepts (`StereoFrame`, block rendering, PolyBLEP oscillators, the cookbook biquad) and starts with a fresh history and a smaller, immutable API.
