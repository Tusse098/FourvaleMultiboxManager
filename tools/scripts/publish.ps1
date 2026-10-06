# Builds a self-contained release of Fourvale Multibox Manager for players (no .NET SDK needed to run it).
# Output: artifacts\publish\FourvaleMultibox\, artifacts\FourvaleMultibox-<version>-win-x64.zip and its .sha256
# A folder publish, not single-file: WebView2's native loader stays a normal file next to the exe.
# Used locally and by .github/workflows/release.yml; release steps: docs/development.md "Making a release".
#
#   powershell -ExecutionPolicy Bypass -File tools\scripts\publish.ps1 [-SkipTests] [-ExpectedVersion 0.2.0]

param(
    [switch]$SkipTests,
    # The release workflow passes the tag's version; the build stops if <Version> in the project differs.
    [string]$ExpectedVersion
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$project = Join-Path $root 'src\Multibox.App\Multibox.App.csproj'
$artifacts = Join-Path $root 'artifacts'
$out = Join-Path $artifacts 'publish\FourvaleMultibox'

[xml]$csproj = Get-Content $project
$version = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw 'No <Version> in Multibox.App.csproj.' }
if ($ExpectedVersion -and $ExpectedVersion -ne $version) {
    throw "Tag says $ExpectedVersion but Multibox.App.csproj says $version. Change <Version>, commit, and tag again."
}

if (-not $SkipTests) {
    dotnet test (Join-Path $root 'FourvaleMultibox.slnx') --artifacts-path (Join-Path $artifacts 'test')
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed; nothing published.' }
}

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
dotnet publish $project -c Release -r win-x64 --self-contained true -o $out
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

foreach ($required in 'Multibox.App.exe', 'multibox.json', 'fourvale-adapter.json', 'WebView2Loader.dll') {
    if (-not (Test-Path (Join-Path $out $required))) { throw "Publish output is missing $required." }
}

Get-ChildItem $out -Filter '*.pdb' | Remove-Item
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $out 'LICENSE.txt')
Copy-Item (Join-Path $root 'README.md') (Join-Path $out 'README.md')

$zip = Join-Path $artifacts "FourvaleMultibox-$version-win-x64.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path $out -DestinationPath $zip

# Players can check the download: Get-FileHash <zip> -Algorithm SHA256
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$zip.sha256" -Value "$hash  $(Split-Path $zip -Leaf)" -Encoding ascii
Write-Host "Published $version -> $zip"
Write-Host "SHA-256 $hash"
