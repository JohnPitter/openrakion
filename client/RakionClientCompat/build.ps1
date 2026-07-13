$ErrorActionPreference = 'Stop'
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'Visual Studio Build Tools com C++ x86 não encontrado' }

$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars32.bat'
$out = Join-Path $PSScriptRoot 'bin'
$systemVersion = Join-Path $env:WINDIR 'SysWOW64\version.dll'
New-Item -ItemType Directory -Path $out -Force | Out-Null
Copy-Item -LiteralPath $systemVersion -Destination (Join-Path $out 'verorig.dll') -Force

$proxy = 'call "{0}" >nul && cl /nologo /std:c++20 /O2 /MT /EHsc /LD /W4 /WX "{1}" /link /DEF:"{2}" /OUT:"{3}"' -f `
    $vcvars, (Join-Path $PSScriptRoot 'version_proxy.cpp'), (Join-Path $PSScriptRoot 'version_proxy.def'), (Join-Path $out 'version.dll')
$smoke = 'call "{0}" >nul && cl /nologo /std:c++20 /O2 /MT /EHsc /W4 /WX "{1}" /link /OUT:"{2}"' -f `
    $vcvars, (Join-Path $PSScriptRoot 'proxy_smoke.cpp'), (Join-Path $out 'proxy_smoke.exe')

Push-Location $out
try
{
    cmd.exe /d /c $proxy
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    cmd.exe /d /c $smoke
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & .\proxy_smoke.exe
    if ($LASTEXITCODE -ne 0) { throw "Smoke test do proxy falhou: $LASTEXITCODE" }
}
finally { Pop-Location }
