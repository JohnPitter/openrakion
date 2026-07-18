[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory = $true)]
    [string]$TargetRoot,
    [Parameter(Mandatory = $true)]
    [string]$GoldenRoot,
    [string]$ServerHost = '127.0.0.1',
    [ValidateSet('windowed', 'borderless', 'fullscreen')]
    [string]$DisplayMode = 'windowed',
    [switch]$Refresh
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

function Backup-Destination([string]$Destination, [string]$RelativePath)
{
    if (-not (Test-Path -LiteralPath $Destination -PathType Leaf)) { return }
    $backup = Join-Path $script:BackupRoot $RelativePath
    New-Item -ItemType Directory -Path (Split-Path -Parent $backup) -Force | Out-Null
    Copy-Item -LiteralPath $Destination -Destination $backup -Force
}

function Test-SameFile([string]$Source, [string]$Destination)
{
    if (-not (Test-Path -LiteralPath $Destination -PathType Leaf)) { return $false }
    return (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash -eq
        (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash
}

function Assert-FileInstallable([string]$Source, [string]$RelativePath)
{
    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        throw "artefato de origem ausente: $Source"
    }
    $destination = Join-Path $script:Target $RelativePath
    if (Test-SameFile $Source $destination) { return }
    if (-not (Test-Path -LiteralPath $destination -PathType Leaf)) { return }

    try {
        $stream = [System.IO.File]::Open(
            $destination,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None)
        $stream.Dispose()
    } catch {
        throw "destino em uso ou sem permissão: $destination"
    }
}

function Prepare-Destination([string]$Destination, [string]$RelativePath)
{
    $existed = Test-Path -LiteralPath $Destination -PathType Leaf
    Backup-Destination $Destination $RelativePath
    $script:Touched.Add([pscustomobject]@{
        Destination = $Destination
        RelativePath = $RelativePath
        Existed = $existed
    })
}

function Install-File([string]$Source, [string]$RelativePath)
{
    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        throw "artefato de origem ausente: $Source"
    }
    $destination = Join-Path $script:Target $RelativePath
    $hash = (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash
    if (Test-SameFile $Source $destination) {
        $script:Installed[$RelativePath] = $hash
        return
    }
    Prepare-Destination $destination $RelativePath
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $Source -Destination $destination -Force
    $script:Installed[$RelativePath] = $hash
}

function Install-Text([string]$Content, [string]$RelativePath)
{
    $destination = Join-Path $script:Target $RelativePath
    $expected = "$Content`r`n"
    if ((Test-Path -LiteralPath $destination -PathType Leaf) -and
        [System.IO.File]::ReadAllText($destination) -eq $expected) {
        $script:Installed[$RelativePath] = (Get-FileHash $destination -Algorithm SHA256).Hash
        return
    }
    Prepare-Destination $destination $RelativePath
    [System.IO.File]::WriteAllText($destination, $expected, [System.Text.UTF8Encoding]::new($false))
    $script:Installed[$RelativePath] = (Get-FileHash $destination -Algorithm SHA256).Hash
}

function Restore-TouchedFiles()
{
    for ($index = $script:Touched.Count - 1; $index -ge 0; $index--) {
        $entry = $script:Touched[$index]
        $backup = Join-Path $script:BackupRoot $entry.RelativePath
        if ($entry.Existed) {
            Copy-Item -LiteralPath $backup -Destination $entry.Destination -Force
        } elseif (Test-Path -LiteralPath $entry.Destination -PathType Leaf) {
            Remove-Item -LiteralPath $entry.Destination -Force
        }
    }
}

$ip = [System.Net.IPAddress]::None
if (-not [System.Net.IPAddress]::TryParse($ServerHost, [ref]$ip) -or
    $ip.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
    throw "server.host deve ser um IPv4: $ServerHost"
}

$script:Target = Resolve-ExistingDirectory $TargetRoot 'diretório de validação'
$golden = Resolve-ExistingDirectory $GoldenRoot 'rakion-final golden'
if ($script:Target -eq $golden) { throw 'o diretório de validação não pode ser o golden source' }
$existingManifestPath = Join-Path $script:Target 'validation-install.json'
$existingManifest = if (Test-Path -LiteralPath $existingManifestPath -PathType Leaf) {
    Get-Content -LiteralPath $existingManifestPath -Raw | ConvertFrom-Json
}
if ($existingManifest -and -not $Refresh) {
    throw 'uma validação já está instalada; use -Refresh para atualizar preservando o backup original'
}

$goldenBin = Join-Path $golden 'Bin'
$pristineExe = Join-Path $goldenBin 'rakion.exe.orig'
$patchedExe = Join-Path $goldenBin 'rakion.exe'
$compatRoot = $PSScriptRoot
$launcherProject = Join-Path $PSScriptRoot '..\RakionLauncher\RakionLauncher.csproj'
$publishDir = Join-Path $PSScriptRoot '..\RakionLauncher\bin\validation-publish'

& (Join-Path $compatRoot 'build.ps1') -PatchedExe $patchedExe -OriginalExe $pristineExe
if ($LASTEXITCODE -ne 0) { throw "build da DLL falhou: $LASTEXITCODE" }

$dotnet = Find-DotNetSdk
& $dotnet publish $launcherProject -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=false -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "publish do launcher falhou: $LASTEXITCODE" }

if (-not $PSCmdlet.ShouldProcess($script:Target, 'instalar baseline pristine v258 e DLL')) { return }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$script:BackupRoot = Join-Path $script:Target ".validation-backup\$stamp"
$originalBackupRoot = if ($existingManifest) {
    if ($existingManifest.originalBackupRoot) {
        $existingManifest.originalBackupRoot
    } else {
        $existingManifest.backupRoot
    }
} else {
    $script:BackupRoot
}
$script:Installed = [ordered]@{}
$script:Touched = [System.Collections.Generic.List[object]]::new()
$filePlan = [System.Collections.Generic.List[object]]::new()
$filePlan.Add([pscustomobject]@{ Source = (Join-Path $golden 'DataSetup.xfs'); RelativePath = 'DataSetup.xfs' })
$filePlan.Add([pscustomobject]@{ Source = (Join-Path $golden 'Data\SeriousSam.gms'); RelativePath = 'Data\SeriousSam.gms' })
$filePlan.Add([pscustomobject]@{ Source = (Join-Path $goldenBin 'engine.dll'); RelativePath = 'Bin\engine.dll' })
$filePlan.Add([pscustomobject]@{ Source = $pristineExe; RelativePath = 'Bin\rakion.exe' })
$filePlan.Add([pscustomobject]@{ Source = (Join-Path $compatRoot 'bin\version.dll'); RelativePath = 'Bin\version.dll' })
$filePlan.Add([pscustomobject]@{ Source = (Join-Path $compatRoot 'bin\verorig.dll'); RelativePath = 'Bin\verorig.dll' })
Get-ChildItem $publishDir -File | Where-Object { $_.Extension -ne '.pdb' } | ForEach-Object {
    $filePlan.Add([pscustomobject]@{ Source = $_.FullName; RelativePath = $_.Name })
}

foreach ($entry in $filePlan) {
    Assert-FileInstallable $entry.Source $entry.RelativePath
}

$manifestPath = Join-Path $script:Target 'validation-install.json'
try {
    foreach ($entry in $filePlan) {
        Install-File $entry.Source $entry.RelativePath
    }
    Install-Text $ServerHost 'server.host'
    Install-Text $DisplayMode 'display.mode'

    $manifest = [ordered]@{
        schema = 1
        installedAtUtc = [DateTime]::UtcNow.ToString('O')
        goldenRoot = $golden
        targetRoot = $script:Target
        backupRoot = $script:BackupRoot
        originalBackupRoot = $originalBackupRoot
        baseline = [ordered]@{
            rakionExeOriginalSha256 = (Get-FileHash $pristineExe -Algorithm SHA256).Hash
            rakionExePatchedSha256 = (Get-FileHash $patchedExe -Algorithm SHA256).Hash
            engineSha256 = (Get-FileHash (Join-Path $goldenBin 'engine.dll') -Algorithm SHA256).Hash
        }
        files = $script:Installed
    }
    Prepare-Destination $manifestPath 'validation-install.json'
    [System.IO.File]::WriteAllText(
        $manifestPath,
        ($manifest | ConvertTo-Json -Depth 5),
        [System.Text.UTF8Encoding]::new($false))
} catch {
    Restore-TouchedFiles
    throw
}

Write-Host "Validação v258 instalada em $script:Target"
Write-Host "Backup dos arquivos substituídos: $script:BackupRoot"
Write-Host 'O jogo não foi iniciado.'
