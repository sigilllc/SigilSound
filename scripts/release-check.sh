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
    for id in Sigil.Sound Sigil.Sound.Notes Sigil.Sound.Testing; do
        for ext in nupkg snupkg; do
            [[ -f "$packages/$id.$version.$ext" ]] || fail "missing $packages/$id.$version.$ext"
        done
        [[ -f "$packages/$id.$version.nupkg" ]] || continue
        listing=$(unzip -Z1 "$packages/$id.$version.nupkg")
        for want in README.md LICENSE "lib/net8.0/$id.dll" "lib/net8.0/$id.xml" "lib/net10.0/$id.dll" "lib/net10.0/$id.xml"; do
            grep -qxF "$want" <<< "$listing" || fail "$id.$version.nupkg has no $want"
        done
    done
    count=$(find "$packages" -type f | wc -l | tr -d ' ')
    [[ "$count" -eq 6 ]] || fail "expected 6 package files in $packages (3 nupkg, 3 snupkg), found $count"
fi

# A clean consumer outside the repo (so Directory.Build.props does not apply) restores the packages from the
# local feed, builds them for net8.0 and net10.0, and renders a little audio on each.
if [[ -n "$packages" && "$status" -eq 0 ]]; then
    work=$(mktemp -d)
    work_w=$(cd "$work" && pwd -W 2>/dev/null || pwd)
    feed_w=$(cd "$packages" && pwd -W 2>/dev/null || pwd)
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
EOF
    dotnet restore "$work_w/Consumer.csproj" --source "$feed_w" --verbosity quiet
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
