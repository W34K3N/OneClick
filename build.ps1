# Build WinSetup.exe with the stock .NET Framework compiler (no SDK required).
# Run:  powershell -ExecutionPolicy Bypass -File build.ps1
# NOTE: keep this file ASCII-only. PowerShell 5.1 reads BOM-less UTF-8 as ANSI,
#       and Cyrillic text breaks the parser.

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcDir = Join-Path $root 'src'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$fxDir = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$outExe = Join-Path $root 'OneClick.exe'
$icoFile = Join-Path $root 'OneClick.ico'

if (-not (Test-Path $csc)) {
    throw ('Compiler not found: ' + $csc)
}

$sources = @(Get-ChildItem $srcDir -Filter *.cs -File | Sort-Object Name | ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) {
    throw ('No .cs files in ' + $srcDir)
}

$refs = @(
    (Join-Path $fxDir 'System.dll'),
    (Join-Path $fxDir 'System.Core.dll'),
    (Join-Path $fxDir 'System.Drawing.dll'),
    (Join-Path $fxDir 'System.Windows.Forms.dll'),
    (Join-Path $fxDir 'System.Web.Extensions.dll')
)

foreach ($r in $refs) {
    if (-not (Test-Path $r)) { throw ('Missing assembly: ' + $r) }
}

$cscArgs = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/codepage:65001',
    ('/out:' + $outExe)
)

foreach ($r in $refs) { $cscArgs += ('/reference:' + $r) }
if (Test-Path $icoFile) { $cscArgs += ('/win32icon:' + $icoFile) }
foreach ($s in $sources) { $cscArgs += $s }

Write-Host ('Compiler : ' + $csc)
Write-Host ('Sources  : ' + $sources.Count)
foreach ($s in $sources) { Write-Host ('  ' + (Split-Path $s -Leaf)) }
Write-Host ('Output   : ' + $outExe)
Write-Host ''

& $csc $cscArgs
$code = $LASTEXITCODE

if ($code -ne 0) {
    Write-Host ''
    Write-Host ('BUILD FAILED, exit code ' + $code)
    exit $code
}

if (Test-Path $outExe) {
    $fi = Get-Item $outExe

    # app.config -> OneClick.exe.config (ASCII-only copy, no xdt)
    $cfgIn = Join-Path $srcDir 'app.config'
    if (Test-Path $cfgIn) {
        $cfgOut = $outExe + '.config'
        Copy-Item $cfgIn $cfgOut -Force
        Write-Host ('Config : ' + $cfgOut)
    } else {
        Write-Host 'WARNING: src\app.config not found - long paths may fail'
    }

    Write-Host ''
    Write-Host ('OK: ' + $fi.FullName)
    Write-Host ('Size: ' + [math]::Round($fi.Length / 1KB, 1) + ' KB')
    Write-Host ('Time: ' + $fi.LastWriteTime)
} else {
    Write-Host 'BUILD finished without errors but no output file was created'
    exit 1
}
