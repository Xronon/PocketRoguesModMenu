# Builds the two release archives into dist\:
#   PocketRoguesCheats-<version>-full.zip      BepInEx 5.4.23.5 (x64, unmodified) + the plugin
#   PocketRoguesCheats-<version>-mod-only.zip  the plugin only, for an existing BepInEx 5
#
#   powershell -ExecutionPolicy Bypass -File package.ps1 -BepInExZip <path to BepInEx_win_x64_5.4.23.5.zip>
#
# Run build.cmd first. The BepInEx archive (from https://github.com/BepInEx/BepInEx/releases)
# is checked by SHA-256 before anything is packed; its files go into the full package byte for
# byte. Nothing from an installed game folder is packed: no settings, no logs, no caches.
# ASCII only: Windows PowerShell 5.1 reads scripts without a BOM in the system code page.
param(
    [Parameter(Mandatory = $true)][string]$BepInExZip
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$expected = '82f9878551030f54657792c0740d9d51a09500eeae1fba21106b0c441e6732c4'

if (-not (Test-Path -LiteralPath $BepInExZip)) { throw "BepInEx archive not found: $BepInExZip" }
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $BepInExZip).Hash.ToLowerInvariant()
if ($hash -ne $expected) { throw "BepInEx archive SHA-256 mismatch: $hash (expected $expected, BepInEx_win_x64_5.4.23.5.zip)" }

$dll = Join-Path $here 'bin\PocketRoguesCheats.dll'
if (-not (Test-Path -LiteralPath $dll)) { throw 'bin\PocketRoguesCheats.dll not found - run build.cmd first' }

# version - the one the mod reports in its log (CheatsPlugin.Version)
$src = [System.IO.File]::ReadAllText((Join-Path $here 'src\CheatsPlugin.cs'))
$m = [regex]::Match($src, 'public const string Version = "([^"]+)"')
if (-not $m.Success) { throw 'version not found in src\CheatsPlugin.cs' }
$version = $m.Groups[1].Value

$dist = Join-Path $here 'dist'
if (-not (Test-Path -LiteralPath $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }

# texts for players, next to the game's exe inside the archive
$docs = @(
    @{ From = 'README.md';    To = 'Pocket Rogues Cheats - README.txt' },
    @{ From = 'README.ru.md'; To = 'Pocket Rogues Cheats - README (RU).txt' },
    @{ From = 'LICENSE';      To = 'Pocket Rogues Cheats - LICENSE.txt' }
)

# the only BepInEx setting the full package brings: keep the loader's object out of the game's
# scene changes (the mod runs on its own hidden object anyway; this just keeps the log quiet)
$bepCfg = "[Chainloader]`r`n`r`n## If enabled, hides BepInEx Manager GameObject from Unity.`r`nHideManagerGameObject = true`r`n"

function New-Archive([string]$path) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    return [System.IO.Compression.ZipFile]::Open($path, [System.IO.Compression.ZipArchiveMode]::Create)
}

function Add-File($zip, [string]$file, [string]$entry) {
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file, $entry, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
}

function Add-Text($zip, [string]$text, [string]$entry) {
    $e = $zip.CreateEntry($entry, [System.IO.Compression.CompressionLevel]::Optimal)
    $w = New-Object System.IO.StreamWriter($e.Open(), (New-Object System.Text.UTF8Encoding($false)))
    $w.Write($text)
    $w.Dispose()
}

function Add-Docs($zip) {
    foreach ($d in $docs) { Add-File $zip (Join-Path $here $d.From) $d.To }
}

# --- full package -------------------------------------------------------------------------
$full = Join-Path $dist "PocketRoguesCheats-$version-full.zip"
$zip = New-Archive $full
try {
    $bep = [System.IO.Compression.ZipFile]::OpenRead($BepInExZip)
    try {
        foreach ($e in $bep.Entries) {
            if ($e.FullName.EndsWith('/')) { continue }
            $name = $e.FullName.Replace('\', '/')
            $out = $zip.CreateEntry($name, [System.IO.Compression.CompressionLevel]::Optimal)
            $out.LastWriteTime = $e.LastWriteTime
            $r = $e.Open(); $w = $out.Open()
            $r.CopyTo($w)
            $w.Dispose(); $r.Dispose()
        }
    } finally { $bep.Dispose() }
    Add-File $zip $dll 'BepInEx/plugins/PocketRoguesCheats.dll'
    Add-Text $zip $bepCfg 'BepInEx/config/BepInEx.cfg'
    Add-Docs $zip
} finally { $zip.Dispose() }
Write-Host "[OK] $full"

# --- mod only -----------------------------------------------------------------------------
$only = Join-Path $dist "PocketRoguesCheats-$version-mod-only.zip"
$zip = New-Archive $only
try {
    Add-File $zip $dll 'BepInEx/plugins/PocketRoguesCheats.dll'
    Add-Docs $zip
} finally { $zip.Dispose() }
Write-Host "[OK] $only"
