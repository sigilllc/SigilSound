<#
.SYNOPSIS  Builds the API docs site (DocFX) and optionally serves it with the REPL beside it.
.EXAMPLE   scripts/docs.ps1            # build docs/_site
.EXAMPLE   scripts/docs.ps1 -Serve     # build, publish the REPL into _site/repl, serve on http://localhost:8080
#>
param([switch]$Serve, [int]$Port = 8080)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    dotnet tool restore | Out-Null
    Push-Location docs
    dotnet docfx docfx.json
    if ($LASTEXITCODE) { throw 'docfx failed' }
    Pop-Location

    if ($Serve) {
        $repl = Join-Path $root 'docs/_site/repl'
        dotnet publish apps/Sigil.Sound.Repl -c Release -o "$root/artifacts/repl-publish"
        if ($LASTEXITCODE) { throw 'REPL publish failed' }
        if (Test-Path $repl) { Remove-Item $repl -Recurse -Force }
        Copy-Item "$root/artifacts/repl-publish/wwwroot" $repl -Recurse
        # The REPL is served from /repl/, not from the site root.
        $index = Join-Path $repl 'index.html'
        (Get-Content $index -Raw).Replace('<base href="/" />', '<base href="/repl/" />') | Set-Content $index -NoNewline
        dotnet docfx serve docs/_site -p $Port
    }
}
finally { Pop-Location -ErrorAction SilentlyContinue }
