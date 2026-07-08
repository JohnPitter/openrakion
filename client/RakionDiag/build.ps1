# build.ps1 — compila a entitydiff.dll (x86) via cl do VS BuildTools.
# Uso:  .\build.ps1            (usa o VS 2022 BuildTools)
#       .\build.ps1 -Vcvars "C:\caminho\vcvarsall.bat"   (outro VS)
param(
    [string]$Vcvars = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvarsall.bat"
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$out  = 'C:\temp\entitydiff.dll'

if (-not (Test-Path $Vcvars)) {
    # tenta achar qualquer vcvarsall (Community/Professional/Enterprise/BuildTools)
    $found = Get-ChildItem "C:\Program Files*\Microsoft Visual Studio" -Recurse -Filter vcvarsall.bat -ErrorAction SilentlyContinue |
             Select-Object -First 1 -ExpandProperty FullName
    if (-not $found) { throw "vcvarsall.bat não encontrado. Passe -Vcvars com o caminho do seu VS." }
    $Vcvars = $found
}

New-Item -ItemType Directory -Force 'C:\temp' | Out-Null
# vcvarsall precisa rodar em cmd (seta o ambiente x86) e encadear o cl na MESMA sessão.
$cmd = "call `"$Vcvars`" x86 && cd /d `"$here`" && cl /nologo /LD /O2 /MT /EHsc entitydiff.cpp /Fe:$out"
& cmd.exe /c $cmd
if ($LASTEXITCODE -ne 0) { throw "cl falhou (exit $LASTEXITCODE)" }

if (Test-Path $out) { Write-Host "OK -> $out" -ForegroundColor Green }
else { throw "compilou mas $out não apareceu" }
