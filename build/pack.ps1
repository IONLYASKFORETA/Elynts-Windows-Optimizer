# Builds the app and its service and packs them into a Velopack release for one channel.
# Used by CI (beta) and by the manual promote-to-stable workflow, so both channels get identical builds.
#
#   ./build/pack.ps1 -Version 1.0.42 -Channel beta
#
# Output: out/releases (Setup.exe, the full and delta packages, and releases.<channel>.json).

param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [ValidateSet('beta', 'stable')] [string] $Channel,
    # When set, earlier releases of the channel are downloaded first so Velopack can build a small delta update.
    [string] $ReleasesRepo = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$pack = Join-Path $root 'out/pack'
$releases = Join-Path $root 'out/releases'
Remove-Item $pack, $releases -Recurse -Force -ErrorAction SilentlyContinue

function Invoke-Checked([scriptblock] $command) {
    & $command
    if ($LASTEXITCODE -ne 0) { throw "Failed ($LASTEXITCODE): $command" }
}

# The app at the root of the package; the service in service\, which the app copies to Program Files itself.
# Both carry the same version, which is how the app tells that its installed service is out of date.
Invoke-Checked { dotnet publish "$root/src/Elynt.App" -c Release -r win-x64 --self-contained -p:Version=$Version -o $pack }
Invoke-Checked { dotnet publish "$root/src/Elynt.Service" -c Release -r win-x64 --self-contained -p:Version=$Version -o "$pack/service" }

if ($ReleasesRepo) {
    # No releases yet on a brand-new channel is fine: the first package simply has no delta.
    vpk download github --repoUrl $ReleasesRepo --channel $Channel --outputDir $releases
}

Invoke-Checked {
    vpk pack `
        --packId ElyntOptimizer `
        --packTitle "Elynt's Windows Optimizer" `
        --packAuthors 'Aden' `
        --packVersion $Version `
        --packDir $pack `
        --mainExe ElyntOptimizer.exe `
        --channel $Channel `
        --outputDir $releases
}
