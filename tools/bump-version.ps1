<#
.SYNOPSIS
Sets the package version everywhere it lives, so no release ships with the five copies disagreeing.

.DESCRIPTION
One version number lives in five places: the csproj <Version>, the umbraco-package.json "version", and a ?v=
cache-buster on each asset path in that manifest. The build workflow fails when they drift, which makes a manual
bump a game of find-them-all. This is the find-them-all.

.EXAMPLE
./tools/bump-version.ps1 0.1.0-alpha003

Then commit, and tag the release with the same number: git tag 0.1.0-alpha003
#>
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'

# The same shape the release workflow compares against the tag: semver with an optional prerelease suffix.
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z][0-9A-Za-z\.]*)?$') {
    throw "'$Version' is not a version this package ships as. Expected something like 0.1.0 or 0.1.0-alpha003."
}

$root = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $root 'src\Initials.AutoLink\Initials.AutoLink.csproj'
$manifest = Join-Path $root 'src\Initials.AutoLink\wwwroot\umbraco-package.json'

# ReadAllText/WriteAllText rather than Get-Content/Set-Content: no re-encoding, no BOM surprises, and the
# file's own final-newline state survives.
$csprojText = [System.IO.File]::ReadAllText($csproj)
$updatedCsproj = $csprojText -replace '<Version>[^<]+</Version>', "<Version>$Version</Version>"
if ($updatedCsproj -eq $csprojText -and $csprojText -notmatch [regex]::Escape("<Version>$Version</Version>")) {
    throw "No <Version> element found in $csproj."
}

$manifestText = [System.IO.File]::ReadAllText($manifest)
$updatedManifest = $manifestText -replace '("version"\s*:\s*")[^"]+(")', "`${1}$Version`${2}"
$updatedManifest = $updatedManifest -replace '\?v=[^"]+', "?v=$Version"
if ($updatedManifest -notmatch [regex]::Escape("`"version`": `"$Version`"")) {
    throw "No version field found in $manifest."
}

[System.IO.File]::WriteAllText($csproj, $updatedCsproj)
[System.IO.File]::WriteAllText($manifest, $updatedManifest)

$busters = ([regex]::Matches($updatedManifest, [regex]::Escape("?v=$Version"))).Count
Write-Host "csproj    <Version>          -> $Version"
Write-Host "manifest  version            -> $Version"
Write-Host "manifest  ?v= cache-busters  -> $Version ($busters of them)"
Write-Host ""
Write-Host "Now: commit, then 'git tag $Version && git push origin $Version' to release."
