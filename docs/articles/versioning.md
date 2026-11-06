# Versioning

Sigil.Sound versions look like `MAJOR.MINOR.PATCH`, as in [Semantic Versioning](https://semver.org/), with a few promises that are specific to an audio library. This page says what each number means, what you can rely on today, and what 1.0 will add.

## What the numbers mean

| Part | Meaning | Example |
| --- | --- | --- |
| **MAJOR** | `0` is beta: the API is shaped by use and may change between minor versions. `1` and above is a promise that the API and the sound will not break within that major version. | `0.x` is beta, `1.0.0` is the first stable release |
| **MINOR** | Features: new operators, new parameters, new things in the packages. While MAJOR is `0` it is also where breaking changes and retuned defaults can happen, and each one is listed in the changelog. From 1.0 a minor release only adds. | `0.2.0` adds operators; `1.3.0` adds operators without breaking `1.2.0` code |
| **PATCH** | Fixes only: bugs, performance that leaves the output as it was, documentation. No change to the API and no intentional change to the sound, other than fixing a defect. Always safe to take. | `0.1.1` fixes a click in a filter |

A suffix such as `-rc.1` or `-beta.2` marks a candidate for a release. It may still change before the release, and nuget.org hides it from searches unless you ask for pre-releases.

All the packages (`Sigil.Sound`, `Sigil.Sound.Notes`, `Sigil.Sound.Testing`) share one version number and are released together. A package depends on exactly the matching version of `Sigil.Sound`, so they cannot drift apart.

## What 0.1.0 means

The first public release. It is complete enough to build real instruments and effects: sources, filters, effects, dynamics, convolution, notes and patterns, and a browser REPL to try it all. It is also the start of the feedback phase: the public API is tracked, but it may still change before 1.0, and your use of it is how we decide what changes.

## What counts as the public API

Everything listed in each package's `PublicAPI.Shipped.txt`. The build checks it, so a change to the public API cannot happen by accident. It does not include internals, the REPL application, the documentation site or the test helpers' internals.

## What counts as the sound

A sound is part of the contract, not only the code that makes it. Given the same graph, a version renders the same audio, to within floating-point tolerance. We do not promise bit-identical output across versions, hardware or .NET versions, because vector widths and fused multiply-adds differ between machines.

- **Patch:** the sound does not change, except to fix a defect (a click, a wrong pitch, a NaN).
- **Minor, before 1.0:** a default, a curve or an algorithm may be retuned if we find a better one. It is listed in the changelog under **Changed**.
- **Minor, from 1.0:** existing sounds do not change. A better algorithm arrives as a new method or an option, and the old behaviour stays the default.

The [defaults](defaults.md) are part of the sound. Changing one counts as changing the sound.

## Removing things

- **Before 1.0:** a method may be removed or renamed in a minor release. It is listed in the changelog under **Removed** or **Changed**, and where it is cheap we mark the old name `[Obsolete]` one release earlier.
- **From 1.0:** a member is first marked `[Obsolete]` in a minor release, with a message that names its replacement, and is removed no sooner than the next major release.
- Dropping a target framework (.NET 8, for instance) is a breaking change and follows the same rule.

## Depending on it

While the major version is `0`, take patches automatically and move between minors on purpose:

```xml
<PackageReference Include="Sigil.Sound" Version="[0.1.0,0.2.0)" />
```

From 1.0, a floating version is safe within the major version:

```xml
<PackageReference Include="Sigil.Sound" Version="1.*" />
```

## What 1.0 needs

Version 1.0 is a promise, so we make it only when we can keep it. Our checklist:

- The overload sets with several optional parameters (`Adsr`, `Sample`, `RenderToWav` and `Control.Map`) are settled, because adding a parameter to one later would be a breaking change.
- The defaults have been used by people other than us for long enough that we are not going to retune them.
- No known correctness bug is open, and the performance and memory characteristics of every operator are written down.

The [changelog](https://github.com/sigilllc/SigilSound/blob/HEAD/CHANGELOG.md) lists every release, and what changed in it.
