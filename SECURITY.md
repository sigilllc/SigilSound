# Security policy

## Reporting a vulnerability

Please report privately by email to **security@sigilaud.io**, and do not open a public issue. Include what you found, the smallest snippet or steps that show it, and the version. We aim to acknowledge a report within a week. Please give us **90 days** to fix it before you publish details. We credit reporters in the changelog unless they prefer not to be named.

Only the latest version is supported. Sigil.Sound is pre-1.0, so a fix may arrive as a new minor version.

## What counts

`Sigil.Sound.Repl.Core` compiles C# written by someone else and checks it against a list of what is allowed before it runs, so a host can offer scripting without giving the script the machine.

**In scope:**
- A snippet that gets past the validator and reaches files, the network, processes, native memory, reflection or other parts of the host that the policy blocks, including by naming a blocked member indirectly (as a delegate, through an alias, or through a language feature that hides the call).
- A flaw in the build or release that could put malicious code into a published package.

**Out of scope** (report these as ordinary issues):
- A snippet that uses a lot of CPU or memory, loops forever, or crashes the page or host. The execution guards are a best effort. A host that runs scripts it does not trust should isolate them and set its own limits.
- Anything that needs the attacker to already control the host application, the machine or the browser.
- Problems in third-party software we depend on (Roslyn, .NET, Blazor); please report those upstream.

The validator is a strong filter, not a proof, and a determined attacker may find a route we missed. That is why reports are welcome, and why a host should treat it as one layer of defence.
