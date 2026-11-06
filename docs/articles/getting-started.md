# Getting started

The quickest way in is the browser REPL: every example in the [API reference](../api/Sigil.Sound.yml) has an **Open in REPL** link that loads it ready to play. Press Ctrl+Enter to play and edit anything; the new sound crossfades in.

```csharp
// A sound used as a parameter: a slow sine sweeps the filter cutoff.
var sweep = Sine(0.25).Range(200, 2000);

Saw(110).LowPass(cutoff: sweep, q: 4) * 0.4
```

Parameters accept a number, another `Sound` (audio-rate modulation), or a function of time, so the same idea reaches frequency, cutoff, gain and mix.

## In your own project

```csharp
using static Sigil.Sound.Sounds;

var mix = (Saw(110).LowPass(800) * 0.4).Limit();
mix.RenderToWav("demo.wav", seconds: 4, sampleRate: 48_000);
```
