# A whole piece: Night drive

The other guides show one idea at a time. This one builds a short groove from start to finish, a part at a time, so you can hear how the pieces fit: drums, a bass, a pad and a lead on four chords (A minor, G, F, E minor) at 96 beats a minute. It is also one of the REPL's examples: pick Night drive in the menu.

Every part below plays on its own. Open it in the REPL, change it, and press Ctrl+Enter to hear the change.

## Kick

A kick is a sine that starts high and drops fast. `Fn` gives each hit its own pitch curve, because inside a voice `t` starts at zero on every note. A little brown noise, which is mostly lows, adds weight to the attack.

```csharp
var kick = Mini.Drums("bd ~ bd ~").Play(v =>
    (Sine(48 + 110 * (1 - Fn(t => Math.Min(1, t * 30)))) + BrownNoise().LowPass(300) * 0.6)
        .Adsr(0.001, 0.28, 0, 0.05), bpm: 96);
kick * 0.9
```

## Snare, through a gated reverb

A snare is a burst of band-passed noise with a little tone under it. Here it goes through a large room, and a `Gate` cuts the room off as soon as it falls below -26 dB: the big-but-short sound called gated reverb.

```csharp
var snare = Mini.Drums("~ sn ~ sn").Play(v =>
    (PinkNoise().BandPass(1800, q: 0.7) + Sine(185) * 0.5).Adsr(0.001, 0.12, 0, 0.05), bpm: 96);
snare.Reverb(size: 1.5, decay: 2, mix: 0.45).Gate(-26, release: 0.02) * 0.5
```

## Hats, swung

The hats play the off-beats, swung. In mini-notation `[x@2 y]` gives the first half of a beat twice the time of the second, so the off-beat lands late. The level is a pattern of numbers, and `Slew` turns each step from 0 to 1 and back into a short tick: a fast rise, a 25 ms fall.

The brightness comes from subtraction. The noise minus its own low-passed copy leaves only what is above 6 kHz, which is a high-pass built from `-`.

```csharp
var noise = PinkNoise(seed: 4);
var ticks = Mini.Numbers("[0@2 [1 0]]*4").ToSignal(bpm: 96).Slew(rise: 0.001, fall: 0.025);
(noise - noise.LowPass(6000)) * ticks * 0.5
```

## Bass

A funk bass plays little but places it well: the root on the one, a soft ghost note (`:0.4` is the velocity) and a jump up an octave, both on swung off-beats. Each bar follows the chord, and the last bar leads back to A through G sharp.

The filter's cutoff is a step from 1600 Hz down to 400 Hz, 20 ms into each note. `Slew` turns that step into a glide, so every note opens bright and closes fast: a quick wah.

```csharp
var bass = Mini.Notes(
        "<[a1 ~ [~@2 a1:0.4] [~@2 a2]] [g1 ~ [~@2 g1:0.4] [~@2 g2]] [f1 ~ [~@2 f1:0.4] [~@2 f2]] [e1 ~ [~@2 e1:0.4] [~@2 g#1]]>")
    .Play(v => Saw(v.Frequency)
        .LowPass(Fn(t => t < 0.02 ? 1600 : 400).Slew(0.04), q: 4)
        .Adsr(0.001, 0.15, 0.2, 0.04), bpm: 96, polyphony: 2);
bass * 0.35
```

## Pad, made by subtraction

One saw minus another that is a hair sharper is a pulse wave whose width turns slowly as the two drift apart. That is the hollow, moving sound of pulse-width modulation, here with no LFO at all. A low-pass and a slow envelope keep it in the background.

```csharp
var pad = Mini.Notes("<[a2,c3,e3] [g2,b2,d3] [f2,a2,c3] [e2,g2,b2]>")
    .Play(v => (Saw(v.Frequency) - Saw(v.Frequency * 1.004)).LowPass(1400).Adsr(0.4, 0.5, 0.8, 1), bpm: 96, polyphony: 6);
pad * 0.1
```

## Lead

Two notes a bar, from the top of each chord. The sound is a narrow pulse: at a 12% duty cycle it spends most of its time at -1, so it averages well below zero, and `DcBlock` re-centres it. The filter jumps to a new random cutoff on every beat (`Hold` on noise) and glides there (`Slew`), so each note is a little different. `Mix` fades the lead into its own echo and back, slowly.

```csharp
var cutoff = Noise(seed: 5).Hold(96 / 60.0).Slew(0.08).Range(900, 3200);
var lead = Mini.Notes("<[~ e4 ~ c4] [~ d4 ~ b3] [~ c4 ~ a3] [~ b3 ~ g#3]>")
    .Play(v => Square(v.Frequency, width: 0.12).DcBlock().LowPass(cutoff, q: 2).Adsr(0.003, 0.25, 0.2, 0.2), bpm: 96)
    * 0.22;
lead.Mix(lead.Delay(60.0 / 96 * 0.75, feedback: 0.35, mix: 1), Sine(0.1).Range(0.15, 0.5))
```

## Putting it together

The punch comes from one more step: the kick drives a compressor on the pad and the bass (a sidechain), so they dip on every hit and swell back in between. There is no reverb on the whole mix, which keeps the drums dry and close, and a `Limit` at the end keeps the sum under full scale.

This is the whole piece, as it is in the REPL:

```csharp
// A sparse groove on four chords (A minor, G, F, E minor), built with the newer tools: subtraction, Mix, Slew,
// Hold, Gate, DcBlock, and pink and brown noise. Each part says which one it uses.
var bpm = 96;
var beat = 60.0 / bpm;

// Kick on 1 and 3: a sine that drops in pitch, with a puff of brown noise for weight.
var kick = Mini.Drums("bd ~ bd ~").Play(v =>
    (Sine(48 + 110 * (1 - Fn(t => Math.Min(1, t * 30)))) + BrownNoise().LowPass(300) * 0.6)
        .Adsr(0.001, 0.28, 0, 0.05), bpm);

// Snare on 2 and 4, through a big room that a gate chops off short: a gated reverb.
var snare = Mini.Drums("~ sn ~ sn").Play(v =>
    (PinkNoise().BandPass(1800, q: 0.7) + Sine(185) * 0.5).Adsr(0.001, 0.12, 0, 0.05), bpm);
var gatedSnare = snare.Reverb(size: 1.5, decay: 2, mix: 0.45).Gate(-26, release: 0.02);

// Hats on the swung offbeats ("[x@2 y]" makes the second half of each beat land late). The noise is made bright
// by subtraction, the noise minus its own low-passed copy, and Slew turns each on/off step into a short tick.
var noise = PinkNoise(seed: 4);
var ticks = Mini.Numbers("[0@2 [1 0]]*4").ToSignal(bpm: bpm).Slew(rise: 0.001, fall: 0.025);
var hats = (noise - noise.LowPass(6000)) * ticks * 0.5;

// A funk bass: the root on the one, a ghost note (:0.4) and an octave pop, swung like the hats. Slew on the cutoff
// opens it on each attack and closes it over 40 ms, a quick wah.
var bass = Mini.Notes(
        "<[a1 ~ [~@2 a1:0.4] [~@2 a2]] [g1 ~ [~@2 g1:0.4] [~@2 g2]] [f1 ~ [~@2 f1:0.4] [~@2 f2]] [e1 ~ [~@2 e1:0.4] [~@2 g#1]]>")
    .Play(v => Saw(v.Frequency)
        .LowPass(Fn(t => t < 0.02 ? 1600 : 400).Slew(0.04), q: 4)
        .Adsr(0.001, 0.15, 0.2, 0.04), bpm, polyphony: 2)
    * 0.35;

// A pad made by subtraction: one saw minus another a hair sharper is a pulse whose width slowly turns,
// the hollow sound of pulse-width modulation, with no LFO.
var pad = Mini.Notes("<[a2,c3,e3] [g2,b2,d3] [f2,a2,c3] [e2,g2,b2]>")
    .Play(v => (Saw(v.Frequency) - Saw(v.Frequency * 1.004)).LowPass(1400).Adsr(0.4, 0.5, 0.8, 1), bpm, polyphony: 6)
    * 0.06;

// Two notes of lead per bar. A 12% pulse sits far below zero on average, so DcBlock re-centres it. Its cutoff
// jumps to a new random place on every beat (Hold) and glides there (Slew), and Mix fades it into its echo and back.
var cutoff = Noise(seed: 5).Hold(bpm / 60.0).Slew(0.08).Range(900, 3200);
var lead = Mini.Notes("<[~ e4 ~ c4] [~ d4 ~ b3] [~ c4 ~ a3] [~ b3 ~ g#3]>")
    .Play(v => Square(v.Frequency, width: 0.12).DcBlock().LowPass(cutoff, q: 2).Adsr(0.003, 0.25, 0.2, 0.2), bpm)
    * 0.22;
var echoed = lead.Mix(lead.Delay(beat * 0.75, feedback: 0.35, mix: 1), Sine(0.1).Range(0.15, 0.5));

// The kick pushes the pad and the bass down on every hit (a sidechain), which is where the punch comes from.
var bed = (pad + bass).Compress(thresholdDb: -24, ratio: 6, attack: 0.002, release: 0.15, sidechain: kick);

(kick * 0.9 + gatedSnare * 0.5 + hats + bed + echoed).Limit()
```

## Things to try

- **Change the groove.** Take the swing out of the hats (`"[0 [1 0]]*4"`) and hear how much of the funk it carried.
- **Turn the pump up or down.** A `ratio` of 2 on the sidechain is gentle; 12 is a hard duck.
- **Open the gate.** `Gate(-60)` lets the whole room ring, which is the reverb the gate was cutting.
- **Swap the pad's minus for a plus.** `Saw(f) + Saw(f * 1.004)` is the familiar chorused saw pad; the minus is what makes it hollow.
