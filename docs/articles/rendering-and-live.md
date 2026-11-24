# Rendering and live playback

A sound is a description. To hear it or keep it, you render it. There are three ways, from simplest to most flexible.

## Render to memory

`Render(seconds, sampleRate)` produces an `AudioBuffer` you can inspect or pass along:

```csharp
using static Sigil.Sound.Sounds;

var buffer = (Saw(110).LowPass(900) * 0.4).Render(seconds: 4, sampleRate: 48_000);
```

Use it for tests, for analysis, or to build a longer piece from short parts.

## Render to a WAV file

`RenderToWav` writes the same audio to disk, as 16, 24-bit or 32-bit float WAV, to a path or a stream:

```csharp
using static Sigil.Sound.Sounds;

var mix = (Saw(110).LowPass(900) * 0.4).Limit();
mix.RenderToWav("demo.wav", seconds: 8, sampleRate: 48_000);
```

To capture a stretch from well into a sound, pass `startSeconds`. The skipped part is still run and thrown away, so oscillators, envelopes, delay lines and filters are in the state they would have reached by then, and the result matches that stretch of a longer render from zero. `Render` takes it too:

```csharp
using static Sigil.Sound.Sounds;

// Eight seconds of the mix, beginning two minutes in.
var mix = (Saw(110).LowPass(900) * 0.4).Limit();
mix.RenderToWav("demo-later.wav", seconds: 8, sampleRate: 48_000, startSeconds: 120);
```

Skipping costs the time of rendering the skipped part, since there is no way to jump a stateful sound ahead without running it.

The file-path overload is not allowed inside the REPL: it cannot write to a disk there.

## Live playback

`SoundContext` is a running output. You play a sound into it and replace it at any time. A replaced sound crossfades in over the old one.

```csharp
using static Sigil.Sound.Sounds;
using Sigil.Sound;

using var context = new SoundContext();
context.Play(Saw(110).LowPass(900) * 0.4, fadeSeconds: 0.5);

// Later, from the audio callback or a test: pull frames from the context.
// context.Render(span);

context.Stop(fadeSeconds: 1.0);
```

`Time` reports how many seconds have been rendered. A host calls `Render(span)` from its own audio callback.

## Swappable locations

A `Slot` marks a place in a graph where the sound can be changed later. Replacing it compiles the new sound off the audio thread and crossfades to it atomically.

```csharp
using static Sigil.Sound.Sounds;
using Sigil.Sound;

var slot = new Slot(Saw(110) * 0.4);
var patch = slot.Sound.Gain(0.8);   // use the slot anywhere a Sound goes

// Later, from any thread:
slot.Swap(Saw(165) * 0.4, fadeSeconds: 0.2);
```

Use a slot when a graph is built once but its contents should change while it runs, for example a live-coding host.

Swaps can follow each other as fast as you like. A swap made while an earlier one is still fading starts a new fade from wherever the sounds have got to, so nothing is cut off part of the way out, and pressing Run twice in quick succession, or Play just after Stop, does not click.

The crossfade is equal-power by default, which keeps the loudness steady when the old and the new sound are unrelated, as a freshly compiled sound usually is. A slot that carries a control signal (a filter sweep, a level) should fade linearly instead, because two versions of the same signal would rise by 3 dB halfway through an equal-power fade:

```csharp
using static Sigil.Sound.Sounds;
using Sigil.Sound;

var sweep = new Slot(Sine(0.25).Range(400, 2000)) { FadeCurve = FadeCurve.Linear };
var patch = Saw(110).LowPass(sweep.Sound) * 0.4;

sweep.Swap(Sine(0.5).Range(300, 3000));   // the cutoff moves to the new sweep without a bump
```

`SoundContext` has the same `FadeCurve` property, for an output that carries a control signal.
