# Changelog

All notable changes to the Sigil.Sound packages. The format follows [Keep a Changelog](https://keepachangelog.com/), and the versions follow [Semantic Versioning](https://semver.org/). Before 1.0 a minor version may change the public API; [Versioning](docs/articles/versioning.md) says what each number promises.

## Unreleased

### Sigil.Sound

- **Notes by name on a live input.** `NoteInput.NoteOn(string)` and `NoteOff(string)` take mini-notation (`keys.NoteOn("a2 g3 e4:0.6")`, `"[c3,e3,g3]"`): every note in the text starts or ends at once, with the velocity from `:0.6`. Invalid text throws `MiniNotationException` before any note starts. The numeric overloads are unchanged.
- **More places take note names.** `Scale.Notes(string)` and `Scale.Degrees(string)` read degrees as mini-notation (`Scale.Parse("c3 minor").Notes("0 2 <4 7>")`). `NoteInput.Start(string)` returns one `PlayingNote` per name, and `NoteInput.IsHeld(string)` asks about several at once. `Notes.Parse` now reads names as `Mini.Notes` does: a name without an octave is in octave 3 and `:0.6` sets the velocity (it threw on a missing octave before).
- **Drums by name.** `Mini.Drums("bd ~ sn ~")` gives notes on the General MIDI drum map (`bd`, `sn`, `hh`, `oh`, `cp`, `rim`, toms, `cr`, `rd`, `cb`).
- **Render from an offset.** `Render` and both `RenderToWav` overloads take a trailing `startSeconds` (default 0). The skipped part is still run and discarded, so oscillators, envelopes, delay lines and filters are in the state they would have reached by then, and the result matches that stretch of a longer render from zero.

## 0.1.0

The first public release of `Sigil.Sound`, `Sigil.Sound.Notes`, `Sigil.Sound.Testing` and `Sigil.Sound.Repl.Core`. The first three target .NET 8.0 and .NET 10.0, are trimmable and AOT-compatible, and `Sigil.Sound` has no dependencies and runs in the browser (WebAssembly). `Sigil.Sound.Repl.Core` targets .NET 10.0 only.

### Sigil.Sound

- **The model.** A `Sound` is an immutable description; `Compile` turns it into a running `IOperator` whose `RenderBlock` does not allocate, lock or block. Sounds compose with `+`, `*` and method chains, and a sound used twice in a graph is rendered once. Custom operators plug in with `Sound.FromOperator` and `Sound.Through`, and `Operator.From` writes one as a lambda.
- **Defaults.** Every method runs with no arguments, and each default is a documented, opinionated choice: `Sine()` is 440 Hz, `LowPass()` is 1000 Hz, `Delay()` is a dotted eighth, `Comb()` is tuned to 440 Hz, and so on (see [Defaults](docs/articles/defaults.md)). A parameter is required only when nothing sensible could stand in for it, such as a function, a sample or an impulse response. Tests keep it that way. In `Sigil.Sound.Notes`, `Every` steps one beat by default.
- **Sources.** `Sine`, `Saw`, `Square` (band-limited, with pulse width), `Triangle`, seeded `Noise`, `Constant`, `Fn(t => ...)` and `Sample`, which plays an `AudioBuffer` at any speed or pitch with one-shot, loop and ping-pong modes.
- **Parameters.** Every `Param` takes a number, another sound (audio-rate modulation), a function of time, or a `Control` that a host changes while the sound plays. A control also carries what a host UI needs: `Taper` (linear, log or squared knob travel), `Polarity`, `Style` (knob, slider or selector) and, for a selector made with `Control.Choice`, its `Options`. `Travel` sets the value from a 0 to 1 knob position through the taper, and `SetFromSignal` sets it from a modulating signal through the polarity. See [Controls](docs/articles/controls.md).
- **Shaping and tone.** `Adsr`, `Range`, `Pan`, `Balance`, `Limit`, `LowPass`, `HighPass`, `BandPass`, `Notch`, `Peak`, `LowShelf`, `HighShelf`, `Equalize`, `Vowel`, `Fold`, `Spread`.
- **Dynamics.** `Compress` (soft knee, sidechain, lookahead) and `FollowEnvelope`.
- **Effects.** `Delay`, `TapeDelay`, `Reverb`, `FdnReverb` (with optional shimmer), `Comb`, `Distort`, `BitCrush`, `Chorus`, `Phaser`, `PitchShift` and `Feedback` loops.
- **Impulse responses.** `Convolve(AudioBuffer, mix, normalize)` runs a sound through an impulse response with zero latency. The first 128 taps run directly and the rest in FFT partitions, so even a ten second stereo response runs about 62 times faster than real time on the development machine (about 340 ns per frame, worst 256-frame block 0.7 ms). Mono or stereo, any sample rate (converted when compiled), longer than 10 seconds is cut with a fade-out, and the prepared response is shared by every voice and recompile. `IrNormalize` chooses `Energy` (the default), `Peak` or `Off`.
- **Samples and files.** `AudioBuffer.FromWav` and `WavReader` decode 8, 16, 24 and 32-bit PCM and 32-bit float WAV; `RenderToWav` and `WavWriter` write 16-bit, 24-bit and 32-bit float. `SampleLibrary` resolves `Samples.Load("name.wav")` over any `ISampleSource`.
- **Rendering and live playback.** `Render` to memory, `RenderToWav`, `SoundContext` for a running output with crossfaded `Play`, and `Slot` for a swappable place in a graph.

### Sigil.Sound.Notes

- **Per-note expression.** `NoteInput.Start(pitch, velocity)` starts a note that is its own object, a `PlayingNote` with `Bend` (semitones), `Pressure` and `Slide` (0 to 1) that can be changed from any thread while it sounds, and `Release()`. Two notes at one pitch are independent, which is how an MPE controller plays. A voice reads the gesture as `v.Bend`, `v.Pressure` and `v.Slide` (each glides over about 10 ms), and `v.Frequency` and `v.Pitch` already include the bend, so existing sounds answer to a bending player unchanged. Notes started with `NoteOn` are unaffected. The library has no MIDI vocabulary: a host maps its own gestures onto the three numbers. See [Live notes and expression](docs/articles/live-notes.md).
- **Host sequence.** A pattern played without a tempo follows the host's clock, and `HostSequence.Observe` lets a host learn, while a script builds its sound, that it contains such a sequence, so it can offer play, pause, stop and a tempo for it. The observer is per thread and costs nothing when unset. Together with `Control` and `NoteInput` it is the host-integration surface; see [Hosting](docs/articles/hosting.md).
- Notes and pitch parsing (`Notes.Parse`), scales and chords, exact rational pattern time (`Fraction`), the pattern algebra, mini-notation (`Mini`), sequencing (`Every`, `Loop`, `Beats`), live note input (`NoteInput`), and allocation-free polyphonic playback (`Play`).

### Sigil.Sound.Testing

- `SignalAnalysis` (peak, RMS, frequency, spectrum) and `AudioAssert` for tolerance-based assertions on rendered audio.

### Sigil.Sound.Repl.Core

- The snippet compiler the browser REPL is built on, now a package for apps that let their users run Sigil.Sound scripts. `SnippetCompiler` compiles a C# snippet in memory with Roslyn and returns the `Sound` it describes. A security validator runs first, and a `SnippetProfile` lets the host add its own API and its own rules on top of the built-in ones (no files, network, reflection or processes; no preprocessor directives; loops and recursion are counted). `EditorServices` answers completion, signature and diagnostic questions for a code editor. This is a best-effort filter for code you did not write, not a security boundary: a host that runs code from strangers should still isolate it. Its public API is tracked in `PublicAPI.Shipped.txt` like the other packages, but it is pre-1.0 and may change in a minor release.

### Documentation and tooling

- A showcase in the REPL, **Church organ**: 64 sine partials in eight ranks, kept in tune by rotating phasors, as a single custom operator.
- A DocFX site with a guide for each area and an **Open in REPL** link under every example, and a browser REPL (`apps/Sigil.Sound.Repl`) that compiles C# snippets and streams to Web Audio.
- Every public member has an XML comment (`CS1591` is an error), every example in the API docs and the guides compiles and sounds (checked by `DocExampleTests`), and the public API is tracked in `PublicAPI.Shipped.txt` so a change to it is a build error.

### Versioning

What the numbers promise, including what a patch may and may not change about the sound, is in [Versioning](docs/articles/versioning.md).

### Known limits

- Four method groups, `Adsr`, `Sample`, `RenderToWav` and `Control.Map`, have several overloads with optional parameters (analyzer `RS0026`). They are visible as warnings and are to be settled before 1.0.
- `Sigil.Sound.SoundFont` and `Sigil.Sound.Playback` (real-time audio drivers) are planned, not part of this release.
