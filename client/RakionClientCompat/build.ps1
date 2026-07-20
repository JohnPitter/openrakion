param(
    [string]$PatchedExe = $env:RAKION_PATCHED_EXE,
    [string]$OriginalExe = $env:RAKION_ORIGINAL_EXE
)
$ErrorActionPreference = 'Stop'
$patchHeader = Join-Path $PSScriptRoot 'baked_patches.h'
if ($PatchedExe -and $OriginalExe) {
    if (-not (Test-Path -LiteralPath $PatchedExe)) { throw "rakion-final patcheado não encontrado: $PatchedExe" }
    if (-not (Test-Path -LiteralPath $OriginalExe)) { throw "rakion.exe.orig não encontrado: $OriginalExe" }
    python (Join-Path $PSScriptRoot 'gen_patch_table.py') $PatchedExe $OriginalExe $patchHeader
    if ($LASTEXITCODE -ne 0) { throw "geração da tabela de patches falhou: $LASTEXITCODE" }
    $engine = Join-Path (Split-Path -Parent $PatchedExe) 'engine.dll'
    if (-not (Test-Path -LiteralPath $engine)) { throw "engine.dll golden não encontrada: $engine" }
    python (Join-Path $PSScriptRoot 'verify_engine.py') $engine
    if ($LASTEXITCODE -ne 0) { throw "validação da engine.dll falhou: $LASTEXITCODE" }
}
if (-not (Test-Path -LiteralPath $patchHeader)) { throw 'baked_patches.h ausente' }
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'Visual Studio Build Tools com C++ x86 não encontrado' }

$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars32.bat'
$out = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$legacyForwarder = Join-Path $out 'verorig.dll'
if (Test-Path -LiteralPath $legacyForwarder -PathType Leaf) {
    Remove-Item -LiteralPath $legacyForwarder -Force
}

$patchSources = @(
    'rakion_client_patch.cpp',
    'cash_store.cpp',
    'client_patches.cpp',
    'ui_lifecycle_patch.cpp',
    'bot_telemetry.cpp',
    'compat_log.cpp'
) | ForEach-Object { '"{0}"' -f (Join-Path $PSScriptRoot $_) }
$patch = 'call "{0}" >nul && cl /nologo /std:c++20 /O2 /MT /EHsc /LD /W4 /WX {1} /link /Brepro /OUT:"{2}"' -f `
    $vcvars, ($patchSources -join ' '), (Join-Path $out 'RakionClientPatch.dll')
$proxy = 'call "{0}" >nul && cl /nologo /std:c++20 /O2 /MT /EHsc /LD /W4 /WX "{1}" /link /Brepro /DEF:"{2}" /OUT:"{3}"' -f `
    $vcvars, (Join-Path $PSScriptRoot 'version_proxy.cpp'), (Join-Path $PSScriptRoot 'version_proxy.def'), (Join-Path $out 'version.dll')
$smoke = 'call "{0}" >nul && cl /nologo /std:c++20 /O2 /MT /EHsc /W4 /WX "{1}" /link /Brepro /OUT:"{2}"' -f `
    $vcvars, (Join-Path $PSScriptRoot 'proxy_smoke.cpp'), (Join-Path $out 'proxy_smoke.exe')

Push-Location $out
try
{
    cmd.exe /d /c $patch
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    cmd.exe /d /c $proxy
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    cmd.exe /d /c $smoke
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & .\proxy_smoke.exe
    if ($LASTEXITCODE -ne 0) { throw "Smoke test do proxy falhou: $LASTEXITCODE" }
}
finally { Pop-Location }
