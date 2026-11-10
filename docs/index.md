---
layout: landing
title: Sigil.Sound - sound synthesis for your .NET app
description: A fluent API for sound synthesis in your .NET app. Build an instrument or an effect in a few lines, then record it to a file or play it live. Examples play in your browser.
_disableToc: true
_disableAffix: true
_disableBreadcrumb: true
_disableNextArticle: true
_disableContribution: true
---

<!-- The landing page. Raw HTML on purpose: docfx does not parse markdown inside an HTML block. Styled by body[data-layout="landing"]
     in templates/sigil/public/main.css. The code sample is the one in articles/getting-started.md, which DocExampleTests runs;
     this file is not scanned, so keep the two identical. -->

<div class="lp">

<section class="lp-hero" aria-label="Overview">
  <div class="lp-copy">
    <h1 class="lp-title">Sound synthesis for your .NET app</h1>
    <p class="lp-sub">A fluent API for sound synthesis in your .NET app. Build an instrument or an effect in a few lines, then record it to a file or play it live.</p>
    <div class="lp-cta">
      <a class="lp-btn lp-btn-primary" href="articles/getting-started.md">Get started <span aria-hidden="true">&rarr;</span></a>
      <a class="lp-btn lp-btn-ghost" href="https://sound.sigilaud.io/repl/" data-repl-home target="_blank" rel="noopener">Try it in the REPL</a>
    </div>
    <div class="lp-links">
      <a class="lp-chip" href="https://www.nuget.org/packages/Sigil.Sound" rel="noopener">NuGet</a>
      <a class="lp-chip" href="https://github.com/sigilllc/SigilSound" rel="noopener">GitHub</a>
      <a class="lp-chip" href="api/Sigil.Sound.yml">API reference</a>
      <a class="lp-chip" href="articles/versioning.md">Versioning</a>
    </div>
  </div>

  <div class="lp-window">

```csharp
// A sound used as a parameter: a slow sine sweeps the filter cutoff.
var sweep = Sine(0.25).Range(200, 2000);

Saw(110).LowPass(cutoff: sweep, q: 4) * 0.4
```

  </div>
</section>

<section class="lp-install" aria-label="Install">
  <code>dotnet add package Sigil.Sound</code>
  <span class="lp-install-note">MIT licensed. Works on .NET 8 or later. Patterns and notes are in <code>Sigil.Sound.Notes</code>.</span>
</section>

<section class="lp-grid" aria-label="What it does">
  <a class="lp-card" href="articles/sources.md">
    <h2>Sources</h2>
    <p>Oscillators, noise, functions of time and samples. Every method works with no arguments.</p>
  </a>
  <a class="lp-card" href="articles/parameters.md">
    <h2>Modulation</h2>
    <p>A parameter takes a number, another sound or a function of time, so one idea reaches pitch, cutoff, gain and mix, including FM.</p>
  </a>
  <a class="lp-card" href="articles/effects.md">
    <h2>Effects</h2>
    <p>Filters, envelopes, delay, reverb, modulation, distortion, pitch and stereo.</p>
  </a>
  <a class="lp-card" href="articles/notes-and-patterns.md">
    <h2>Notes and patterns</h2>
    <p>Sequences, scales, chords and a compact mini-notation, played by any instrument you describe.</p>
  </a>
  <a class="lp-card" href="articles/rendering-and-live.md">
    <h2>Render or play live</h2>
    <p>Render to memory or WAV offline, or send the same sound to live output with slots you can swap while it plays.</p>
  </a>
  <a class="lp-card" href="articles/custom-operators.md">
    <h2>Extend it</h2>
    <p>Write your own <code>IOperator</code> and use it like a built-in.</p>
  </a>
</section>

<section class="lp-more" aria-label="Documentation">
  <h2>Documentation</h2>
  <div class="lp-more-cols">
    <div>
      <h3>Start here</h3>
      <ul>
        <li><a href="articles/getting-started.md">Getting started</a></li>
        <li><a href="articles/defaults.md">Defaults</a></li>
        <li><a href="articles/controls.md">Controls</a></li>
        <li><a href="articles/shaping.md">Shaping sound</a></li>
      </ul>
    </div>
    <div>
      <h3>Notes and playback</h3>
      <ul>
        <li><a href="articles/mini-notation.md">Mini-notation</a></li>
        <li><a href="articles/live-notes.md">Live notes and expression</a></li>
        <li><a href="articles/note-streams.md">Note streams</a></li>
        <li><a href="articles/samples.md">Samples</a></li>
        <li><a href="articles/rendering-and-live.md">Rendering and live playback</a></li>
        <li><a href="articles/hosting.md">Hosting</a></li>
      </ul>
    </div>
    <div>
      <h3>Reference</h3>
      <ul>
        <li><a href="api/Sigil.Sound.yml">API reference</a></li>
        <li><a href="articles/versioning.md">Versioning</a></li>
        <li><a href="https://github.com/sigilllc/SigilSound/blob/main/CONTRIBUTING.md" rel="noopener">Contributing</a></li>
      </ul>
    </div>
  </div>
</section>

</div>
