[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$GoldenRoot,
    [string]$ServerHost = '127.0.0.1',
    [string]$CashStoreUrl,
    [string]$LauncherBaseUrl,
    [string]$OutputRoot,
    [ValidateSet('windowed', 'borderless', 'fullscreen')]
    [string]$DisplayMode = 'windowed',
    [switch]$EnableUpdates,
    [switch]$EnableTicketAuth = $true,
    [string]$PublicKeyPath
)

$ErrorActionPreference = 'Stop'

function Resolve-ExistingDirectory([string]$Path, [string]$Label)
{
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "$Label não encontrado: $Path"
    }
    return (Resolve-Path -LiteralPath $Path).Path.TrimEnd('\')
}

function Find-DotNetSdk()
{
    $candidates = @(
        $(if ($env:DOTNET_ROOT) { Join-Path $env:DOTNET_ROOT 'dotnet.exe' }),
        (Join-Path $HOME '.dotnet\dotnet.exe'),
        $(if (Get-Command dotnet.exe -ErrorAction SilentlyContinue) {
            (Get-Command dotnet.exe).Source
        })
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -Unique

    foreach ($candidate in $candidates) {
        $sdks = & $candidate --list-sdks 2>$null
        if ($LASTEXITCODE -eq 0 -and $sdks) { return $candidate }
    }
    throw '.NET SDK não encontrado'
}

function Resolve-HttpUri([string]$Value, [string]$Label, [bool]$RequireSecureRemote)
{
    $uri = [Uri]::new($Value, [UriKind]::Absolute)
    if ($uri.Scheme -notin @('http', 'https')) {
        throw "$Label deve ser uma URL HTTP(S) absoluta"
    }
    if ($RequireSecureRemote -and -not $uri.IsLoopback -and $uri.Scheme -ne 'https') {
        throw "$Label remota exige HTTPS"
    }
    return $uri
}

function Copy-PackageFile([string]$Source, [string]$RelativePath, [string]$DestinationRoot)
{
    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        throw "artefato obrigatório ausente: $Source"
    }
    $destination = Join-Path $DestinationRoot $RelativePath
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $Source -Destination $destination -Force
}

function Write-PackageText([string]$DestinationRoot, [string]$RelativePath, [string]$Content)
{
    $destination = Join-Path $DestinationRoot $RelativePath
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    [System.IO.File]::WriteAllText(
        $destination, "$Content`r`n", [System.Text.UTF8Encoding]::new($false))
}

function Test-SameOrParentPath([string]$Candidate, [string]$Path)
{
    $parent = [System.IO.Path]::GetFullPath($Candidate).TrimEnd('\')
    $child = [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
    return $child.Equals($parent, [StringComparison]::OrdinalIgnoreCase) -or
        $child.StartsWith("$parent\", [StringComparison]::OrdinalIgnoreCase)
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$golden = Resolve-ExistingDirectory $GoldenRoot 'cliente v258 golden'
$output = [System.IO.Path]::GetFullPath($(if ($OutputRoot) {
    $OutputRoot
} else {
    Join-Path $repositoryRoot 'artifacts\client-v258-overlay'
}))
if ($output -eq [System.IO.Path]::GetPathRoot($output) -or
    (Test-SameOrParentPath $output $repositoryRoot) -or
    (Test-SameOrParentPath $output $golden)) {
    throw "OutputRoot inseguro: $output"
}
if ((Test-Path -LiteralPath $output -PathType Container) -and
    -not (Test-Path -LiteralPath (Join-Path $output 'client-package.json') -PathType Leaf)) {
    throw "OutputRoot já existe e não é um pacote gerado: $output"
}

$ip = [System.Net.IPAddress]::None
if (-not [System.Net.IPAddress]::TryParse($ServerHost, [ref]$ip) -or
    $ip.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
    throw "ServerHost deve ser um IPv4: $ServerHost"
}
if (-not $CashStoreUrl) { $CashStoreUrl = "http://$ServerHost/cash" }
if (-not $LauncherBaseUrl) { $LauncherBaseUrl = "http://$ServerHost/" }
$cashStoreUri = Resolve-HttpUri $CashStoreUrl 'CashStoreUrl' $false
$launcherUri = Resolve-HttpUri $LauncherBaseUrl 'LauncherBaseUrl' $true
if ($EnableUpdates -and -not $PublicKeyPath) {
    throw 'PublicKeyPath é obrigatório quando EnableUpdates está ativo'
}

$goldenBin = Join-Path $golden 'Bin'
$pristineExe = Join-Path $goldenBin 'rakion.exe.orig'
$patchedExe = Join-Path $goldenBin 'rakion.exe'
$compatRoot = Join-Path $PSScriptRoot 'RakionClientCompat'
$botHostRoot = Join-Path $PSScriptRoot 'RakionBotEngineHost'
$launcherProject = Join-Path $PSScriptRoot 'RakionLauncher\RakionLauncher.csproj'
$publishRoot = Join-Path $PSScriptRoot 'RakionLauncher\bin\package-publish'
$staging = "$output.staging-$([Guid]::NewGuid().ToString('N'))"

& (Join-Path $compatRoot 'build.ps1') -PatchedExe $patchedExe -OriginalExe $pristineExe
if ($LASTEXITCODE -ne 0) { throw "build das DLLs falhou: $LASTEXITCODE" }
# O Bot Engine Host mora no Bin do cliente: SE_InitEngine deriva o diretório de dados do
# caminho do executável. Sem ele no pacote, a instalação limpa não tem bots.
& (Join-Path $botHostRoot 'build.ps1')
if ($LASTEXITCODE -ne 0) { throw "build do Bot Engine Host falhou: $LASTEXITCODE" }
$dotnet = Find-DotNetSdk
& $dotnet publish $launcherProject -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publishRoot
if ($LASTEXITCODE -ne 0) { throw "publish do launcher falhou: $LASTEXITCODE" }

try {
    New-Item -ItemType Directory -Path $staging -Force | Out-Null
    Copy-PackageFile (Join-Path $golden 'DataSetup.xfs') 'DataSetup.xfs' $staging
    Copy-PackageFile (Join-Path $golden 'Data\SeriousSam.gms') 'Data\SeriousSam.gms' $staging
    Copy-PackageFile (Join-Path $goldenBin 'engine.dll') 'Bin\engine.dll' $staging
    Copy-PackageFile $pristineExe 'Bin\rakion.exe' $staging
    Copy-PackageFile (Join-Path $compatRoot 'bin\version.dll') 'Bin\version.dll' $staging
    Copy-PackageFile (Join-Path $compatRoot 'bin\RakionClientPatch.dll') `
        'Bin\RakionClientPatch.dll' $staging
    Copy-PackageFile (Join-Path $botHostRoot 'bin\BotEngineHost.exe') `
        'Bin\BotEngineHost.exe' $staging
    Copy-PackageFile (Join-Path $PSScriptRoot 'verify_client_package.ps1') `
        'verify-package.ps1' $staging
    Get-ChildItem -LiteralPath $publishRoot -File | Where-Object {
        $_.Extension -ne '.pdb' -and $_.Name -ne 'launcher.settings.json'
    } | ForEach-Object { Copy-PackageFile $_.FullName $_.Name $staging }
    if ($PublicKeyPath) {
        Copy-PackageFile $PublicKeyPath 'update-public.pem' $staging
    }

    Write-PackageText $staging 'server.host' $ServerHost
    Write-PackageText $staging 'cash-shop.url' $cashStoreUri.AbsoluteUri.TrimEnd('/')
    Write-PackageText $staging 'display.mode' $DisplayMode
    $readme = @'
PACOTE OPENRAKION v258

1. Feche o jogo e o launcher.
2. Faça backup do cliente original.
3. Execute: powershell -ExecutionPolicy Bypass -File .\verify-package.ps1
4. Copie todo o conteúdo desta pasta para a raiz do cliente, preservando Bin e Data.
5. Inicie RakionLauncher.exe.

Não renomeie rakion.bin para rakion.exe e não misture este pacote com DLLs de outra versão.
'@
    Write-PackageText $staging 'LEIA-ME.txt' $readme.TrimEnd()
    $settings = [ordered]@{
        updatesEnabled = [bool]$EnableUpdates
        ticketAuthEnabled = [bool]$EnableTicketAuth
        updateBaseUrl = $launcherUri.AbsoluteUri
        appId = 11001
        baseVersion = 258
        publicKeyPemPath = 'update-public.pem'
    } | ConvertTo-Json
    Write-PackageText $staging 'launcher.settings.json' $settings

    $files = [ordered]@{}
    Get-ChildItem -LiteralPath $staging -Recurse -File | Sort-Object FullName | ForEach-Object {
        $relative = [System.IO.Path]::GetRelativePath($staging, $_.FullName)
        $files[$relative] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }
    $manifest = [ordered]@{
        schema = 1
        generatedAtUtc = [DateTime]::UtcNow.ToString('O')
        baseline = [ordered]@{
            rakionExeOriginalSha256 = (Get-FileHash $pristineExe -Algorithm SHA256).Hash
            rakionExePatchedSha256 = (Get-FileHash $patchedExe -Algorithm SHA256).Hash
            engineSha256 = (Get-FileHash (Join-Path $goldenBin 'engine.dll') -Algorithm SHA256).Hash
        }
        files = $files
    }
    Write-PackageText $staging 'client-package.json' ($manifest | ConvertTo-Json -Depth 5)

    if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
    Move-Item -LiteralPath $staging -Destination $output
} finally {
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
}

Write-Host "Pacote pronto: $output"
Write-Host 'Copie todo o conteúdo dessa pasta para a raiz do cliente original compatível.'
