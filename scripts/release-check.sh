#!/usr/bin/env bash
# Release checks for the Sigil.Sound packages. release.yml runs this; run it locally before tagging too.
#
#   bash scripts/release-check.sh <version>                   version, VersionPrefix, public API, changelog
#   bash scripts/release-check.sh <version> <packages-dir>    also the package files and a clean consumer
#
# Tests are not run here: run them locally before tagging (project convention).
set -euo pipefail
cd "$(dirname "$0")/.."

version="${1:?usage: release-check.sh <version> [packages-dir]}"
packages="${2:-}"
status=0
fail() { echo "error: $*" >&2; status=1; }

# A version is 1.2.3 or 1.2.3-rc.1 (SemVer 2 without build metadata, which NuGet accepts).
if [[ "$version" =~ ^([0-9]+\.[0-9]+\.[0-9]+)(-[0-9A-Za-z.-]+)?$ ]]; then
    base="${BASH_REMATCH[1]}"
else
    echo "error: '$version' is not a version such as 1.2.3 or 1.2.3-rc.1" >&2
    exit 1
fi

# The base version must be the one the code declares.
prefix=$(tr -d '\r' < Directory.Build.props | sed -n 's:.*<VersionPrefix>\([^<]*\)</VersionPrefix>.*:\1:p')
[[ "$base" == "$prefix" ]] || fail "$version has base $base, but Directory.Build.props VersionPrefix is $prefix"

# A release ships the whole public API. Anything still in Unshipped must move to Shipped first (launch plan C2).
for file in src/*/PublicAPI.Unshipped.txt; do
    extra=$(tr -d '\r' < "$file" | grep -vE '^(#nullable enable)?[[:space:]]*$' || true)
    if [[ -n "$extra" ]]; then
        fail "$file lists API that is not shipped yet. Move these lines to the matching PublicAPI.Shipped.txt:"
        echo "$extra" >&2
    fi
done

# The release notes are the changelog section for this exact version.
changelog=$(tr -d '\r' < CHANGELOG.md)
grep -qxF "## $version" <<< "$changelog" || fail "CHANGELOG.md has no '## $version' section"

if [[ -n "$packages" ]]; then
    # Sigil.Sound.Repl.Core is the snippet compiler for hosts such as the Workbench. It targets net10.0 only and ships no XML docs file.
    for id in Sigil.Sound Sigil.Sound.Notes Sigil.Sound.Testing Sigil.Sound.Repl.Core; do
        for ext in nupkg snupkg; do
            [[ -f "$packages/$id.$version.$ext" ]] || fail "missing $packages/$id.$version.$ext"
        done
        [[ -f "$packages/$id.$version.nupkg" ]] || continue
        listing=$(unzip -Z1 "$packages/$id.$version.nupkg")
        wants=(README.md LICENSE "lib/net10.0/$id.dll")
        if [[ "$id" != "Sigil.Sound.Repl.Core" ]]; then
            wants+=("lib/net8.0/$id.dll" "lib/net8.0/$id.xml" "lib/net10.0/$id.xml")
        fi
        for want in "${wants[@]}"; do
            grep -qxF "$want" <<< "$listing" || fail "$id.$version.nupkg has no $want"
        done
    done
    count=$(find "$packages" -type f | wc -l | tr -d ' ')
    [[ "$count" -eq 8 ]] || fail "expected 8 package files in $packages (4 nupkg, 4 snupkg), found $count"
fi

# A clean consumer outside the repo (so Directory.Build.props does not apply) restores the packages from the
# local feed, builds them for net8.0 and net10.0, and renders a little audio on each.
if [[ -n "$packages" && "$status" -eq 0 ]]; then
    work=$(mktemp -d)
    work_w=$(cd "$work" && pwd -W 2>/dev/null || pwd)
    feed_w=$(cd "$packages" && pwd -W 2>/dev/null || pwd)
    # The consumer gets its own package cache. Otherwise the candidate lands in the machine's cache, and a later restore of the
    # same version (the next dry run, or the published one) would silently use those stale bits instead of what was just packed.
    export NUGET_PACKAGES="$work_w/packages"
    cat > "$work/Consumer.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Sigil.Sound.Notes" Version="$version" />
    <PackageReference Include="Sigil.Sound.Testing" Version="$version" />
    <PackageReference Include="Sigil.Sound.Repl.Core" Version="$version" Condition="'\$(TargetFramework)' == 'net10.0'" />
  </ItemGroup>
</Project>
EOF
    cat > "$work/Program.cs" <<'EOF'
using Sigil.Sound;
using Sigil.Sound.Testing;
using static Sigil.Sound.Sounds;

var notes = Notes.Parse("C4 E4 G4");
var audio = Sine(440).Adsr(0.01, 0.1, 0.7, 0.2).Render(0.25);
var peak = SignalAnalysis.Peak(audio.ToMono());
if (notes.Count != 3 || peak <= 0f)
    throw new InvalidOperationException($"smoke failed: {notes.Count} notes, peak {peak}");
Console.WriteLine($"ok: {notes.Count} notes, peak {peak:F3}");

#if NET10_0_OR_GREATER
// Sigil.Sound.Repl.Core targets net10.0 only: compile a snippet through the real Roslyn pipeline and render it.
var compiled = Sigil.Sound.Repl.SnippetCompiler.Compile("Sine(440)");
if (!compiled.Success)
    throw new InvalidOperationException("snippet failed: " + string.Join("; ", compiled.Diagnostics.Select(d => d.Message)));
var snippetPeak = SignalAnalysis.Peak(compiled.Run().Render(0.05).ToMono());
if (snippetPeak <= 0f)
    throw new InvalidOperationException("the snippet rendered silence");
Console.WriteLine($"ok: snippet compiled and rendered, peak {snippetPeak:F3}");
#endif
EOF
    # The local feed holds the Sigil packages (not published yet at this point); Roslyn, a dependency of Repl.Core, comes from nuget.org.
    # A config file rather than --source: Git Bash on Windows rewrites a URL given on the command line into a local path.
    cat > "$work/nuget.config" <<EOF
<configuration>
  <packageSources>
    <clear />
    <add key="release-candidate" value="$feed_w" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF
    dotnet restore "$work_w/Consumer.csproj" --verbosity quiet
    dotnet build "$work_w/Consumer.csproj" -c Release --no-restore --verbosity quiet
    # Both runtimes by default (CI installs both). SMOKE_TFMS="net10.0" limits the run on a machine without the 8.0 runtime.
    for tfm in ${SMOKE_TFMS:-net8.0 net10.0}; do
        dotnet run --project "$work_w/Consumer.csproj" -c Release --no-build -f "$tfm"
    done
    rm -rf "$work"
fi

if [[ "$status" -eq 0 ]]; then
    echo "release checks passed for $version"
fi
exit "$status"
