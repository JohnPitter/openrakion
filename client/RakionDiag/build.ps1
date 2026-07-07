# Compila addremote_hook.dll (x86) — DLL de DIAGNÓSTICO dev-only p/ RE do trigger de AddRemotePlayer.
# Toolchain: MSVC BuildTools 2022 (cl.exe x86), mesma do engine_host.
param(
    [string]$Out = $(Join-Path $PSScriptRoot "addremote_hook.dll")
)
$ErrorActionPreference = "Stop"
$src = Join-Path $PSScriptRoot "addremote_hook.cpp"

$vcvars = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars32.bat"
if (-not (Test-Path $vcvars)) { throw "vcvars32.bat nao encontrado em $vcvars (instale MSVC BuildTools x86)" }

$tmp = Join-Path $env:TEMP "diagbuild"
New-Item -ItemType Directory -Force $tmp | Out-Null
Push-Location $tmp
try {
    cmd /c "`"$vcvars`" >nul 2>&1 && cl /nologo /LD /EHa /O2 /Fe:`"$Out`" `"$src`""
    if (-not (Test-Path $Out)) { throw "build falhou" }
    Write-Host "OK -> $Out ($((Get-Item $Out).Length) bytes)"
} finally { Pop-Location }
