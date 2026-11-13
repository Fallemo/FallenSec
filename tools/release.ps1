# Builds FallenSec, creates FallenSec-vX.Y.zip (+ .sha256) and, with -Publish, a GitHub release.
# The in-app updater needs BOTH assets: FallenSec-vX.Y.zip and FallenSec-vX.Y.zip.sha256.
# Version = AppInfoConst.Version in Core\Common.cs (must match the csproj FileVersion).
# Release notes (English, markdown) are required for -Publish: ..\release\notes\vX.Y.md
# Run from Windows PowerShell:  powershell -ExecutionPolicy Bypass -File tools\release.ps1 [-Publish]
param([switch]$Publish)
$ErrorActionPreference = 'Stop'
$src = Split-Path $PSScriptRoot -Parent
$root = Split-Path $src -Parent
$app = Join-Path $root 'app\FallenSec'
$out = Join-Path $root 'release'
$dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet-sdk\dotnet.exe"
$gh = "$env:LOCALAPPDATA\Microsoft\WinGet\Links\gh.exe"
$repo = 'Fallemo/FallenSec'

$common = [IO.File]::ReadAllText((Join-Path $src 'Core\Common.cs'), [Text.Encoding]::UTF8)
$ver = [regex]::Match($common, 'Version = "([0-9.]+)"').Groups[1].Value
if (-not $ver) { throw 'AppInfoConst.Version not found in Core\Common.cs' }
"Version: $ver"

if (Test-Path $app) { Remove-Item $app -Recurse -Force }
& $dotnet publish $src -c Release -r win-x64 --self-contained false -nologo -v q -o $app
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Get-ChildItem $app -Include *.pdb, *.xml -Recurse | Remove-Item -Force
foreach ($f in 'CITESTE-MA.txt', 'README.txt', 'LICENSE', 'THIRD-PARTY-NOTICES.md') { Copy-Item (Join-Path $src $f) $app -Force }

$exe = Join-Path $app 'FallenSec.exe'
$fv = (Get-Item $exe).VersionInfo.FileVersion
if (-not $fv.StartsWith($ver)) { throw "FileVersion ($fv) does not match AppInfoConst.Version ($ver) - update the csproj" }
$bytes = [IO.File]::ReadAllBytes($exe)
$text = [Text.Encoding]::ASCII.GetString($bytes) + [Text.Encoding]::Unicode.GetString($bytes)
if (-not $text.Contains('requireAdministrator')) { throw 'The exe has no requireAdministrator manifest (NoAdmin build?)' }

New-Item -ItemType Directory -Force $out | Out-Null
$zipName = "FallenSec-v$ver.zip"
$zip = Join-Path $out $zipName
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path $app -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$sha = "$zip.sha256"
[IO.File]::WriteAllText($sha, "$hash  $zipName`n")
Copy-Item $zip ([Environment]::GetFolderPath('Desktop')) -Force
"Zip: $zip (also copied to the Desktop)"
"SHA-256: $hash"

if ($Publish) {
    $notes = Join-Path $out "notes\v$ver.md"
    if (-not (Test-Path $notes)) { throw "Missing English release notes: $notes" }
    & $gh release create "v$ver" $zip $sha --repo $repo --title "FallenSec $ver" --notes-file $notes --latest
    if ($LASTEXITCODE -ne 0) { throw 'GitHub release failed' }
    "Published: https://github.com/$repo/releases/tag/v$ver"
}
