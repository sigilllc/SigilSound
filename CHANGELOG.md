# Changelog

All notable changes to the Sigil.Sound packages. The format follows [Keep a Changelog](https://keepachangelog.com/), and the versions follow [Semantic Versioning](https://semver.org/). Before 1.0 a minor version may change the public API; [Versioning](docs/articles/versioning.md) says what each number promises.

## Unreleased

Planned as 0.2.0: safe to edit while it plays. Hot swaps that follow each other no longer click, five fixes, twelve new members for signal arithmetic and modulation, and faster rendering that leaves every sample as it was. Nothing is removed or renamed, and a 0.1.x script compiles and sounds the same unless it ran into one of the fixes below.

### Added

- **Subtraction.** `a - b` subtracts one sound from another, sample by sample on each channel; `a - 0.5` and `1 - a` work with a number, a function or a `Control`; `-a` flips the polarity. A sound used on both sides is rendered once. Only an effect that adds no delay cancels this way: `x - x.LowPass(500)` leaves what the filter removed, but `PitchShift` and `Compress` with its lookahead are late.
- **`Range(min, max)` with parameters for its ends**, so a knob, a function or another sound can set how far an LFO reaches: `Sine(0.25).Range(200, top)`. Plain numbers still choose the existing overload.
- **`Mix(other, amount)`**: an equal-power crossfade from one sound to another by a parameter (0.5 by default).
- **`Slew(rise, fall)`**: a glide toward each new value, for portamento, smoothing a stepped control or rounding a square LFO (0.05 s by default; `fall` defaults to `rise`).
- **`Hold(rate)`**: sample and hold, 8 steps a second by default. With a host clock the steps follow the host's time.
- **`Gate(thresholdDb, attack, release, sidechain)`**: a noise gate with 4 dB of hysteresis, -40 dB by default, and a sidechain like `Compress`'s.
- **`DcBlock(cutoff)`**: removes the offset that leaves a wave off centre, such as a narrow pulse's, with a gentle high-pass at 20 Hz.
- **`PinkNoise(seed)` and `BrownNoise(seed)`**: noise with equal energy per octave, and noise that is mostly lows. Seeded, independent per channel, with peaks near full scale.
- **`FadeCurve`** on `Slot` and `SoundContext`: `EqualPower` (the default) or `Linear`. A slot that carries a control signal should fade linearly, since two versions of the same signal rise by 3 dB halfway through an equal-power fade.

### Fixed

- **A swap during a crossfade cut off the sound that was fading out**, a click. It happened when Run was pressed twice in quick succession in the REPL, when Play followed Stop within its fade, and when a host swapped twice within one fade. A slot now keeps every sound that is still fading and fades each from the level it had reached; a burst of more than four swaps hurries the quietest out over 2 ms.
- **`Feedback` changed with the host's block size below 48 frames.** Its loop was a block late instead of the 1 ms its documentation promises, so a host rendering in small blocks heard a different sound. It is now equally late at every block size, so **a host with blocks under 48 frames hears a different (the documented) sound than in 0.1.x**. Hosts with larger blocks, the REPL and the Workbench device among them, hear no change.
- **`Slot` compiled under its lock when a render first attached to it**, so `Slot.Current` and a swap waited for the whole compile. A swap made during that compile is no longer lost.
- **A `mix` that was not a number reached the output** in `Delay`, `TapeDelay`, `Reverb`, `FdnReverb`, `Phaser` and `Fold`. It now plays dry.
- **An empty block threw** `IndexOutOfRangeException` in the filters, `Sample`, and `Sine`, `Saw` and `Square` under a host clock.

### Performance

The output is unchanged to the bit: every example in the API docs, the guides and the REPL was rendered and hashed before and after.

- A `Control` at rest now costs a filter, an EQ band or a vowel filter what a constant does: the coefficients are worked out once per block. A saw through a low-pass on a settled knob went from about 45 to about 31 ns per frame on the development machine (same session, Release). Every knob on a Workbench Sound Script device is such a control.
- `+`, `*` between sounds, a constant `Gain`, `Range` and the voice pool's mix use vector instructions where the machine has them. A low-pass swept by `Sine(...).Range(...)` went from about 109 to 100 ns per frame and the 16-voice pool from about 460 to 444. In the browser, where the .NET interpreter has no vector instructions, the plain loops run as before.

### Documentation and examples

- Guides for every new member, and a new **Integrations** page: the Sound Script device in Sigilgraph Audio Workbench, the REPL and its Sigilgraph mode, a small live host, and offline rendering.
- REPL: **Night drive**, a sparse groove that uses all of the new members, is now the example the REPL opens with, and the new guide **A whole piece** builds it a part at a time; **Edit me while it plays** and **Edit me: the beat keeps its place**, written to be changed while they play; and two Sigilgraph examples, **Random steps with glide** and **Noise gate**.
- `samples/LiveScript`: a Windows console app (NAudio) that plays a script file and swaps it in on every save, with the computer keyboard as a `NoteInput`.
- A feature request form, a pull request checklist and `CODEOWNERS`.

## 0.1.1

A patch release for 0.1.0: five fixes, no change to the public API.

### Fixed

- **A sound used both inside a `Feedback` loop and outside it was rendered wrong.** `src.Feedback(loop => loop * 0) + src` should be the source twice, and was the source followed by gaps, because a sound used in several places assumed that every place asked for the same number of frames at the same time. `Feedback` asks its input in chunks of its own. Every user of a shared sound now reads it from its first frame, in order, whatever sizes it asks for; the same holds for a custom operator that pulls its input in pieces. A sound used once costs nothing extra, and one used several times keeps a history of four blocks.
- **`ToString` on a sound could run a host out of memory.** A description contained the whole text of the sound it wrapped, so a sound used twice doubled its text: `s = s + s` twenty times was 14.7 million characters, and a few more lines were gigabytes. A description is now written when it is asked for, and a sound made of itself many times over is cut short with `...` after 200 sounds. Ordinary descriptions are unchanged.
- **Descriptions followed the machine's culture.** `Sine(0.25).Range(0.5, 2000.5)` described itself as `Range(0,5, 2000,5)` under a culture that writes a comma for the decimal point. They are now the same everywhere.
- **`Slot.Swap` held the slot's lock while it compiled**, so asking a slot what it plays (`Slot.Current`) waited for a long compile, such as a ten second impulse response. Swaps still happen one at a time, in order; reading the slot no longer waits.
- **Mini-notation: a weight, an underscore or a repeat inside `<>` was ignored.** `<c3@3 e3>` and `<c3 _ _ e3>` now hold `c3` for three cycles, as in Strudel; they alternated one cycle each before, so a script that used them plays differently now. A weight inside `<>` that is not a whole number (`<a@1.5 b>`) is now an error that points at the step, where it used to be accepted and ignored.

### Documentation

- **Mini-notation:** the weight rule for `<>` and a list of what differs from Strudel.
- **Custom operators:** the rule for pulling an input in pieces.
- **REPL:** new chord, arpeggio and "Twang" examples; the organ example is now **Church organ (custom operator)**. The examples menu is reordered with the fuller pieces first, starting with **Phaser sweep**. The plain filter sweep, the cabinet impulse response and a few one-line demos (pulse width, ring modulation, wind, plucked string, pitch from a function, notes by name, the generated sampler) are gone, since others cover them better.

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
- **Render from an offset.** `Render` and both `RenderToWav` overloads take a trailing `startSeconds` (default 0). The skipped part is still run and discarded, so oscillators, envelopes, delay lines and filters are in the state they would have reached by then, and the result matches that stretch of a longer render from zero.
- **Limits that stop an absurd request, and nothing more.** A script that asks for a delay line or a room of gigabytes, or for a tail that never dies, gets an `ArgumentOutOfRangeException` when the call is made. `Delay` and `TapeDelay` take a `maxTime` above 0 and up to 60 seconds (23 MB at 48 kHz), `Reverb` a `size` up to 100 (about 6 MB), and `Reverb` and `FdnReverb` a `decay` up to 120 seconds; `NaN` and infinity are refused as well. A delay of a minute, a hall a hundred times a medium room and a two minute tail all work. `FdnReverb` keeps its size limit of 4, and a host can set stricter limits for untrusted code (the Workbench's Graph mode allows 10).

### Sigil.Sound.Notes

- **Per-note expression.** `NoteInput.Start(pitch, velocity)` starts a note that is its own object, a `PlayingNote` with `Bend` (semitones), `Pressure` and `Slide` (0 to 1) that can be changed from any thread while it sounds, and `Release()`. Two notes at one pitch are independent, which is how an MPE controller plays. A voice reads the gesture as `v.Bend`, `v.Pressure` and `v.Slide` (each glides over about 10 ms), and `v.Frequency` and `v.Pitch` already include the bend, so existing sounds answer to a bending player unchanged. Notes started with `NoteOn` are unaffected. The library has no MIDI vocabulary: a host maps its own gestures onto the three numbers. See [Live notes and expression](docs/articles/live-notes.md).
- **Host sequence.** A pattern played without a tempo follows the host's clock, and `HostSequence.Observe` lets a host learn, while a script builds its sound, that it contains such a sequence, so it can offer play, pause, stop and a tempo for it. The observer is per thread and costs nothing when unset. Together with `Control` and `NoteInput` it is the host-integration surface; see [Hosting](docs/articles/hosting.md).
- Notes and pitch parsing (`Notes.Parse`), scales and chords, exact rational pattern time (`Fraction`), the pattern algebra, mini-notation (`Mini`), sequencing (`Every`, `Loop`, `Beats`), live note input (`NoteInput`), and allocation-free polyphonic playback (`Play`).
- **Note streams: live notes as data.** `NoteInput.Stream` is a `NoteStream`, the notes of an input as they happen, each with an identity that every operator keeps, so a note always ends however it was changed. Shape it with functions of your own (`Select`, `Where`, `SelectMany`, `Scan`, `Fold`; `NoteEvent` has `Transpose`, `Detune`, `Snap`, `InScale` and `Chance` for them) and with the operators that wait, remember or start several notes (`Delay`, `Echo`, `Strum`, `Length`, `Humanize`, `Mono`, `Latch`, `Harmonize`, `Merge` or `a + b`), then `Play` it, read it per block with `Compile()` and a `NoteStreamRunner`, or turn it into a pattern. `pattern.ToStream()` goes the other way. See [Note streams](docs/articles/note-streams.md).
- **Write against the chord that arrives.** `stream.Chord(settle, latch, switchOn)` is the played chord as a `Pattern<Chord>`, with the AutoArp device's choices: notes within 35 ms are one chord, the last chord is kept or not (`ChordLatch`), and a new chord takes over at once, on the beat or at the bar line (`ChordSwitch`). New steps of a chord: `chords.Steps(Mini.Integers("7 6 5 4"))`, `Chord.Steps(...)`, and `Lowest()` and `Highest()`. `Scale.Contains(pitch)` asks whether a pitch is a tone of a scale. A pattern that follows live notes cannot be written out with `ToPattern`, which throws for it.
- **Notes off their pitch.** `NoteInput.NoteOn(pitch, velocity, cents)` takes a detune of up to a semitone (a note from another tuning), which sounds in `Play` and travels through streams. `NoteInput.Clock` gives streams their tempo.
- **Notes by name on a live input.** `NoteInput.NoteOn(string)` and `NoteOff(string)` take mini-notation (`keys.NoteOn("a2 g3 e4:0.6")`, `"[c3,e3,g3]"`): every note in the text starts or ends at once, with the velocity from `:0.6`. Invalid text throws `MiniNotationException` before any note starts. The numeric overloads are unchanged.
- **More places take note names.** `Scale.Notes(string)` and `Scale.Degrees(string)` read degrees as mini-notation (`Scale.Parse("c3 minor").Notes("0 2 <4 7>")`). `NoteInput.Start(string)` returns one `PlayingNote` per name, and `NoteInput.IsHeld(string)` asks about several at once. `Notes.Parse` now reads names as `Mini.Notes` does: a name without an octave is in octave 3 and `:0.6` sets the velocity.
- **Drums by name.** `Mini.Drums("bd ~ sn ~")` gives notes on the General MIDI drum map (`bd`, `sn`, `hh`, `oh`, `cp`, `rim`, toms, `cr`, `rd`, `cb`).

### Sigil.Sound.Testing

- `SignalAnalysis` (peak, RMS, frequency, spectrum) and `AudioAssert` for tolerance-based assertions on rendered audio.

### Sigil.Sound.Repl.Core

- The snippet compiler the browser REPL is built on, now a package for apps that let their users run Sigil.Sound scripts. `SnippetCompiler` compiles a C# snippet in memory with Roslyn and returns the `Sound` it describes. A security validator runs first, and a `SnippetProfile` lets the host add its own API and its own rules on top of the built-in ones (no files, network, reflection or processes; no preprocessor directives; loops and recursion are counted). `EditorServices` answers completion, signature and diagnostic questions for a code editor. This is a best-effort filter for code you did not write, not a security boundary: a host that runs code from strangers should still isolate it. Its public API is tracked in `PublicAPI.Shipped.txt` like the other packages, but it is pre-1.0 and may change in a minor release.

### Documentation and tooling

- A showcase in the REPL, **Church organ**: 64 sine partials in eight ranks, kept in tune by rotating phasors, as a single custom operator.
- A DocFX site with a guide for each area and an **Open in REPL** link under every example, and a browser REPL (`apps/Sigil.Sound.Repl`) that compiles C# snippets and streams to Web Audio.
- Every public member has an XML comment (`CS1591` is an error), every example in the API docs and the guides compiles and sounds (checked by `DocExampleTests`), and the public API is tracked in `PublicAPI.Shipped.txt` so a change to it is a build error.
- **REPL: note streams, and a Notes out strip.** New starter snippets: "Note streams" and "A tune through a stream" (plain mode), "Note streams (Keys)" and "Follow the chord (Keys)" (the page keyboard), and four Sigilgraph ones ("Notes out: a tune", "harmonizer", "follow the chord", "lock to a scale"). In Sigilgraph mode a snippet that calls `Out.Notes` gets a **Notes out** strip beside the oscilloscope: time runs right to left under a playhead, one row per semitone, drawn as the notes sound. The page's live notes now run on a clock that the engine advances with the audio it renders, so an echo, a chord boundary and a pattern played as notes keep one time, and a pattern sent with `Out.Notes` sits on that clock as it sits on the device's transport.

### Versioning

What the numbers promise, including what a patch may and may not change about the sound, is in [Versioning](docs/articles/versioning.md).

### Known limits

- Four method groups, `Adsr`, `Sample`, `RenderToWav` and `Control.Map`, have several overloads with optional parameters (analyzer `RS0026`). They are visible as warnings and are to be settled before 1.0.
- `Sigil.Sound.SoundFont` and `Sigil.Sound.Playback` (real-time audio drivers) are planned, not part of this release.
