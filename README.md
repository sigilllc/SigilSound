# Sigil.Sound

Composable sound synthesis for .NET: describe a sound as an immutable value, compose it like LINQ, and render it when you need it.

> **Status: pre-alpha, private.** Nothing here is a stable API yet. No license has been chosen, so no `LICENSE` file is included and no rights are granted.

## Quick Start

```csharp
using static Sigil.Sound.Sounds;

// Audio-rate parameter modulation
var sweep = Sine(0.25).Range(200, 2000);

var pad = Saw(110)
    .LowPass(cutoff: sweep, q: 2.0)
    .Adsr(0.01, 0.2, 0.6, 1.5)
    .Chorus()
    .Reverb(size: 0.8, mix: 0.3);

var melody = Notes.Parse("C4 E4 G4 B4").Transpose(12)
    .Every(Beats.Eighth, gate: 2)
    .Loop();

var lead = melody.Play(v => Square(v.Frequency).Adsr(0.005, 0.1, 0.7, 0.2), polyphony: 8);

var mix = (pad * 0.5 + lead * 0.3).Limit();
mix.RenderToWav("demo.wav", seconds: 8, sampleRate: 48_000);
```

- **Sources:** `Sine`, `Saw` and `Square` (PolyBLEP band-limited, variable pulse width), `Noise` (seeded), `Constant`, `Fn(t => ...)`.
- **Operators:** `Gain` / `*`, mix / `+`, offset / `+ 12`, ring modulation (`sound * sound`), `Range`, `Adsr`, `LowPass`, `HighPass`, `BandPass`, `Notch` (topology-preserving state-variable filter), `Pan`, `Limit`.
- **Effects:** `Delay`, `Reverb` (Schroeder stereo decorrelated), `Comb` (tuned resonator; Karplus-Strong string), `Distort` (soft, hard, fold, fuzz), `BitCrush`, `Chorus`, `Phaser`. Each takes `mix` where applicable, and continuous parameters accept numbers, sounds, or `Fn(t => ...)`.
- **Parameters:** Every `Param` accepts a number, another `Sound` (audio-rate modulation), or a `Func<double, float>` of time. Constant parameters are evaluated with zero per-block overhead.
- **Sequences & Notes:** `Notes.Parse("C4 Eb4 G4")`, LINQ transformations (`Where`, `Select`, `Transpose`), timing (`Every`, `Beats`), and zero-allocation polyphonic playback (`Play`).
- **Rendering:** `Render(seconds)` returns an in-memory `AudioBuffer`; `RenderToWav` writes 16-bit, 24-bit, or 32-bit float WAV to a path or a `Stream`.
- **Live Playback and Hot Swap:** `SoundContext` is a running output: `Play(sound)` crossfades from the current audio, and audio callbacks pull frames with `Render(span)`. `new Slot(sound)` inserts a swappable location into any graph; `slot.Swap(next)` compiles off-thread and crossfades atomically.

## Architecture

1. **`Sound`**: An immutable, stateless description. Composing never renders or mutates anything, so a sound can be reused, nested, and rendered repeatedly. If a `Sound` node appears multiple times in a graph, `Compile` shares the rendered block automatically.
2. **`IOperator`**: The stateful running instance produced by `sound.Compile(RenderContext)`. Its single `RenderBlock(Span<StereoFrame>)` method is hard real-time safe (zero managed allocations, zero locks, non-blocking).
3. **`IResettable`**: Built-in operators can reset to their initial state, enabling polyphonic voice pooling with zero allocations during note triggering.

`sound.Through(input => new MyOperator(input))` and `Sound.FromOperator(ctx => ...)` connect custom operators to the fluent API.

## Packages

| Package | Contents | Status |
| --- | --- | --- |
| `Sigil.Sound` | `Sound`, `Param`, `RenderContext`, `IOperator`, oscillators, envelopes, TP-SVF filters, effects, offline render, WAV writing. Zero dependencies, browser-wasm clean. | Scaffold |
| `Sigil.Sound.Notes` | Notes, pitch parsing, note sequences, timing patterns, polyphonic voice engine. | Scaffold |
| `Sigil.Sound.Testing` | `SignalAnalysis` and `AudioAssert` for tolerance-based audio assertions | Scaffold |
| `Sigil.Sound.SoundFont` | SoundFont playback | Planned |
| `Sigil.Sound.Playback` | Real-time audio driver integrations | Planned |

## Browser REPL

`apps/Sigil.Sound.Repl` runs a full C# audio synthesizer workspace in the browser: Blazor WebAssembly hosts .NET, Roslyn compiles C# on the fly, and the engine streams into Web Audio via an `AudioWorklet`.

```bash
dotnet run --project apps/Sigil.Sound.Repl
```

For production builds: `dotnet publish apps/Sigil.Sound.Repl -c Release -o artifacts/repl`.

## Build & Test

Supports .NET 8.0 (LTS) and .NET 10.0.

```bash
dotnet build
dotnet test
```

## Heritage

Sigil.Sound is a clean, modern rewrite of the engine powering Sigilgraph Audio Workbench (`AudioWorkbench.Core`), eliminating legacy singletons, per-sample abstractions, and desktop coupling in favor of a fluent, functional, allocation-free architecture.
