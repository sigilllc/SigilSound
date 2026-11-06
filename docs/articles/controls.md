# Controls: how a host hands your sound a knob

A `Control` is a value that something outside the sound changes while it plays: a game's engine RPM, a plugin host's cutoff knob, a slider on a web page. It is also the place a host says how that value should look and feel to a person. That makes it the one seam between a sound and whatever app is controlling it. The library knows controls; it never knows faceplates, ports or screens.

## The basics

```csharp
// Engine RPM follows a control. The game sets rpm.Value every frame; the sound glides to it over 30 ms.
var rpm = new Control(800, min: 600, max: 7000, slewSeconds: 0.03);
Saw(rpm / 30).LowPass(rpm.Map(200, 4000, ControlCurve.Exponential)) * 0.3
```

`Value` is safe to set from any thread. A jump is never heard as a click: the audio side follows it with a glide, set by `slewSeconds`.

## What a host adds

A host that shows controls to a person (a UI) wants more than a number. These are settings on the control, all optional, and none of them changes how the sound is computed:

| Setting | Question it answers | Values |
| --- | --- | --- |
| `Taper` | How does a turn of the knob map to the value? | `Linear`, `Log` (equal ratios, for pitch and cutoff), `Exp` (slow at the bottom, fast at the top) |
| `Polarity` | What does a signal that drives this control span? | `Unipolar` (0 to 1), `Bipolar` (-1 to 1) |
| `Style` | Which widget suits it? | `Knob`, `Slider`, `Selector` |
| `Options` | For a selector, what are the choices? | the names, in order; the value is the index |

```csharp
// A cutoff knob. The UI reports a position from 0 to 1 and the control applies the taper: half way is 1000 Hz.
var cutoff = new Control(100, min: 100, max: 10000) { Taper = ControlTaper.Log };
cutoff.Travel = 0.5;
Saw(110).LowPass(cutoff) * 0.4
```

- **Travel** is the helper a UI uses. `control.Travel = 0.37` sets the value through the taper, and reading it gives the knob position back, so the UI never has to know whether the control is linear, logarithmic or squared. The sound still glides.
- **SetFromSignal** is for a value that arrives from outside, such as a modulator the host wired in. The signal is read through the polarity (0 to 1, or -1 to 1) and then the taper, and it stops at the ends of the range.
- **Choice** makes a selector: `Control.Choice("Wave", ["Sine", "Saw", "Square"])`. The value is the index, it only takes whole numbers, and it changes at once instead of gliding.

`Taper` is not the same thing as `ControlCurve`. A taper describes the hand (how a knob's travel maps to the control's value) and only matters to a UI. A `ControlCurve` is how `Map` spreads the control's value over another range, such as 200 to 4000 Hz, and it changes the sound.

## Binding your own app

Whatever you control sounds with, the steps are the same:

1. Create a `Control` for each thing the player can change, with a name, a range and the settings above.
2. Put it in the sound wherever a number goes, or use `Map` for a different range.
3. Show it however your app shows controls, using `Name`, `Min`, `Max`, `Style`, `Taper` and `Options`.
4. When the player moves it, set `Travel` (from a UI) or `Value` (from code), on any thread.

The Sigilgraph Audio Workbench does exactly this: the `Knob`, `Slider` and sample pills of its Sound Script device are controls it shows on a faceplate.
