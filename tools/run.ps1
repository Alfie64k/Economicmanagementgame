# Build and launch the game on Windows.  Usage: .\tools\run.ps1 [-SkipTests] [-Godot "C:\path\Godot_v4.3-stable_mono_win64.exe"]
param([switch]$SkipTests, [string]$Godot = $env:GODOT)
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  throw "dotnet not found. Install the .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0"
}
if (-not $Godot) {
  $cmd = Get-Command godot, godot4 -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($cmd) { $Godot = $cmd.Source }
}
if (-not $Godot -or -not (Test-Path $Godot)) {
  throw "Godot 4.3 .NET not found. Download the .NET edition from https://godotengine.org/download and pass -Godot <path to the .exe> (use the non-_console exe)."
}

if (-not $SkipTests) { Write-Host "== simulation tests"; dotnet test EconGame.sln --nologo -v q; if ($LASTEXITCODE) { throw "tests failed" } }
Write-Host "== building game"; dotnet build game/EconomicGame.csproj --nologo -v q; if ($LASTEXITCODE) { throw "build failed" }
Write-Host "== launching"; & $Godot --path game
