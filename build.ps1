# Compila BetaClock.exe usando el compilador integrado de .NET Framework.
# No requiere instalar nada.
$ErrorActionPreference = 'Stop'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe" }

$src = Join-Path $dir "BetaClock.cs"
$i18n = Join-Path $dir "BetaClockI18n.cs"
$out = Join-Path $dir "BetaClock.exe"

$icon = Join-Path $dir "clock.ico"
$iconArg = if (Test-Path $icon) { "/win32icon:$icon" } else { "" }
& $csc /noconfig /nologo /codepage:65001 /target:winexe /optimize+ /out:"$out" $iconArg `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    "$src" "$i18n"

if (Test-Path $out) { Write-Host "OK -> $out" } else { Write-Host "FALLO la compilacion" }
