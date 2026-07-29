param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Start', 'Stop', 'Status')]
    [string]$Action,

    [string[]]$ClientRoots = @(),

    [string]$OutputDirectory = 'C:\temp\openrakion-human-match'
)

$ErrorActionPreference = 'Stop'
$activeMarker = Join-Path ([IO.Path]::GetTempPath()) 'openrakion-human-match.active'

function Resolve-ClientBin([string]$root) {
    $resolved = (Resolve-Path -LiteralPath $root).Path
    $bin = if (Test-Path -LiteralPath (Join-Path $resolved 'Bin\rakion.exe')) {
        Join-Path $resolved 'Bin'
    } elseif (Test-Path -LiteralPath (Join-Path $resolved 'rakion.exe')) {
        $resolved
    } else {
        throw "rakion.exe não encontrado em '$resolved' ou em '$resolved\Bin'."
    }
    return (Resolve-Path -LiteralPath $bin).Path
}

function Resolve-ClientRoots {
    if ($ClientRoots.Count -gt 0) {
        return @($ClientRoots)
    }
    $configured = $env:RAKION_CAPTURE_CLIENT_ROOTS
    if ([string]::IsNullOrWhiteSpace($configured)) {
        throw 'Informe -ClientRoots ou defina RAKION_CAPTURE_CLIENT_ROOTS separado por ponto e vírgula.'
    }
    return @($configured.Split(';', [StringSplitOptions]::RemoveEmptyEntries))
}

function Read-ActiveDirectory {
    if (-not (Test-Path -LiteralPath $activeMarker)) {
        return $null
    }
    $directory = (Get-Content -LiteralPath $activeMarker -Raw).Trim()
    if ([string]::IsNullOrWhiteSpace($directory)) {
        throw "Marcador ativo inválido: $activeMarker"
    }
    return $directory
}

function Remove-OwnedMarker([string]$path, [string]$directory) {
    if (-not (Test-Path -LiteralPath $path)) {
        return
    }
    $owner = (Get-Content -LiteralPath $path -Raw).Trim()
    if ($owner -eq $directory) {
        Remove-Item -LiteralPath $path -Force
    }
}

function Start-Capture {
    $active = Read-ActiveDirectory
    if ($active) {
        throw "Já existe captura ativa em '$active'."
    }
    $running = @(Get-Process -Name rakion -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) {
        throw 'Feche todos os clientes rakion.exe antes de iniciar a captura.'
    }

    $bins = @(Resolve-ClientRoots | ForEach-Object { Resolve-ClientBin $_ } |
        Sort-Object -Unique)
    $sessionName = Get-Date -Format 'yyyyMMdd-HHmmss'
    $sessionDirectory = Join-Path $OutputDirectory $sessionName
    New-Item -ItemType Directory -Path $sessionDirectory -Force | Out-Null

    $metadata = [ordered]@{
        startUtc = [DateTimeOffset]::UtcNow.ToString('o')
        startTick = [Environment]::TickCount64
        clientRoots = $bins
        formatVersion = 1
    }
    $metadata | ConvertTo-Json -Depth 4 |
        Set-Content -LiteralPath (Join-Path $sessionDirectory 'session.json') -Encoding utf8
    foreach ($bin in $bins) {
        Set-Content -LiteralPath (Join-Path $bin 'action.capture') `
            -Value $sessionDirectory -Encoding ascii -NoNewline
    }
    Set-Content -LiteralPath $activeMarker `
        -Value $sessionDirectory -Encoding ascii -NoNewline

    Write-Host "Captura ativa: $sessionDirectory"
    Write-Host 'Abra os dois clientes, jogue a partida completa e use Stop ao terminar.'
}

function Stop-Capture {
    $sessionDirectory = Read-ActiveDirectory
    if (-not $sessionDirectory) {
        throw 'Não existe captura humano x humano ativa.'
    }
    $sessionPath = Join-Path $sessionDirectory 'session.json'
    $metadata = Get-Content -LiteralPath $sessionPath -Raw | ConvertFrom-Json

    foreach ($bin in @($metadata.clientRoots)) {
        Remove-OwnedMarker (Join-Path $bin 'action.capture') $sessionDirectory
    }
    Remove-OwnedMarker $activeMarker $sessionDirectory
    Start-Sleep -Milliseconds 750

    $finalizer = Join-Path $PSScriptRoot 'finalize_human_match_capture.py'
    & python $finalizer $sessionDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "Falha ao consolidar captura: código $LASTEXITCODE."
    }
    Write-Host "Captura finalizada: $sessionDirectory"
    Write-Host "Timeline: $(Join-Path $sessionDirectory 'timeline.csv')"
    Write-Host "Resumo: $(Join-Path $sessionDirectory 'summary.md')"
}

function Show-Status {
    $sessionDirectory = Read-ActiveDirectory
    if (-not $sessionDirectory) {
        Write-Host 'Captura humano x humano inativa.'
        return
    }
    $files = @(Get-ChildItem -LiteralPath $sessionDirectory -File)
    $clients = @(Get-Process -Name rakion -ErrorAction SilentlyContinue)
    Write-Host "Captura ativa: $sessionDirectory"
    Write-Host "Clientes abertos: $($clients.Count)"
    Write-Host "Arquivos coletados: $($files.Count)"
}

switch ($Action) {
    'Start' { Start-Capture }
    'Stop' { Stop-Capture }
    'Status' { Show-Status }
}
