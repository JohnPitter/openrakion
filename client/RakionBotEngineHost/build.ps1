param(
    [string]$ClientRoot,
    [switch]$RunProbe,
    [switch]$RunIpcProbe,
    [string]$World = 'LevelsSV\Mammoth\Mammoth.wld'
)

$ErrorActionPreference = 'Stop'
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -products * `
    -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
    -property installationPath
if (-not $vs) {
    throw 'Visual Studio Build Tools com C++ x86 não encontrado'
}

$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars32.bat'
$out = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$entitiesImportLibrary = Join-Path $out 'entitiesmp_host.lib'
$entitiesDefinition = Join-Path $PSScriptRoot 'entitiesmp_host.def'
$importCommand = 'call "{0}" >nul && lib /nologo /machine:x86 /def:"{1}" /out:"{2}"' -f `
    $vcvars, $entitiesDefinition, $entitiesImportLibrary
cmd.exe /d /c $importCommand
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$sources = @(
    (Join-Path $PSScriptRoot 'main.cpp'),
    (Join-Path $PSScriptRoot 'engine_runtime.cpp'),
    (Join-Path $PSScriptRoot 'host_paths.cpp'),
    (Join-Path $PSScriptRoot 'pipe_server.cpp')
) | ForEach-Object { '"{0}"' -f $_ }
$output = Join-Path $out 'BotEngineHost.exe'
$command = 'call "{0}" >nul && cl /nologo /std:c++20 /O2 /MT /EHsc /W4 /WX {1} "{2}" /link /Brepro /SUBSYSTEM:CONSOLE /DYNAMICBASE:NO /NXCOMPAT:NO /OUT:"{3}"' -f `
    $vcvars, ($sources -join ' '), $entitiesImportLibrary, $output

Push-Location $out
try {
    cmd.exe /d /c $command
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
finally {
    Pop-Location
}

if (-not ($RunProbe -or $RunIpcProbe)) {
    return
}
if (-not $ClientRoot) {
    throw 'ClientRoot é obrigatório para executar probes'
}

$clientRootPath = (Resolve-Path -LiteralPath $ClientRoot).Path
$clientBin = Join-Path $clientRootPath 'Bin'
$engine = Join-Path $clientBin 'engine.dll'
if (-not (Test-Path -LiteralPath $engine -PathType Leaf)) {
    throw "engine.dll não encontrada em $clientBin"
}

$deployedHost = Join-Path $clientBin 'BotEngineHost.exe'
Copy-Item -LiteralPath $output -Destination $deployedHost -Force
if ($RunProbe) {
    & $deployedHost --client-root $clientRootPath --world $World
    if ($LASTEXITCODE -ne 0) {
        throw "Probe do Bot Engine Host falhou: $LASTEXITCODE"
    }
}
if ($RunIpcProbe) {
    & (Join-Path $PSScriptRoot 'ipc_smoke.ps1') `
        -HostPath $deployedHost `
        -World $World
}
