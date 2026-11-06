# Writing doc examples

Every public member that does something audible carries at least one `<example>` in its XML doc comment. DocFX shows it under **Examples**, a script adds an **Open in REPL** link, and a test compiles and renders it. A doc example that does not sound is a failing test.

## The format

```xml
/// <example>
/// <code>
/// // Short comment saying what you will hear.
/// Saw(110).LowPass(Sine(0.25).Range(200, 2000), q: 4) * 0.4
/// </code>
/// </example>
```

## The rules

1. **One `<code>` is one complete REPL snippet.** It ends with an expression that makes a `Sound`, with no semicolon. Declare every variable it uses; `pad.Reverb()` with no `pad` is not an example.
2. **It makes sound by itself.** It renders at least a second of non-silent, finite audio at the default sample rate. A gated envelope example needs to hold its note or loop.
3. **Only what the REPL has.** No file paths. For samples use the built-in names with `Samples.Load("...")`, or build the audio in code.
4. **A comment says what to listen for.** One line, first.
5. **Several `<example>` elements are fine** when they show different ideas (a basic use, then a modulated one). Do not pad a member with near-copies.
6. **Project code starts with `using`.** An example that cannot run in the REPL because it reads a file, or sits in an audio callback or a game loop, begins with a `using` directive. It is shown in the docs, but it is not compiled, rendered or linked. Use this only when the REPL really cannot do it; most examples can.

## How it is enforced

`DocExampleTests` extracts every `<code>` from the generated XML docs, compiles it with the REPL's own compiler, renders one second and rejects errors, NaN and silence. Examples that do not yet meet the rules are listed in `tests/Sigil.Sound.Repl.Tests/DocExamples.baseline.txt`. The test fails when a listed example starts passing (remove it from the list) and when a new example fails (fix it). The list only shrinks, and for 0.1.0 it is empty: every example sounds.

## How the link works

The REPL loads `#code=<base64url of the UTF-8 snippet>` from its address, the same format its Share button writes. The docs script builds that address from the text of each example, so the link and the example can never differ.

Guides follow the same rule: any C# block that does not start with `using` or `///` gets the link.

## Guides

The same rule applies to the snippets in the guides. A C# block that starts with `using` is project code: it is shown, but not compiled or linked. Any other C# block is a REPL snippet: it gets an **Open in REPL** link, and the test compiles and renders it.

Write a block as a REPL snippet unless it needs a project. Use `using` at the top when it does.
