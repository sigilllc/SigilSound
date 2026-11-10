# Sigil.Sound

A fluent API for sound synthesis in your .NET app. Build an instrument or an effect in a few lines, then record it to a file or play it live.

> **Status: 0.1.0, the first public release.** The public API is tracked and every example is tested, but it may still change before 1.0; see the [changelog](CHANGELOG.md). Licensed under the [MIT License](LICENSE).

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

- **Sources:** `Sine`, `Saw` and `Square` (PolyBLEP band-limited, variable pulse width), `Triangle` (band-limited, with a variable skew), `Noise` (seeded), `Constant`, `Fn(t => ...)`, and `Sample` (plays an `AudioBuffer` at any speed, or at a pitch, with one-shot, loop and ping-pong modes). Low-frequency modulation is ordinary math: a slow `Sine` or `Triangle` through `Range`, or an `Fn` for stepped values.
- **Samples:** `AudioBuffer.FromWav(path)`, `FromWav(stream)` and `FromWav(bytes)` decode 8, 16, 24 and 32-bit PCM and 32-bit float WAV, mono or stereo, and `WavReader.ReadInfo(bytes)` reads a file's format and length without decoding it. Snippets do not give file paths: they name a sample, `Samples.Load("kick.wav")`, and the host decides where samples live (`SampleLibrary` over an `ISampleSource`; `FolderSampleSource` in `Sigil.Sound.Repl` for a folder). In the browser REPL, drop `.wav` files on the page; the editor offers the names as you type the quotes. The path overload is blocked in snippets.
- **Operators:** `Gain` / `*`, mix / `+`, offset / `+ 12`, ring modulation (`sound * sound`), `Range`, `Adsr`, `LowPass`, `HighPass`, `BandPass`, `Notch` (topology-preserving state-variable filter), `Pan`, `Limit`.
- **Tone:** `Peak` (bell), `LowShelf`, `HighShelf` (RBJ biquads), `Equalize` (three-band tone control), `Vowel` (four formants, morphs A to U), `Fold` (West Coast wavefolder).
- **Dynamics:** `Compress` (soft knee, sidechain, lookahead), `FollowEnvelope` (loudness as a control signal), `Spread` (mid/side stereo width).
- **Impulse responses:** `Convolve` runs a sound through an `AudioBuffer`, such as a guitar cabinet or a concert hall recorded as a WAV file. Zero latency, mono or stereo, up to 10 seconds, any sample rate.
- **Effects:** `Delay`, `TapeDelay` (slewed read head, saturating and damped feedback), `Reverb` (Schroeder stereo decorrelated), `FdnReverb` (eight-line feedback network, optional shimmer), `Comb` (tuned resonator; Karplus-Strong string), `Distort` (soft, hard, fold, fuzz), `BitCrush`, `Chorus`, `Phaser`. Each takes `mix` where applicable, and continuous parameters accept numbers, sounds, or `Fn(t => ...)`.
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
| `Sigil.Sound` | `Sound`, `Param`, `RenderContext`, `IOperator`, oscillators, envelopes, TP-SVF filters, effects, samples, offline render, WAV reading and writing. Zero dependencies, browser-wasm clean. | 0.1.0 |
| `Sigil.Sound.Notes` | Notes, pitch parsing, note sequences, timing patterns, polyphonic voice engine. | 0.1.0 |
| `Sigil.Sound.Testing` | `SignalAnalysis` and `AudioAssert` for tolerance-based audio assertions | 0.1.0 |
| `Sigil.Sound.Repl.Core` | The snippet compiler behind the browser REPL: Roslyn compiles Sigil.Sound C# behind a security validator, with completion, signatures and diagnostics. For apps that let users run Sigil.Sound scripts. .NET 10 only; the API may change before 1.0. | 0.1.0 |
| `Sigil.Sound.SoundFont` | SoundFont playback | Planned |
| `Sigil.Sound.Playback` | Real-time audio driver integrations | Planned |

## Browser REPL

`apps/Sigil.Sound.Repl` runs a full C# audio synthesizer workspace in the browser: Blazor WebAssembly hosts .NET, Roslyn compiles C# on the fly, and the engine streams into Web Audio via an `AudioWorklet`.

```bash
dotnet run --project apps/Sigil.Sound.Repl
```

For production builds: `dotnet publish apps/Sigil.Sound.Repl -c Release -o artifacts/repl`.

Start with **Church organ** in the examples menu: sixty-four sine partials in eight ranks, kept in tune by rotating phasors instead of 64 calls to `Sin` per sample, written as a single custom operator. It shows what the library lets you build; [Custom operators](docs/articles/custom-operators.md) explains how it works.

## Versioning

Versions are `MAJOR.MINOR.PATCH`. What to expect:

| Part | Means | Example |
| --- | --- | --- |
| **MAJOR** | `0` is beta: the API may change between minor versions. `1`+ promises the API and the sound will not break within that major version. | `0.x` is beta |
| **MINOR** | New operators and features. Before 1.0 it may also break the API or retune a default, always listed in the [changelog](CHANGELOG.md). | `0.2.0` |
| **PATCH** | Fixes only. No API change and no intentional change to the sound. Always safe to take. | `0.1.1` |

**0.1.0 is the first public release**: complete enough to build real instruments and effects, and the start of the feedback phase. Until 1.0, pin to a minor version (`Version="[0.1.0,0.2.0)"`) to take fixes and move on purpose. All packages share one version. The full policy, including what counts as "the sound", is in [Versioning](docs/articles/versioning.md).

## Build & Test

Supports .NET 8.0 (LTS) and .NET 10.0.

```bash
dotnet build
dotnet test
```

## Contributing

Contributions are welcome: see [CONTRIBUTING.md](CONTRIBUTING.md), which says what to open an issue for before writing code. Questions and bugs: open an issue. Security problems: report privately, see [SECURITY.md](SECURITY.md). Please keep to the [Code of Conduct](CODE_OF_CONDUCT.md).

## Heritage

Sigil.Sound is a clean, modern rewrite of the engine powering Sigilgraph Audio Workbench (`AudioWorkbench.Core`), eliminating legacy singletons, per-sample abstractions, and desktop coupling in favor of a fluent, functional, allocation-free architecture.

## License & Trademarks

This project is licensed under the [MIT License](LICENSE). Bundled audio, loaded assets and dependencies, with their sources and terms, are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

### Trademarks

The "Sigil", "Sigil.Sound", and "AudioWorkbench" names, logos, and iconography are trademarks. You may fork and build on the code under the MIT license, but you may not use our brand assets or represent third-party forks as official Sigil products.

### Canonical Distribution Channels

- **Official NuGet Packages:** Available exclusively under the verified `Sigil.Sound` namespace on [nuget.org](https://www.nuget.org).
- **Official Live REPL:** Hosted at [sigilaud.io](https://sigilaud.io) and official project domains.
- **Official Source Repository:** [github.com/sigilllc/SigilSound](https://github.com/sigilllc/SigilSound).
