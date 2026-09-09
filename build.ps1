# Builds BetaClock.exe with the C# compiler that ships with .NET Framework.
# Nothing to install.
$ErrorActionPreference = 'Stop'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe" }

# Every .cs in the folder: ClockForm is split across several `partial class` files.
$sources = Get-ChildItem -Path $dir -Filter *.cs | Sort-Object Name | ForEach-Object { $_.FullName }
if ($sources.Count -eq 0) { throw "No .cs sources found in $dir" }
Write-Host ("Compiling {0} source files..." -f $sources.Count)

$out = Join-Path $dir "BetaClock.exe"
$icon = Join-Path $dir "clock.ico"
$iconArg = if (Test-Path $icon) { "/win32icon:$icon" } else { "" }

& $csc /noconfig /nologo /codepage:65001 /target:winexe /optimize+ /out:"$out" $iconArg `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    $sources

if (Test-Path $out) { Write-Host "OK -> $out" } else { Write-Host "BUILD FAILED" }
