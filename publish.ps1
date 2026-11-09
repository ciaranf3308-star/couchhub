# CouchHub v0.2 — publish a portable self-contained win-x64 release.
# Run on Windows from the repo root, with the .NET 10 SDK installed:
#   powershell -ExecutionPolicy Bypass -File .\publish.ps1
# Output: .\publish\CouchHub\  (copy this whole folder to the mini PC)
# No trimming, no AOT, no admin required. The target PC needs no .NET runtime.

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$outDir = Join-Path $root 'publish\CouchHub'
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }

Write-Host 'Publishing CouchHub (self-contained, win-x64, Release)...' -ForegroundColor Cyan
dotnet publish .\src\CouchHub\CouchHub.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o $outDir

if (-not (Test-Path (Join-Path $outDir 'CouchHub.exe'))) {
    throw 'Publish finished but CouchHub.exe was not found in the output folder.'
}

$exe = Get-Item (Join-Path $outDir 'CouchHub.exe')
Write-Host ''
Write-Host "Done. Portable release: $outDir" -ForegroundColor Green
Write-Host "CouchHub.exe: $([math]::Round($exe.Length / 1MB, 1)) MB" -ForegroundColor Green
Write-Host 'Copy the whole folder to the mini PC. No install step, no admin rights needed.' -ForegroundColor Green
Write-Host 'First run creates %LOCALAPPDATA%\CouchHub\config.json with empty launch targets.' -ForegroundColor Green
