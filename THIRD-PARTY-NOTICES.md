# Third-party notices

Sigil.Sound itself is MIT licensed (see [LICENSE](LICENSE)). This page lists the material from other people that the project
bundles, loads or depends on, with where it comes from and under what terms. Nothing here needs to be installed by hand.

## Audio bundled with the online REPL

These two files ship with the REPL web app only (`apps/Sigil.Sound.Repl/Host/BuiltIn/`). They are not part of any NuGet package.

| File | What it is | Source and terms |
| --- | --- | --- |
| `pluck.wav` | A plucked C3 made by this project: eight computed harmonics and a little noise for the pick. | Created by the Sigil.Sound authors. Covered by this repository's MIT licence. |
| `IR_Speakers_Living_Room.wav` | An impulse response of a living room, used by the "Room from an IR" example. | By **mansardian**, <https://freesound.org/s/172677/>, licensed **Creative Commons 0** (public domain dedication). Attribution is not required and is given here with thanks. |

## Fonts, served from this site

The REPL and the docs site serve their own copies of these fonts (Latin subset, variable weight, `woff2`) from the `fonts` folders next to their style sheets, so no page request goes to Google. The licence text of each is in the same folder.

| Font | Used for | Terms |
| --- | --- | --- |
| [Inter](https://github.com/rsms/inter) | Docs body text | SIL Open Font License 1.1 |
| [Montserrat](https://github.com/JulietaUla/Montserrat) | Headings and page typography | SIL Open Font License 1.1 |
| [Roboto Mono](https://github.com/googlefonts/robotomono) | Code and numbers | SIL Open Font License 1.1 |

The files are the ones Google Fonts serves, checked against the licence files in the `google/fonts` repository on 2026-11-10. Older releases of Roboto Mono were Apache 2.0; the current variable font is OFL 1.1, and that is the version used here.

## Loaded by the online REPL at run time

The REPL page fetches this from a public CDN when it opens. It is not copied into this repository or into the packages.

| Component | Used for | Terms |
| --- | --- | --- |
| [Monaco Editor](https://github.com/microsoft/monaco-editor) 0.52.2, from cdn.jsdelivr.net | The code editor | MIT |

The terms are those the upstream project publishes; they were not re-checked when this file was written.


## NuGet dependencies

Declared licences read from the packages themselves (their `.nuspec`).

| Package | Version | Where it is used | Licence |
| --- | --- | --- | --- |
| Microsoft.CodeAnalysis.CSharp (Roslyn) and Microsoft.CodeAnalysis.Common | 5.0.0 | `Sigil.Sound.Repl.Core` and the REPL: compiles snippets | MIT |
| Microsoft.AspNetCore.Components.WebAssembly, the .NET runtime and its Microsoft.Extensions.* and Microsoft.JSInterop packages | 10.0.x | The REPL (Blazor WebAssembly) | MIT |
| Microsoft.CodeAnalysis.PublicApiAnalyzers, Microsoft.CodeAnalysis.Analyzers | 3.3.4, 3.11.0 | Build time only (public API tracking) | MIT |
| xunit, xunit.runner.visualstudio | 2.9.3, 3.1.4 | Tests only | Apache 2.0 |
| Microsoft.NET.Test.Sdk, coverlet.collector | 17.14.1, 6.0.4 | Tests only | MIT |

`Sigil.Sound` and `Sigil.Sound.Notes` have no third-party package dependencies (`Sigil.Sound.Notes` depends on `Sigil.Sound`).

## Trademarks

The names "Sigil", "Sigil.Sound" and "AudioWorkbench" are covered in the [README](README.md#trademarks).
