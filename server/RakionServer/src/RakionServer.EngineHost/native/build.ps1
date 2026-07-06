# Compila o engine_host.exe (x86, DEP OFF) e o instala no Bin do cliente.
# DEP OFF (/NXCOMPAT:NO) e' OBRIGATORIO: a EntitiesMP.dll e' ASPack e o stub de unpack executa em pagina
# de dados nao-executavel; com DEP ligado (padrao do MSVC moderno) isso da AV no entry-point.
# Toolchain: MSVC BuildTools 2022 (cl.exe x86). g++ MinGW nao serve (so 64-bit) e o CLR nao hospeda a engine.
param(
    [string]$Bin = $(if ($env:RAKION_BIN) { $env:RAKION_BIN } else { "C:\Users\joaop\Desenvolvimento\Rakion\rakion-final\Bin" })
)
$ErrorActionPreference = "Stop"
$src = Join-Path $PSScriptRoot "engine_host.cpp"
$out = Join-Path $Bin "engine_host.exe"   # vive no Bin: a SE1 deriva o data-root do dir do .exe

$vcvars = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars32.bat"
if (-not (Test-Path $vcvars)) { throw "vcvars32.bat nao encontrado em $vcvars (instale MSVC BuildTools x86)" }

$tmp = Join-Path $env:TEMP "ehbuild"
New-Item -ItemType Directory -Force $tmp | Out-Null
Push-Location $tmp
try {
    cmd /c "`"$vcvars`" >nul 2>&1 && cl /nologo /EHa /O2 /Fe:`"$out`" `"$src`" /link /NXCOMPAT:NO"
    if (-not (Test-Path $out)) { throw "build falhou" }
    Write-Host "OK -> $out ($((Get-Item $out).Length) bytes)"
} finally { Pop-Location }
