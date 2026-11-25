# Integrations

Sigil.Sound is a library: it describes sound and renders it, and an app decides when it plays, who turns the knobs and where the notes come from. This page shows it running in the places it already runs, and what it takes to put it in your own.

## Sound Script in Sigilgraph Audio Workbench

[Sigilgraph Audio Workbench](https://sigilaud.io) is a modular audio app. Its **Sound Script** device is a box on the patch whose face is a Sigil.Sound script: you type a few lines, the device compiles them and they play, with no restart. The device has every kind of port (audio, notes and control signals, in and out) and a faceplate of knobs and sliders, so one script can be an effect, a synth voice, an LFO, a note generator or several of those at once.

It is built on exactly the pieces described in [Hosting](hosting.md):

| In the script | In the device |
| --- | --- |
| `Knob("Cutoff", ...)` | a `Control`, drawn as a knob on the faceplate |
| `In.Notes`, `Poly(...)` | a `NoteInput` fed from the device's note port |
| a pattern without a tempo | a `HostClock` that follows the Workbench transport, so the script plays in bar with the rest of the patch |
| editing the script | a `Slot` swap: the new script fades in over the old one while the patch keeps playing |

The device's own guide lives with the Workbench. A script written there also runs in the browser REPL's Sigilgraph mode, below.

## The browser REPL

The [REPL](https://sound.sigilaud.io/repl/) compiles C# snippets in the browser and plays them through Web Audio. Every example on this site has an **Open in REPL** link. Press Ctrl+Enter to run what you typed; the new sound crossfades in over the old one, so you can edit while it plays. A pattern without its own tempo follows the page's clock, so an edit made while it plays carries on from the same beat; Stop, then Run, starts from the top.

Its **Sigilgraph** mode adds a simulation of the Sound Script device's API (`Knob`, `Slider`, `In`, `Out`, `Poly`), so a device script can be tried, and shared, without the app.

## Your own app

Three pieces carry everything between a script and a host: `Control` for values the host changes, `NoteInput` for notes, `HostClock` for time. `SoundContext` is the output, and every `Play` is a hot swap. A complete live host is a sound card callback and a few lines:

```csharp
using Sigil.Sound;
using static Sigil.Sound.Sounds;

var cutoff = new Control(1200, 200, 8000, name: "Cutoff");
using var output = new SoundContext(sampleRate: 48_000);
output.Play(Saw(110).LowPass(cutoff, q: 3) * 0.3);

// In the audio callback, on the audio thread:
//     output.Render(frames);
// From the UI, at any time:
//     cutoff.Value = 3000;
//     output.Play(Square(110).LowPass(cutoff) * 0.3);   // crossfades, no click
```

The repository has a working example of this in `samples/LiveScript`: a Windows console app that plays a script file through NAudio and swaps it in every time you save the file, with the computer keyboard as a `NoteInput`. It uses `Sigil.Sound.Repl.Core` to compile the file, which is how an app lets its users write scripts of their own (see [Hosting](hosting.md) and the package's notes on what the validator does and does not protect against).

## Rendering offline

The same sounds render to memory or to a WAV file with no audio device, which is how stems, sound effects for a game, and tests are made:

```csharp
using Sigil.Sound;
using Sigil.Sound.Testing;
using static Sigil.Sound.Sounds;

var hit = (Noise() * Fn(t => Math.Exp(-t * 30))).BandPass(1800, q: 0.8);
hit.Reverb(mix: 0.3).RenderToWav("snare.wav", seconds: 1.5);

// The same render, checked in a test.
var audio = hit.Render(1.5);
AudioAssert.IsFinite(audio);
AudioAssert.PeakNear(audio, 0.5f, 0.5f);
```

See [Rendering and live playback](rendering-and-live.md) for the render options and `Sigil.Sound.Testing` for the assertions.
