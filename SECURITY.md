# Security policy

## Reporting a vulnerability

Please report privately and do not open a public issue.

- **Preferred:** on GitHub, open the **Security** tab and choose **Report a vulnerability**. Only the maintainers see it.
- **Or email:** security@sigilaud.io

Include what you found, the smallest snippet or steps that show it, and the version (package version or the REPL's footer). We aim to acknowledge a report within a week, and we will tell you what we decide. Please give us **90 days** from your report to fix it before you publish details; we will credit you in the changelog unless you prefer not to be named.

## Supported versions

Only the **latest minor version** receives fixes (for example, once `0.2` exists, `0.1` does not). Sigil.Sound is pre-1.0, so the fix may arrive as a new minor version.

## What counts as a vulnerability

`Sigil.Sound.Repl.Core` compiles C# written by someone else and checks it against a list of what is allowed before it runs. That check exists so a host can offer a scripting feature without handing the script the machine. We consider these **in scope**:

- A snippet that gets past the validator and reaches **files, the network, processes, native memory, reflection or other parts of the host** that the policy blocks. In the REPL's own policy, and in the policy of a host that uses `Sigil.Sound.Repl.Core` with its documented profile options.
- A way to make the validator accept a restricted member that was meant to be blocked, including by naming it indirectly (as a delegate, through an alias, or through a language feature that hides the call).
- A flaw in the packages' build or release (for example a tampered artifact) that would put malicious code into a published package.

These are **out of scope**, and are ordinary bugs to report as issues:

- A snippet that uses a lot of CPU or memory, loops forever, or crashes the page or host. The execution guards are a best effort, not a hard limit. A host that runs scripts it does not trust should isolate them (a separate process, container or browser tab) and set its own limits.
- Findings that need the attacker to already control the host application, the machine or the browser.
- Problems in third-party software we depend on (Roslyn, .NET, Blazor); please report those upstream.

## What the validator is and is not

The validator is a strong filter, not a proof. It is built to be hard to get around and is tested against a growing list of hostile snippets, but **a determined attacker may find a route we missed**. That is why reports are welcome, and why a host should treat it as one layer. The browser REPL runs everything inside your own browser's sandbox.

Thank you for helping keep users safe.
